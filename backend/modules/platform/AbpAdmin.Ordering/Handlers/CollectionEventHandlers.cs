using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Handlers;

/// <summary>
/// 渠道完成事件：投递可能重试，订单处理与补状态作业共用同一幂等入口。
/// </summary>
public class CollectionCompletedEventHandler
    : IDistributedEventHandler<CollectionCompletedEto>, ITransientDependency
{
    private readonly OrderPaymentManager _orderPaymentManager;

    public CollectionCompletedEventHandler(OrderPaymentManager orderPaymentManager)
    {
        _orderPaymentManager = orderPaymentManager;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(CollectionCompletedEto eto)
    {
        if (eto.BusinessType != OrderingConsts.OrderBusinessType)
        {
            return;
        }

        await _orderPaymentManager.HandleCollectionCompletedAsync(
            eto.BusinessId,
            eto.CollectionId,
            OrderingPayChannelExtensions.FromCollectionChannel(eto.Channel),
            CollectionAmounts.Parse(eto.Amount),
            eto.Currency,
            eto.ChannelTradeNo,
            eto.CompletedAt);
    }
}

/// <summary>渠道退款成功后，把实退金额和币种投影到原业务单的支付明细。</summary>
public class CollectionRefundSucceededEventHandler(OrderPaymentManager orderPaymentManager)
    : IDistributedEventHandler<CollectionRefundSucceededEto>, ITransientDependency
{
    [UnitOfWork]
    public virtual Task HandleEventAsync(CollectionRefundSucceededEto eto) =>
        eto.BusinessType == OrderingConsts.OrderBusinessType
            ? orderPaymentManager.HandleCollectionRefundSucceededAsync(eto)
            : Task.CompletedTask;
}

/// <summary>充值到账：插入已支付的充值订单（BalanceTxnId 唯一）。</summary>
public class BalanceCreditedEventHandler
    : IDistributedEventHandler<AbpAdmin.Collection.Contracts.BalanceCreditedEto>, ITransientDependency
{
    private readonly OrderPaymentManager _orderPaymentManager;

    public BalanceCreditedEventHandler(OrderPaymentManager orderPaymentManager)
    {
        _orderPaymentManager = orderPaymentManager;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(AbpAdmin.Collection.Contracts.BalanceCreditedEto eto)
    {
        await _orderPaymentManager.HandleBalanceCreditedAsync(
            eto.BalanceTxnId, eto.UserId, eto.Currency,
            AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(eto.Amount), eto.CompletedAt);
    }
}

/// <summary>订单应与扣款一起提交为 Paid；仍待支付时告警，交由定时核对处理。</summary>
public class BalanceDebitedEventHandler
    : IDistributedEventHandler<AbpAdmin.Collection.Contracts.BalanceDebitedEto>, ITransientDependency
{
    private readonly OrderPaymentManager _orderPaymentManager;

    public BalanceDebitedEventHandler(OrderPaymentManager orderPaymentManager)
    {
        _orderPaymentManager = orderPaymentManager;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(AbpAdmin.Collection.Contracts.BalanceDebitedEto eto)
    {
        if (eto.BusinessType != OrderingConsts.OrderBusinessType)
        {
            return;
        }

        await _orderPaymentManager.HandleBalanceDebitedAsync(
            eto.BusinessId, eto.BalanceTxnId,
            AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(eto.Amount), eto.Currency);
    }
}
