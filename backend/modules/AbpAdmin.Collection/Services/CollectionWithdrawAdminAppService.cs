using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>审核/拒绝入参（拒绝原因必填）。</summary>
public class WithdrawAuditInput
{
    public Guid Id { get; set; }

    /// <summary>拒绝必填；审批可留空的备注。</summary>
    public string? Remark { get; set; }
}

/// <summary>配置提现热钱包（私钥经 body 传递，不进 URL/访问日志）。</summary>
public class SetWithdrawVaultInput
{
    public string Address { get; set; } = default!;

    public string PrivateKey { get; set; } = default!;
}

/// <summary>
/// 提现管理端（审核/拒绝/列表/状态）。广播重试、人工完结、热钱包状态在 M3 扩展。
/// Host 专用权限（WithdrawAdmin）+ AssertHost 双保险（同 Network.Admin 纪律）。
/// </summary>
[Authorize(CollectionPermissions.Withdraw.Admin)]
public class CollectionWithdrawAdminAppService : ApplicationService
{
    private readonly WithdrawManager _withdraws;
    private readonly WithdrawLedgerPoster _ledger;
    private readonly WithdrawGuard _guard;
    private readonly WithdrawBroadcastManager _broadcast;
    private readonly AbpAdmin.Collection.Chain.ChainGatewayFactory _gatewayFactory;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;

    public CollectionWithdrawAdminAppService(
        WithdrawManager withdraws,
        WithdrawLedgerPoster ledger,
        WithdrawGuard guard,
        WithdrawBroadcastManager broadcast,
        AbpAdmin.Collection.Chain.ChainGatewayFactory gatewayFactory,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings)
    {
        _withdraws = withdraws;
        _ledger = ledger;
        _guard = guard;
        _broadcast = broadcast;
        _gatewayFactory = gatewayFactory;
        _dbContextProvider = dbContextProvider;
        _settings = settings;
    }

    /// <summary>提现单列表（可按状态/用户过滤，倒序分页）。</summary>
    public virtual async Task<PagedResultDto<WithdrawOrderDto>> GetWithdrawalsAsync(
        int skipCount, int maxResultCount, string? status = null, Guid? userId = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.WithdrawOrders.AsNoTracking();
        if (!string.IsNullOrEmpty(status)
            && Enum.TryParse<AbpAdmin.Collection.Entities.WithdrawStatus>(status, true, out var st))
        {
            query = query.Where(o => o.Status == st);
        }

        if (userId.HasValue)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();

        return new PagedResultDto<WithdrawOrderDto>(
            total, rows.Select(o => new WithdrawOrderDto
            {
                Id = o.Id,
                Currency = o.Currency.ToString(),
                NetworkName = o.NetworkName,
                ToAddress = o.ToAddress,
                Amount = CollectionAmounts.Format(o.Amount),
                Fee = CollectionAmounts.Format(o.Fee),
                ActualAmount = CollectionAmounts.Format(o.ActualAmount),
                Status = o.Status.ToString(),
                TxHash = o.TxHash,
                RetryCount = o.RetryCount,
                FailReason = o.FailReason,
                AuditRemark = o.AuditRemark,
                AuditTime = o.AuditTime,
                CompleteTime = o.CompleteTime,
                CreationTime = o.CreationTime,
            }).ToList());
    }

    /// <summary>
    /// 审核通过（Pending → Processing：冻结出账、净额转应付、手续费转收入）。
    /// 审批前过护栏：热钱包偿付 + 熔断地板 + BNB 门槛（worker 小额自动审同款检查；
    /// 广播前 worker 还有一道，签名临界区内最后一道——三道检查对齐 exchange）。
    /// </summary>
    public virtual async Task ApproveAsync(WithdrawAuditInput input)
    {
        AssertHost();
        await EnsureSolvencyAsync(input.Id);
        await _withdraws.ApproveAsync(input.Id, CurrentUser.Id, input.Remark);
    }

    /// <summary>热钱包偿付护栏（审批端点共用；RPC 不可用时失败拒绝——fail-closed）。</summary>
    private async Task EnsureSolvencyAsync(Guid orderId)
    {
        var order = await _withdraws.FindAsync(orderId);
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstAsync()
            ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);

