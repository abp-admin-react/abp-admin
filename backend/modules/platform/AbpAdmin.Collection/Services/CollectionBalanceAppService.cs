using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>我的余额（USDT / USDC）。</summary>
public class MyBalanceDto
{
    public string Currency { get; set; } = default!;

    /// <summary>可用余额，十进制字符串（边界一律字符串，计划第 2 节）。</summary>
    public string Balance { get; set; } = default!;

    /// <summary>提现冻结，十进制字符串（提现 M1 两桶口径）。</summary>
    public string Frozen { get; set; } = default!;

    /// <summary>合计 = 可用 + 冻结，十进制字符串。</summary>
    public string Total { get; set; } = default!;
}

/// <summary>我的余额流水（分页、倒序）。</summary>
public class BalanceTxnDto
{
    public Guid Id { get; set; }

    public string Action { get; set; } = default!;

    public string Currency { get; set; } = default!;

    /// <summary>带符号十进制字符串：充值/回冲为正，扣款为负。</summary>
    public string Amount { get; set; } = default!;

    public string BalanceAfter { get; set; } = default!;

    public DateTime CreationTime { get; set; }

    /// <summary>业务类型（TopUp / Order …）；无归因为 null。</summary>
    public string? BusinessType { get; set; }

    /// <summary>业务单 Id（充值行 = 流水自身 Id；扣款行 = 订单 Id）。对账查询原值用；
    /// 前端的「关联订单」展示走流水 Id 反查端点（order-refs-by-balance-txn），不经此字段。</summary>
    public Guid? BusinessId { get; set; }

    /// <summary>回冲行的原扣款流水 Id（用户侧把回冲关联回原订单用）。</summary>
    public Guid? RefTxnId { get; set; }

    /// <summary>充值行的链上转账凭证（ColDepositOrders.TxHash 回填；非充值行为 null）。</summary>
    public string? TxHash { get; set; }
}

/// <summary>
/// 用户侧余额接口（计划第 3.1 节「用户和管理端看余额、流水，走收款模块自己的应用服务，
/// 不放进集成服务」）。只查 CurrentUser.Id；这两套表没有租户列，租户身份直接拒绝（Host 守卫）。
/// 订单页面展示两种余额就调它（不引用收款实现项目）。
/// </summary>
[Authorize]
public class CollectionBalanceAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;

    public CollectionBalanceAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    /// <summary>我的两种稳定币余额（没有余额行时返回 0，不缺项）。查询内核与 member-\* 单源共享。</summary>
    public virtual async Task<ListResultDto<MyBalanceDto>> GetMyBalancesAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var items = await BalanceQueries.GetBalancesAsync(dbContext, CurrentUser.Id!.Value);
        await uow.CompleteAsync();
        return new ListResultDto<MyBalanceDto>(items);
    }

    /// <summary>
    /// 我的余额流水（只看自己的，倒序分页，可选时间范围）。
    /// 充值行批量回填链上凭证：贷记的幂等键 = 进款单 Id（CreditCoreAsync 的写入口径），
    /// 据此查 ColDepositOrders.TxHash——用户对账要能看到链上转账，不只是余额数字。
    /// </summary>
    public virtual async Task<PagedResultDto<BalanceTxnDto>> GetMyTxnsAsync(
        int skipCount, int maxResultCount, DateTime? fromTime = null, DateTime? toTime = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (total, items) = await BalanceQueries.GetTxnsAsync(
            dbContext, CurrentUser.Id!.Value, skipCount, maxResultCount, fromTime, toTime);
        await uow.CompleteAsync();
        return new PagedResultDto<BalanceTxnDto>(total, items);
    }

    private void AssertHost()
    {
        // 表没有租户列：租户身份调用直接拒绝（计划第 2 节）
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(Contracts.CollectionErrorCodes.HostOnly);
        }
    }
}
