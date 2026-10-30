using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Withdrawals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Collection.Services;

/// <summary>创建提现申请（金额十进制字符串，边界口径与收款一致）。</summary>
public class CreateWithdrawInput
{
    public string Currency { get; set; } = default!;

    public string ToAddress { get; set; } = default!;

    /// <summary>提现总额（手续费从中扣），十进制字符串。</summary>
    public string Amount { get; set; } = default!;
}

/// <summary>提现单视图（金额一律十进制字符串）。用户/管理两个服务共用一份映射（避免两处 15 字段手抄漂移）。</summary>
public class WithdrawOrderDto
{
    public Guid Id { get; set; }

    /// <summary>提现用户 Id（管理端「用户」列展示）。</summary>
    public Guid UserId { get; set; }

    public string Currency { get; set; } = default!;

    public string NetworkName { get; set; } = default!;

    public string ToAddress { get; set; } = default!;

    public string Amount { get; set; } = default!;

    public string Fee { get; set; } = default!;

    public string ActualAmount { get; set; } = default!;

    public string Status { get; set; } = default!;

    public string? TxHash { get; set; }

    public int RetryCount { get; set; }

    public string? FailReason { get; set; }

    public string? AuditRemark { get; set; }

    public DateTime? AuditTime { get; set; }

    public DateTime? CompleteTime { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>实体→DTO 唯一映射（管理端 GetWithdrawalsAsync 同用）。</summary>
    public static WithdrawOrderDto From(AbpAdmin.Collection.Entities.ColWithdrawOrder o)
    {
        return new WithdrawOrderDto
        {
            Id = o.Id,
            UserId = o.UserId,
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
        };
    }
}

/// <summary>
/// 用户侧提现接口：提交/撤单/我的提现。Host 守卫与余额接口同纪律（表无租户列）。
/// 状态机/额度/冻结在 <see cref="WithdrawManager"/>；广播与卡单在 M3 worker。
/// </summary>
[Authorize]
public class CollectionWithdrawAppService : ApplicationService
{
    private readonly WithdrawManager _withdraws;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;

    public CollectionWithdrawAppService(
        WithdrawManager withdraws,
        IDbContextProvider<CollectionDbContext> dbContextProvider)
    {
        _withdraws = withdraws;
        _dbContextProvider = dbContextProvider;
    }

    /// <summary>提交提现：冻结净额+手续费，返回 Pending 单。</summary>
    public virtual async Task<WithdrawOrderDto> SubmitAsync(CreateWithdrawInput input)
    {
        AssertHost();
        var userId = CurrentUser.Id!.Value;

        if (!System.Enum.TryParse<CollectionCurrency>(input.Currency, true, out var currency))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Currency", input.Currency);
        }

        var amount = CollectionAmounts.Parse(input.Amount);
        var order = await _withdraws.SubmitAsync(userId, currency, input.ToAddress, amount);
        return WithdrawOrderDto.From(order);
    }

    /// <summary>撤单（仅 Pending 且本人；解冻并释放当日额度）。</summary>
    public virtual async Task CancelAsync(Guid id)
    {
        AssertHost();
        await _withdraws.CancelAsync(CurrentUser.Id!.Value, id);
    }

    /// <summary>我的提现单（倒序分页，可按状态过滤）。</summary>
    public virtual async Task<PagedResultDto<WithdrawOrderDto>> GetMyWithdrawalsAsync(
        int skipCount, int maxResultCount, string? status = null)
    {
        AssertHost();
        var userId = CurrentUser.Id!.Value;
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.WithdrawOrders.AsNoTracking().Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(status)
            && System.Enum.TryParse<WithdrawStatus>(status, true, out var st))
        {
            query = query.Where(o => o.Status == st);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();

        return new PagedResultDto<WithdrawOrderDto>(
            total, rows.Select(WithdrawOrderDto.From).ToList());
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}
