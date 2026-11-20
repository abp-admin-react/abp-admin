using Microsoft.AspNetCore.Mvc;
using AbpAdmin.Grids;
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
    public virtual Task<PagedResultDto<OrderDto>> GetListAsync(OrderListFilterDto filter) =>
        ReadListPageAsync(null, filter);

    [HttpPost("api/app/order-admin/query")]
    public virtual Task<PagedResultDto<OrderDto>> QueryListAsync([FromBody] GridQueryInput grid, [FromQuery] OrderListFilterDto filter) =>
        ReadListPageAsync(grid, filter);

    private async Task<PagedResultDto<OrderDto>> ReadListPageAsync(GridQueryInput? grid, OrderListFilterDto filter)
    {
        AssertHost();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = OrderServiceHelpers.ApplyFilter(dbContext.Orders.AsNoTracking(), filter);

        var definition = new GridQueryDefinition<OrdOrder>()
            .SearchableField("orderNo", x => x.OrderNo)
            .SearchableField("id", x => x.Id.ToString())
            .SearchableField("userId", x => x.UserId.ToString())
            .SearchableField("subject", x => x.Subject)
            .Field("amount", x => x.Amount)
            .Field("payAmount", x => x.PayAmount)
            .SearchableField("currency", x => x.Currency)
            .SearchableEnumField("status", x => x.Status)
            .SearchableEnumField("payChannel", x => x.PayChannel)
            .SearchableEnumField("orderType", x => x.OrderType)
            .Field("creationTime", x => x.CreationTime)
            .Field("paymentAnomaly", x => x.PaymentAnomaly);
        if (grid != null) query = definition.Filter(query, grid);
        var total = await query.CountAsync();
        var (skip, take) = grid == null ? OrderServiceHelpers.NormalizePaging(filter, def: 50, cap: 500) : (grid.SkipCount, grid.MaxResultCount);
        var items = await (grid == null
            ? query.OrderByDescending(o => o.CreationTime)
            : definition.OrderBy(query, grid, "creationTime", true, x => x.Id))
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
    /// 人工退款记录，不改订单状态，也不把钱退回钱包。
    /// 关联已有退款只追加说明；原资产金额进入待核实；标价币种金额才累加 RefundedAmount。
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
        var balancePaid = order.BalanceTxnId != null
            && order.PayChannel is OrderingPayChannel.BalanceUsdt or OrderingPayChannel.BalanceUsdc;
        var linkedRequestId = string.IsNullOrWhiteSpace(input.LinkedRequestId) ? null : input.LinkedRequestId.Trim();
        if (linkedRequestId != null
            && (linkedRequestId.Length > 64 || OrderServiceHelpers.HasControlChars(linkedRequestId)))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("LinkedRequestId", "LinkedRequestId is at most 64 characters, without control characters.");
        }

        if (linkedRequestId != null)
        {
            if (!balancePaid)
            {
                throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                    .WithData("LinkedRequestId", "Only a balance-paid order can link an existing wallet refund.");
            }

            if (currency.HasValue && currency != order.PayCurrency)
            {
                throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                    .WithData("Currency", "A linked note must use the paid asset, or omit the currency.");
            }
        }

        // 余额支付的补录按实付资产占用额度。其他订单的累计列仍是标价币种。
        if (linkedRequestId == null && currency.HasValue && balancePaid)
        {
            var matchesAsset = currency == order.PayCurrency;
            var matchesList = string.Equals(order.Currency, currency.Value.ToString(), StringComparison.OrdinalIgnoreCase);
            if (!matchesAsset && !matchesList)
            {
                throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                    .WithData("Currency", "Refund currency must match the paid asset or the order list currency.");
            }
        }
        else if (currency.HasValue && !string.Equals(order.Currency, currency.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Currency", $"Refund currency must match the order currency ({order.Currency}).");
        }
        var assetAmount = balancePaid && currency == order.PayCurrency;
        if (!assetAmount && amount.HasValue && decimal.Round(amount.Value, 2) != amount.Value)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Amount", "Refund amount supports at most 2 decimal places.");
        }

        // 关联和待核实不累加标价币种 RefundedAmount，见 AppendManualRefundAsync。
        await _orderPaymentManager.AppendManualRefundAsync(
            order.Id, amount, currency, input.Note.Trim(), linkedRequestId);
        return OrderAppService.Map(await LoadAsync(id), null);
    }

    protected virtual void AssertHost()
    {
        OrderServiceHelpers.ThrowIfNotHost(CurrentTenant.Id);
    }

    /// <summary>备注写入列的长度预算：512 上限减去固定前缀（防 varchar 溢出）。</summary>
    private const int MaxManualRefundNoteLength = 490; // OrderingConsts.MaxNoteLength(512) - len("Manual refund record: ")(22)

    private async Task<OrdOrder> LoadAsync(Guid id)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id)
               ?? throw new BusinessException(OrderingErrorCodes.OrderNotFound).WithData("OrderId", id);
    }
}
