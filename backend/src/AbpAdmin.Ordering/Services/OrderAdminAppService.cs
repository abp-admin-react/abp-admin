using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.Ordering.Permissions;
using AbpAdmin.Ordering.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ordering.Services;

/// <summary>
/// 管理端订单应用服务（/api/app/order-admin）：Host 管理员能看到全部订单（表没有租户列）。
/// 标履约只从业务单的 Paid 标出，标了之后不能回冲；人工退款不改 Status，记在异常明细上
///（计划第 1、4.2 节）。列表过滤/明细映射/Host 守卫与用户端共用 OrderServiceHelpers（单一出处）。
/// </summary>
[Authorize(OrderingPermissions.Orders.ViewAll)]
public class OrderAdminAppService : ApplicationService
{
    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly OrderPaymentManager _orderPaymentManager;

    public OrderAdminAppService(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        OrderPaymentManager orderPaymentManager)
    {
        _dbContextProvider = dbContextProvider;
        _orderPaymentManager = orderPaymentManager;
    }

    public virtual async Task<OrderDto> GetAsync(Guid id)
    {
        AssertHost();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
        {
            throw new BusinessException(OrderingErrorCodes.OrderNotFound).WithData("OrderId", id);
        }

        var linesByOrder = await OrderServiceHelpers.LoadAnomalyLinesBatchedAsync(dbContext, new[] { id });
        return OrderAppService.Map(order, linesByOrder.GetValueOrDefault(id));
    }

    /// <summary>全部订单；列表可按订单类型、状态和「有异常」过滤（更早的关闭单用这里的过滤查看）。</summary>
    public virtual async Task<PagedResultDto<OrderDto>> GetListAsync(OrderListFilterDto filter)
    {
        AssertHost();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = OrderServiceHelpers.ApplyFilter(dbContext.Orders.AsNoTracking(), filter);

        var total = await query.CountAsync();
        var (skip, take) = OrderServiceHelpers.NormalizePaging(filter, def: 50, cap: 500);
        var items = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return new PagedResultDto<OrderDto>(total, items.Select(o => OrderAppService.Map(o, null)).ToList());
    }

    /// <summary>把业务单从 Paid 标成 Fulfilled（管理端动作；不可回冲，退款只做人工记录）。</summary>
    [Authorize(OrderingPermissions.Orders.Fulfill)]
    public virtual async Task<OrderDto> MarkFulfilledAsync(Guid id)
    {
        AssertHost();

        var order = await LoadAsync(id);
        await _orderPaymentManager.MarkFulfilledAsync(order);
        return OrderAppService.Map(await LoadAsync(id), null);
    }

    /// <summary>
    /// 人工退款记录：追加异常明细，不改订单 Status（计划第 4.2 节——不另做渠道退款 API）。
    /// </summary>
    [Authorize(OrderingPermissions.Orders.RecordManualRefund)]
    public virtual async Task<OrderDto> RecordManualRefundAsync(Guid id, RecordManualRefundInput input)
    {
        AssertHost();

        if (string.IsNullOrWhiteSpace(input.Note) || input.Note.Length > MaxManualRefundNoteLength
            || OrderServiceHelpers.HasControlChars(input.Note))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Note", $"Note is required, at most {MaxManualRefundNoteLength} chars, control characters not allowed.");
        }

        decimal? amount = null;
        AbpAdmin.Collection.Contracts.CollectionCurrency? currency = null;
        if (!string.IsNullOrWhiteSpace(input.Amount))
        {
            amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(input.Amount);
            if (amount <= 0)
            {
                throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                    .WithData("Amount", "Refund amount must be positive.");
            }

            // IsDefined 挡住未定义数值（TryParse 对 "99" 这类数字串返回 true）
            if (string.IsNullOrEmpty(input.Currency)
                || !Enum.TryParse<AbpAdmin.Collection.Contracts.CollectionCurrency>(input.Currency, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(AbpAdmin.Collection.Contracts.CollectionCurrency), parsed))
            {
                throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                    .WithData("Currency", "Currency is required with Amount.");
            }

            currency = parsed;
        }

        var order = await LoadAsync(id);

        // 币种必须等于订单标价币种（RefundedAmount 是单币种累计列；混币种累加 = 对用户的错误金额）
        if (currency.HasValue && !string.Equals(order.Currency, currency.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Currency", $"Refund currency must match the order currency ({order.Currency}).");
        }
        // 两位小数：RefundedAmount 列是 numeric(18,2)，子分位会让 PG 静默舍入、累计 ≠ Σ 明细
        if (amount.HasValue && decimal.Round(amount.Value, 2) != amount.Value)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Amount", "Refund amount supports at most 2 decimal places.");
        }

        // 退款明细 + RefundedAmount 累加（用户侧订单详情可见，见 OrderPaymentManager.AppendManualRefundAsync）
        await _orderPaymentManager.AppendManualRefundAsync(order.Id, amount, currency, input.Note.Trim());
        return OrderAppService.Map(await LoadAsync(id), null);
    }

    protected virtual void AssertHost()
    {
        OrderServiceHelpers.ThrowIfNotHost(CurrentTenant.Id);
    }

    /// <summary>备注写入列的长度预算：512 上限减去固定前缀（防 varchar 溢出变 500）。</summary>
    private const int MaxManualRefundNoteLength = 489; // OrderingConsts.MaxNoteLength(512) - len("Manual refund record: ")

    private async Task<OrdOrder> LoadAsync(Guid id)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id)
               ?? throw new BusinessException(OrderingErrorCodes.OrderNotFound).WithData("OrderId", id);
    }
}
