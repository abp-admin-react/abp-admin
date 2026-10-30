using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 人工调账（定稿 §5.1）：建/提交/审批/驳回/冲销各看各的权限码（Finance.Adjust.*），不按角色写死；
/// 审批/驳回自己提交的单据额外要求 Finance.Adjust.ApproveOwn（默认不授予）。
/// 五态：Draft→Pending→Rejected/Posted；Rejected→Draft（reopen）；Posted→Reversed。
/// 改/删/提交/重拟只能由申请人做；审批通过在同一事务里过 manual-adjust/post 凭证。
/// </summary>
public class FinManualAdjustAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly LedgerPostingEngine _engine;
    private readonly PostingRuleProvider _rules;
    private readonly ILogger<FinManualAdjustAppService> _logger;

    public FinManualAdjustAppService(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        LedgerPostingEngine engine,
        PostingRuleProvider rules,
        ILogger<FinManualAdjustAppService> logger)
    {
        _dbContextProvider = dbContextProvider;
        _engine = engine;
        _rules = rules;
        _logger = logger;
    }

    public virtual async Task<FinManualAdjustDto> GetAsync(Guid id)
    {
        await RequireViewAsync();
        return await MapAsync(await LoadAsync(id));
    }

    /// <summary>
    /// 列表：任一 Voucher.View / Adjust.* 权限可看。Status 多值；StartTime/EndTime 为 CreationTime 左闭右开（UTC）；
    /// Filter 只匹配 reason。按 CreationTime 倒序分页。
    /// </summary>
    public virtual async Task<PagedResultDto<FinManualAdjustDto>> GetListAsync(FinManualAdjustListInput input)
    {
        await RequireViewAsync();
        var db = await _dbContextProvider.GetDbContextAsync();
        var q = db.ManualAdjusts.AsNoTracking();
        var statuses = ParseStatuses<FinManualAdjustStatus>(input.Status);
        if (statuses != null)
        {
            q = q.Where(x => statuses.Contains(x.Status));
        }

        if (input.ApplicantId != null)
        {
            q = q.Where(x => x.ApplicantId == input.ApplicantId);
        }

        if (input.StartTime != null)
        {
            q = q.Where(x => x.CreationTime >= input.StartTime);
        }

        if (input.EndTime != null)
        {
            q = q.Where(x => x.CreationTime < input.EndTime);
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            q = q.Where(x => x.Reason != null && x.Reason.Contains(input.Filter));
        }

        var total = await q.CountAsync();
        var (skip, take) = Paging(input);
        var items = await q.OrderByDescending(x => x.CreationTime).Skip(skip).Take(take).ToListAsync();

        // 整页科目一次取齐：原逐行 MapAsync 每行一发科目查询，页大小 100 时即 100 次额外查询，
        // 而一页涉及的科目去重后通常只有两三个
        var accounts = await _rules.GetAccountsByIdAsync(
            items.SelectMany(x => new[] { x.DebitAccountId, x.CreditAccountId }));
        var dtos = items.Select(x => Map(x, accounts)).ToList();

        return new PagedResultDto<FinManualAdjustDto>(total, dtos);
    }

    /// <summary>建草稿。reason 可空；借贷/金额校验见 BuildLinesAsync。需 Finance.Adjust.Create。</summary>
    public virtual async Task<FinManualAdjustDto> CreateAsync([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateUpdateFinManualAdjustDto? input = null)
    {
        await RequireAnyAsync(FinanceErrorCodes.AdjustCreateDenied, FinancePermissions.Adjust.Create);
        var applicant = RequireUserId();
        var lines = await BuildLinesAsync(input);
        var adjust = new FinManualAdjust(GuidGenerator.Create(), applicant, lines, input!.Reason, CurrentTenant.Id);
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.ManualAdjusts.AddAsync(adjust);
        await db.SaveChangesAsync();
        return await MapAsync(adjust);
    }

    /// <summary>改草稿：需 Finance.Adjust.Create 且为申请人（他人 → AdjustNotApplicant）；状态校验在实体 Update 内。</summary>
    public virtual async Task<FinManualAdjustDto> UpdateAsync(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateUpdateFinManualAdjustDto? input = null)
    {
        var adjust = await LoadForApplicantAsync(id);
        adjust.Update(await BuildLinesAsync(input), input!.Reason);
        await SaveAsync();
        return await MapAsync(adjust);
    }

    /// <summary>删单：需 Finance.Adjust.Create 且为申请人；仅实体允许的状态可删（EnsureDeletable）。</summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        var adjust = await LoadForApplicantAsync(id);
        adjust.EnsureDeletable();
        var db = await _dbContextProvider.GetDbContextAsync();
        db.ManualAdjusts.Remove(adjust);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Draft → Pending。需 Finance.Adjust.Submit 且为申请人。
    /// reason 必填：空/空白 → 400 AdjustInvalidInput，Fields=reason（与建单非法字段同形）。
    /// </summary>
    public virtual async Task<FinManualAdjustDto> SubmitAsync(Guid id)
    {
        var adjust = await LoadForApplicantAsync(id, FinanceErrorCodes.AdjustSubmitDenied, FinancePermissions.Adjust.Submit);
        if (string.IsNullOrWhiteSpace(adjust.Reason))
        {
            _logger.LogWarning("Adjust submit rejected: reason required. AdjustId={AdjustId} Code={Code}", id, FinanceErrorCodes.AdjustInvalidInput);
            throw new BusinessException(FinanceErrorCodes.AdjustInvalidInput).WithData("Fields", "reason");
        }

        adjust.Submit(Clock.Now);
        await SaveAsync();
        _logger.LogInformation("Adjust submitted. AdjustId={AdjustId} Status=Pending", id);
        return await MapAsync(adjust);
    }

    /// <summary>Rejected → Draft（POST /{id}/reopen）。仅申请人；他人 403 ReopenDenied；非 Rejected 400 InvalidStatus。</summary>
    public virtual async Task<FinManualAdjustDto> ReopenAsync(Guid id)
    {
        // 缺 Create 权限 → CreateDenied（403）；有 Create 但非制单人（单据归属）→ ReopenDenied（403）
        await RequireAnyAsync(FinanceErrorCodes.AdjustCreateDenied, FinancePermissions.Adjust.Create);
        var user = RequireUserId();
        var adjust = await LoadAsync(id); // 非 Rejected 时 Reopen() 抛 InvalidStatus
        if (adjust.ApplicantId != user)
        {
            throw new BusinessException(FinanceErrorCodes.AdjustReopenDenied).WithData("AdjustId", id);
        }

        adjust.Reopen();
        await SaveAsync();
        return await MapAsync(adjust);
    }

    /// <summary>
    /// Pending → Posted：同事务过 manual-adjust/post 凭证。需 Finance.Adjust.Approve；
    /// 自批另需 ApproveOwn，否则 403 SelfApproveNotAllowed（非 SelfApproveForbidden）。
    /// </summary>
    public virtual async Task<FinManualAdjustDto> ApproveAsync(Guid id)
    {
        var (adjust, auditor) = await LoadForAuditAsync(id, FinanceErrorCodes.AdjustApproveDenied, FinancePermissions.Adjust.Approve);
        // 按 Id 取借贷两侧科目（字典含两项），凭证事件要的是科目编码
        var debit = (await _rules.GetAccountsByIdAsync(new[] { adjust.DebitAccountId, adjust.CreditAccountId }));
        var evt = new FinanceBizEvent(
            FinanceBizTypes.ManualAdjust,
            adjust.Id.ToString("N"),
            FinanceBizTypes.Phases.Post,
            adjust.Currency,
            adjust.Amount,
            From: new AccountRef(debit[adjust.DebitAccountId].Code, adjust.DebitOwnerType, adjust.DebitOwnerId),
            To: new AccountRef(debit[adjust.CreditAccountId].Code, adjust.CreditOwnerType, adjust.CreditOwnerId),
            Summary: adjust.Reason,
            OperatorId: auditor);
        var voucherId = await _engine.PostAsync(evt);
        adjust.MarkPosted(auditor, voucherId, Clock.Now);
        await SaveAsync();
        _logger.LogInformation("Adjust approved and posted. AdjustId={AdjustId} VoucherId={VoucherId} AuditorId={AuditorId}", id, voucherId, auditor);
        return await MapAsync(adjust);
    }

    /// <summary>
    /// Pending → Rejected：需 Finance.Adjust.Reject。驳回原因必填（先于权限校验，空 → AdjustRejectReasonRequired）；
    /// 驳回自己的单同样受 ApproveOwn 约束（见 LoadForAuditAsync）。
    /// </summary>
    public virtual async Task<FinManualAdjustDto> RejectAsync(Guid id, RejectFinManualAdjustInput input)
    {
        if (string.IsNullOrWhiteSpace(input.RejectReason))
        {
            throw new BusinessException(FinanceErrorCodes.AdjustRejectReasonRequired);
        }

        var (adjust, auditor) = await LoadForAuditAsync(id, FinanceErrorCodes.AdjustRejectDenied, FinancePermissions.Adjust.Reject);
        adjust.Reject(auditor, input.RejectReason.Trim(), Clock.Now);
        await SaveAsync();
        _logger.LogInformation("Adjust rejected. AdjustId={AdjustId} AuditorId={AuditorId}", id, auditor);
        return await MapAsync(adjust);
    }

    /// <summary>
    /// Posted → Reversed：红冲调账凭证（Finance.Adjust.Reverse）。有意不走 LoadForAuditAsync 的
    /// 自批闸门：冲销与审批是两个独立权限（fin_boss 可冲销自己批过的单），设计见定稿 §5.2。
    /// </summary>
    public virtual async Task<FinManualAdjustDto> ReverseAsync(Guid id)
    {
        await RequireAnyAsync(FinanceErrorCodes.AdjustReverseDenied, FinancePermissions.Adjust.Reverse);
        var adjust = await LoadAsync(id);
        adjust.Require(FinManualAdjustStatus.Posted);
        var reversal = await _engine.ReverseVoucherAsync(adjust.VoucherId!.Value);
        adjust.MarkReversed(reversal); // 只改状态并记红冲凭证号，原 VoucherId 保留，下方日志同时打两者
        await SaveAsync();
        _logger.LogInformation("Adjust reversed. AdjustId={AdjustId} ReversalVoucherId={ReversalVoucherId} OriginalVoucherId={VoucherId}", id, reversal, adjust.VoucherId);
        return await MapAsync(adjust);
    }

    /// <summary>
    /// 审批/驳回共用：需对应权限 + Pending；申请人==审批人时必须另有 ApproveOwn，
    /// 否则抛 AdjustSelfApproveNotAllowed（403；勿与已废弃的 SelfApproveForbidden 混淆）。
    /// </summary>
    private async Task<(FinManualAdjust Adjust, Guid Auditor)> LoadForAuditAsync(Guid id, string deniedCode, string permission)
    {
        await RequireAnyAsync(deniedCode, permission);
        var auditor = RequireUserId();
        var adjust = await LoadAsync(id);
        adjust.Require(FinManualAdjustStatus.Pending);
        if (adjust.ApplicantId == auditor
            && !await AuthorizationService.IsGrantedAsync(FinancePermissions.Adjust.ApproveOwn))
        {
            _logger.LogWarning("Adjust self-approve blocked. AdjustId={AdjustId} AuditorId={AuditorId} Code={Code}", id, auditor, FinanceErrorCodes.AdjustSelfApproveNotAllowed);
            throw new BusinessException(FinanceErrorCodes.AdjustSelfApproveNotAllowed).WithData("AdjustId", id);
        }

        return (adjust, auditor);
    }

    private Task<FinManualAdjust> LoadForApplicantAsync(Guid id) =>
        LoadForApplicantAsync(id, FinanceErrorCodes.AdjustCreateDenied, FinancePermissions.Adjust.Create);

    private async Task<FinManualAdjust> LoadForApplicantAsync(Guid id, string deniedCode, string permission)
    {
        await RequireAnyAsync(deniedCode, permission);
        var user = RequireUserId();
        var adjust = await LoadAsync(id);
        if (adjust.ApplicantId != user)
        {
            throw new BusinessException(FinanceErrorCodes.AdjustNotApplicant).WithData("AdjustId", id);
        }

        return adjust;
    }

    private async Task<FinManualAdjust> LoadAsync(Guid id)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        return await db.ManualAdjusts.FirstOrDefaultAsync(x => x.Id == id)
               ?? throw new BusinessException(FinanceErrorCodes.AdjustNotFound).WithData("AdjustId", id);
    }

    private async Task SaveAsync()
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.SaveChangesAsync();
    }

    private Task RequireViewAsync() =>
        RequireAnyAsync(null,
            FinancePermissions.Voucher.View, FinancePermissions.Adjust.Create, FinancePermissions.Adjust.Submit,
            FinancePermissions.Adjust.Approve, FinancePermissions.Adjust.Reject, FinancePermissions.Adjust.Reverse);

    private async Task<FinManualAdjustLines> BuildLinesAsync(CreateUpdateFinManualAdjustDto? input)
    {
        var invalid = new System.Collections.Generic.List<string>();
        if (input?.Amount is not { } amount || amount <= 0 || amount > FinanceBizEvent.MaxAmount
            || decimal.Round(amount, 18) != amount) invalid.Add("amount");
        if (input?.Currency is not { } currency
            || currency.Trim() is not { Length: > 0 } trimmed
            || trimmed.Length > LedgerConsts.MaxCurrencyLength
            || trimmed.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            // 币种是子账户维度：字母数字形状收紧，任意符号串会开新维度污染科目×币余。
            // 币种白名单（CNY/USD/USDT…）是产品决策，留待财务口径定稿
            invalid.Add("currency");
        }
        if (string.IsNullOrWhiteSpace(input?.DebitAccountCode)) invalid.Add("debitAccountCode");
        if (invalid.Count > 0)
        {
            throw new BusinessException(FinanceErrorCodes.AdjustInvalidInput).WithData("Fields", string.Join(",", invalid));
        }

        var debit = await _rules.GetAccountAsync(input!.DebitAccountCode!);
        var credit = await _rules.GetAccountAsync(string.IsNullOrWhiteSpace(input.CreditAccountCode)
            ? FinanceAccountCodes.ManualAdjust
            : input.CreditAccountCode);
        var debitOwner = input.DebitOwnerId ?? "";
        var creditOwner = input.CreditOwnerId ?? "";
        if (debit.Id == credit.Id && input.DebitOwnerType == input.CreditOwnerType && debitOwner == creditOwner)
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "same sub-account on both sides");
        }

        // 校验与存储同一字符串：Trim + 大写归一后再入库（校验 Trim、存储原值会分叉，
        // 「CNY 」能建草稿、审批过账才被引擎形状校验打回，错误暴露点后移且存量脏数据语义不明）
        var normalizedCurrency = input.Currency!.Trim().ToUpperInvariant();
        return new FinManualAdjustLines(input.Amount!.Value, normalizedCurrency, debit.Id, input.DebitOwnerType, debitOwner,
            credit.Id, input.CreditOwnerType, creditOwner);
    }

    private async Task<FinManualAdjustDto> MapAsync(FinManualAdjust x)
    {
        var accounts = await _rules.GetAccountsByIdAsync(new[] { x.DebitAccountId, x.CreditAccountId });
        return Map(x, accounts);
    }

    /// <summary>纯映射（无 IO）。列表页传整页预取的科目字典，单条路径走 MapAsync。</summary>
    private static FinManualAdjustDto Map(FinManualAdjust x, System.Collections.Generic.Dictionary<Guid, FinAccount> accounts)
    {
        return new FinManualAdjustDto
        {
            Id = x.Id,
            ApplicantId = x.ApplicantId,
            AuditorId = x.AuditorId,
            Amount = x.Amount,
            Currency = x.Currency,
            DebitAccountCode = accounts.TryGetValue(x.DebitAccountId, out var d) ? d.Code : "",
            DebitOwnerType = x.DebitOwnerType,
            DebitOwnerId = x.DebitOwnerId,
            CreditAccountCode = accounts.TryGetValue(x.CreditAccountId, out var c) ? c.Code : "",
            CreditOwnerType = x.CreditOwnerType,
            CreditOwnerId = x.CreditOwnerId,
            Reason = x.Reason,
            RejectReason = x.RejectReason,
            Status = x.Status,
            SubmittedAt = x.SubmittedAt,
            AuditedAt = x.AuditedAt,
            VoucherId = x.VoucherId,
            ReversalVoucherId = x.ReversalVoucherId,
            CreationTime = x.CreationTime,
        };
    }
}
