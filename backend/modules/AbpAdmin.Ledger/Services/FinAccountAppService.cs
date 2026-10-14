using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 科目管理：GET /api/app/fin-account（Ledger.View 或 Account.Manage），
/// 写操作 POST/PUT/DELETE + POST {id}/disable|enable（Account.Manage）。
/// 系统科目只读（AccountSystemReadOnly）；
/// 有分录或仍被调账单借贷引用 → AccountHasEntries（不可删；有分录时不可改 code/type/direction，只能停用）；
/// 仍有子科目 → AccountHasChildren；编码冲突 → AccountCodeDuplicate。
/// </summary>
public class FinAccountAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;

    public FinAccountAppService(IDbContextProvider<LedgerDbContext> db) => _db = db;

    /// <summary>
    /// 分页列表。需 Ledger.View 或 Account.Manage；无权 → 403（无业务码）。
    /// 默认只返回启用科目（IsActive 未传时等价 IsActive=true）；科目树/下拉依赖此默认，避免停用科目泄漏到前端。
    /// </summary>
    public virtual async Task<PagedResultDto<FinAccountDto>> GetListAsync(FinAccountListInput input)
    {
        await RequireAnyAsync(null, FinancePermissions.Ledger.View, FinancePermissions.Account.Manage);
        var db = await _db.GetDbContextAsync();
        var q = db.Accounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(input.Code)) { var c = input.Code.Trim(); q = q.Where(a => a.Code.Contains(c)); }
        if (!string.IsNullOrWhiteSpace(input.Name)) { var n = input.Name.Trim(); q = q.Where(a => a.Name.Contains(n)); }
        if (input.Type.HasValue) { q = q.Where(a => a.Type == input.Type.Value); }
        // 默认隐藏停用：null → IsActive=true；显式 true/false 按调用方过滤。
        if (input.IsActive.HasValue) { q = q.Where(a => a.IsActive == input.IsActive.Value); }
        else { q = q.Where(a => a.IsActive); }

        var total = await q.CountAsync();
        var skip = Math.Max(0, input.SkipCount);
        var take = input.MaxResultCount <= 0 ? DefaultPageSize : Math.Min(MaxPageSize, input.MaxResultCount);
        var rows = await q.OrderBy(a => a.Code).Skip(skip).Take(take)
            .Select(a => new
            {
                A = a,
                Has = db.Entries.Any(e => db.SubAccounts.Any(s => s.Id == e.SubAccountId && s.AccountId == a.Id)),
            })
            .ToListAsync();
        return new PagedResultDto<FinAccountDto>(total, rows.Select(r => Map(r.A, r.Has)).ToList());
    }

    /// <summary>单科。不存在 → AccountNotFound + data.Id。权限同 GetList。</summary>
    public virtual async Task<FinAccountDto> GetAsync(Guid id)
    {
        await RequireAnyAsync(null, FinancePermissions.Ledger.View, FinancePermissions.Account.Manage);
        var a = await FindAsync(id);
        return Map(a, await HasEntriesAsync(id));
    }

    [Authorize(FinancePermissions.Account.Manage)]
    /// <summary>
    /// 新建。需 Account.Manage。校验：code/name 必填 → InvalidEvent Field=code|name；
    /// 重码 → AccountCodeDuplicate；上级不存在或自指 → AccountNotFound + data.ParentId。
    /// </summary>
    public virtual async Task<FinAccountDto> CreateAsync(CreateUpdateFinAccountDto input)
    {
        var code = Validate(input);
        var db = await _db.GetDbContextAsync();
        await EnsureCodeUniqueAsync(db, code, null);
        await EnsureParentAsync(db, input.ParentId, null);
        var a = new FinAccount(GuidGenerator.Create(), code, input.Name.Trim(), input.Type, input.Direction,
            isSystem: false, parentId: input.ParentId, tenantId: CurrentTenant.Id);
        await db.Accounts.AddAsync(a);
        await db.SaveChangesAsync();
        return Map(a, false);
    }

    [Authorize(FinancePermissions.Account.Manage)]
    /// <summary>
    /// 更新。系统科目 → AccountSystemReadOnly；有分录时改 code/type/direction → AccountHasEntries；
    /// 仍可改 name/parent/停用。错误码与 Create 对齐。
    /// </summary>
    public virtual async Task<FinAccountDto> UpdateAsync(Guid id, CreateUpdateFinAccountDto input)
    {
        var code = Validate(input);
        var db = await _db.GetDbContextAsync();
        var a = await FindAsync(id, tracking: true);
        EnsureNotSystem(a);
        var has = await HasEntriesAsync(id);
        if (has && (a.Code != code || a.Type != input.Type || a.NormalSide != input.Direction))
        {
            throw new BusinessException(FinanceErrorCodes.AccountHasEntries).WithData("Code", a.Code);
        }

        if (a.Code != code) { await EnsureCodeUniqueAsync(db, code, id); }
        await EnsureParentAsync(db, input.ParentId, id);
        a.Code = code;
        a.Name = input.Name.Trim();
        a.Type = input.Type;
        a.NormalSide = input.Direction;
        a.ParentId = input.ParentId;
        await db.SaveChangesAsync();
        return Map(a, has);
    }

    [Authorize(FinancePermissions.Account.Manage)]
    /// <summary>
    /// 物理删除。有分录或仍被调账单借贷引用 → AccountHasEntries；有子科目 → AccountHasChildren；
    /// 系统科目 → AccountSystemReadOnly。已过账历史勿指望靠删科目抹掉。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        var db = await _db.GetDbContextAsync();
        var a = await FindAsync(id, tracking: true);
        EnsureNotSystem(a);
        if (await HasEntriesAsync(id) || await db.ManualAdjusts.AnyAsync(m => m.DebitAccountId == id || m.CreditAccountId == id))
        {
            throw new BusinessException(FinanceErrorCodes.AccountHasEntries).WithData("Code", a.Code);
        }

        if (await db.Accounts.AnyAsync(x => x.ParentId == id))
        {
            throw new BusinessException(FinanceErrorCodes.AccountHasChildren).WithData("Code", a.Code);
        }

        // 无分录的空子户（及其零余额投影）一并清理
        var subIds = await db.SubAccounts.Where(s => s.AccountId == id).Select(s => s.Id).ToListAsync();
        if (subIds.Count > 0)
        {
            db.BalanceProjections.RemoveRange(await db.BalanceProjections.Where(p => subIds.Contains(p.SubAccountId)).ToListAsync());
            db.SubAccounts.RemoveRange(await db.SubAccounts.Where(s => s.AccountId == id).ToListAsync());
        }

        db.Accounts.Remove(a);
        await db.SaveChangesAsync();
    }

    [Authorize(FinancePermissions.Account.Manage)]
    public virtual Task<FinAccountDto> DisableAsync(Guid id) => SetActiveAsync(id, false);

    [Authorize(FinancePermissions.Account.Manage)]
    public virtual Task<FinAccountDto> EnableAsync(Guid id) => SetActiveAsync(id, true);

    protected virtual async Task<FinAccountDto> SetActiveAsync(Guid id, bool active)
    {
        var db = await _db.GetDbContextAsync();
        var a = await FindAsync(id, tracking: true);
        EnsureNotSystem(a);
        a.IsActive = active;
        await db.SaveChangesAsync();
        return Map(a, await HasEntriesAsync(id));
    }

    private static string Validate(CreateUpdateFinAccountDto input)
    {
        var code = input.Code?.Trim() ?? "";
        if (code.Length == 0 || code.Length > LedgerConsts.MaxCodeLength)
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "code");
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > LedgerConsts.MaxNameLength)
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "name");
        }

        if (!Enum.IsDefined(input.Type) || !Enum.IsDefined(input.Direction))
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "type/direction");
        }

        return code;
    }

    private static void EnsureNotSystem(FinAccount a)
    {
        if (a.IsSystem)
        {
            throw new BusinessException(FinanceErrorCodes.AccountSystemReadOnly).WithData("Code", a.Code);
        }
    }

    private static async Task EnsureCodeUniqueAsync(LedgerDbContext db, string code, Guid? selfId)
    {
        if (await db.Accounts.AnyAsync(a => a.Code == code && a.Id != selfId))
        {
            throw new BusinessException(FinanceErrorCodes.AccountCodeDuplicate).WithData("Code", code);
        }
    }

    private static async Task EnsureParentAsync(LedgerDbContext db, Guid? parentId, Guid? selfId)
    {
        if (parentId == null) { return; }
        if (parentId == selfId || !await db.Accounts.AnyAsync(a => a.Id == parentId))
        {
            throw new BusinessException(FinanceErrorCodes.AccountNotFound).WithData("ParentId", parentId);
        }

        // 防环：沿父链上溯不得遇到自己
        var cur = parentId;
        for (var i = 0; cur != null && i < 64; i++)
        {
            if (cur == selfId) { throw new BusinessException(FinanceErrorCodes.AccountNotFound).WithData("ParentId", parentId); }
            cur = await db.Accounts.Where(a => a.Id == cur).Select(a => a.ParentId).FirstOrDefaultAsync();
        }
    }

    private async Task<FinAccount> FindAsync(Guid id, bool tracking = false)
    {
        var db = await _db.GetDbContextAsync();
        var q = tracking ? db.Accounts : db.Accounts.AsNoTracking();
        return await q.FirstOrDefaultAsync(a => a.Id == id)
               ?? throw new BusinessException(FinanceErrorCodes.AccountNotFound).WithData("Id", id);
    }

    private async Task<bool> HasEntriesAsync(Guid id)
    {
        var db = await _db.GetDbContextAsync();
        return await db.Entries.AnyAsync(e => db.SubAccounts.Any(s => s.Id == e.SubAccountId && s.AccountId == id));
    }

    private static FinAccountDto Map(FinAccount a, bool has) => new()
    {
        Id = a.Id, Code = a.Code, Name = a.Name, Type = a.Type, Direction = a.NormalSide,
        ParentId = a.ParentId, IsSystem = a.IsSystem, IsActive = a.IsActive, HasEntries = has,
    };
}