        var wallet = await _guard.LoadAsync()
                     ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
        var symbol = WithdrawLedgerPoster.CurrencyCode(order.Currency);
        var token = await dbContext.Tokens.AsNoTracking()
            .FirstAsync(t => t.NetworkId == network.Id && t.Symbol == symbol);
        await _guard.EnsureCanPayAsync(wallet, gateway, token, order.ActualAmount);
    }

    /// <summary>拒绝（Pending → Rejected：解冻、释放额度；原因必填）。</summary>
    public virtual async Task RejectAsync(WithdrawAuditInput input)
    {
        AssertHost();
        await _withdraws.RejectAsync(input.Id, CurrentUser.Id!.Value, input.Remark!);
    }

    /// <summary>
    /// 广播失败退款终态（BroadcastFailed → Rejected：净额+手续费退回用户可用；原因必填）。
    /// 前置：链上回执已核实未付出（M3 的重试/核实流程负责证明）。
    /// </summary>
    public virtual async Task RefundAsync(WithdrawAuditInput input)
    {
        AssertHost();
        await _withdraws.RefundAfterApproveAsync(input.Id, CurrentUser.Id!.Value, input.Remark!);
    }

    /// <summary>提现配置快照（前端状态卡：开关/总闸/限额/费率 + 各状态单量）。</summary>
    public virtual async Task<WithdrawStatusDto> GetStatusAsync()
    {
        AssertHost();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var counts = await dbContext.WithdrawOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        var byStatus = counts.ToDictionary(x => x.Key, x => x.Count);

        return new WithdrawStatusDto
        {
            Enabled = _withdraws.IsEnabled(),
            Halt = await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt),
            MinAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MinAmount) ?? "10",
            FeeRate = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.FeeRate) ?? "0.004",
            MaxDailyAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MaxDailyAmount) ?? "50000",
            MaxDailyCount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MaxDailyCount) ?? "3",
            AutoApproveMaxAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.AutoApproveMaxAmount) ?? "100",
            PendingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Pending, out var p) ? p : 0,
            ProcessingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Processing, out var pr) ? pr : 0,
            BroadcastingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Broadcasting, out var b) ? b : 0,
            BroadcastFailedCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.BroadcastFailed, out var bf) ? bf : 0,
        };
    }

    /// <summary>
    /// 配置提现热钱包（地址 + 私钥）：私钥与地址必须匹配（Nethereum 推导校验），
    /// 密文入库（版本前缀 + Collection:MasterKey），明文不落日志不回显。
    /// 与归集助记词必须不同源——泄露一个不应危及另一个。
    /// </summary>
    public virtual async Task SetWithdrawVaultAsync(SetWithdrawVaultInput input)
    {
        AssertHost();
        var address = (input.Address ?? string.Empty).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(address, "^0x[0-9a-fA-F]{40}$"))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress).WithData("ToAddress", address);
        }

        var privateKey = (input.PrivateKey ?? string.Empty).Trim();
        string derived;
        try
        {
            var key = new Nethereum.Signer.EthECKey(privateKey);
            derived = key.GetPublicAddress();
        }
        catch (Exception)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Reason", "private key malformed");
        }

        if (!string.Equals(derived, address, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("Reason", "private key does not match the address");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstAsync()
                      ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        network.WithdrawVaultAddress = address.ToLowerInvariant();
        network.WithdrawVaultKeyCipher = _guard.ProtectKey(privateKey);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>热钱包状态卡（余额/门槛/熔断；无密钥信息）。</summary>
    public virtual async Task<AbpAdmin.Collection.Withdrawals.WithdrawHotWalletStatusDto> GetHotWalletAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstAsync()
            ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
        var status = await _guard.GetStatusAsync(gateway);
        await uow.CompleteAsync();
        return status;
    }

    /// <summary>重试广播失败单（有哈希先验回执；无哈希过近期同地址付款双花门）。</summary>
    public virtual async Task RetryAsync(Guid id)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstAsync()
            ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
        await uow.CompleteAsync();
        await _broadcast.RetryAsync(id, network, gateway);
    }

    /// <summary>
    /// 人工完结（BroadcastFailed 单补录链上哈希）：强制查回执且状态必须成功；
    /// 金额匹配核对依赖人工（回执不含金额解析，v1 以哈希+成功状态+人工确认为准）。
    /// </summary>
    public virtual async Task CompleteAsync(Guid id, string txHash)
    {
        AssertHost();
        txHash = (txHash ?? string.Empty).Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(txHash, "^0x[0-9a-f]{64}$"))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Reason", "tx hash malformed");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstAsync()
            ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
        await uow.CompleteAsync();

        var receipt = await gateway.GetTransactionReceiptAsync(txHash);
        if (receipt.Status == null)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound).WithData("TxHash", txHash);
        }

        if (receipt.Status != 1)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("Reason", "receipt status failed");
        }

        await _broadcast.CompleteFromReceiptAsync(id, txHash);
    }

    /// <summary>
    /// 存量余额开账回填：对每个有余额的用户，把 ColBalance(可用+冻结) 与账本 2201 投影的差额
    /// 一次性过账入账本（BizRefId = opening:{userId}:{currency}，幂等可重跑）。
    /// 上线提现前执行一次；之后的新充值走归集确认同事务过账，不再产生差额。
    /// </summary>
    public virtual async Task<WithdrawOpeningBackfillResultDto> BackfillOpeningAsync()
    {
        AssertHost();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var balances = await dbContext.Balances.AsNoTracking()
            .Where(b => b.Balance + b.Frozen > 0)
            .Select(b => new { b.UserId, b.Currency, Total = b.Balance + b.Frozen })
            .ToListAsync();

        var posted = 0;
        var skipped = 0;
        foreach (var b in balances)
        {
            var currency = b.Currency.ToString();
            var ledgerBalance = await _ledger.GetUserAvailableBalanceAsync(b.UserId, currency);
            var diff = b.Total - ledgerBalance;
            if (diff <= 0)
            {
                skipped++; // 账本已不低于余额行（已回填过或已有过账），不动
                continue;
            }

            await _ledger.PostOpeningAsync(b.UserId, currency, diff);
            posted++;
        }

        return new WithdrawOpeningBackfillResultDto { Users = balances.Count, Posted = posted, Skipped = skipped };
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}

/// <summary>提现配置快照（管理端状态卡）。</summary>
public class WithdrawStatusDto
{
    public bool Enabled { get; set; }

    public bool Halt { get; set; }

    public string MinAmount { get; set; } = default!;

    public string FeeRate { get; set; } = default!;

    public string MaxDailyAmount { get; set; } = default!;

    public string MaxDailyCount { get; set; } = default!;

    public string AutoApproveMaxAmount { get; set; } = default!;

    public int PendingCount { get; set; }

    public int ProcessingCount { get; set; }

    public int BroadcastingCount { get; set; }

    public int BroadcastFailedCount { get; set; }
}

/// <summary>存量开账回填结果。</summary>
public class WithdrawOpeningBackfillResultDto
{
    public int Users { get; set; }

    public int Posted { get; set; }

    public int Skipped { get; set; }
}
