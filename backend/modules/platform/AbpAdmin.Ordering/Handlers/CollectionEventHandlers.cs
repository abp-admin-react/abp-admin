using System.Threading.Tasks;
using AbpAdmin.Ordering.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Handlers;

/// <summary>
/// 渠道完成事件（计划第 3.2 节）：订单模块把事件当作快路径，
/// 「提交之后、处理之前进程退出」的窗口由补状态作业（OrderStatusSyncJobHandler）兜底。
/// </summary>
public class CollectionCompletedEventHandler
    : IDistributedEventHandler<AbpAdmin.Collection.Contracts.CollectionCompletedEto>, ITransientDependency
{
    private readonly OrderPaymentManager _orderPaymentManager;

    public CollectionCompletedEventHandler(OrderPaymentManager orderPaymentManager)
    {
        _orderPaymentManager = orderPaymentManager;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(AbpAdmin.Collection.Contracts.CollectionCompletedEto eto)
    {
        if (eto.BusinessType != OrderingConsts.OrderBusinessType)
        {
            return;
        }

        await _orderPaymentManager.HandleCollectionCompletedAsync(
            eto.BusinessId,
            eto.CollectionId,
            OrderingPayChannelExtensions.FromCollectionChannel(eto.Channel),
            AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(eto.Amount),
            eto.Currency,
            eto.ChannelTradeNo);
    }
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

/// <summary>扣款完成事件：订单应已是 Paid；仍是 PendingPayment 只记异常（计划第 3.2 节）。</summary>
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
