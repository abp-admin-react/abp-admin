using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Payments;
using EasyAbp.PaymentService.Payments;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款模块的退款守卫（计划第 2、5.2 节）：按本模块的 ItemType 前缀拒绝已存在的支付行。
/// 用前缀而不是逐常量相等：CollectionPaymentItemTypes 加新渠道常量时守卫自动覆盖
/// （评审 M 级：逐常量相等会在 M3/M4 加渠道时静默漏拦）。渠道一旦退款，收款单和订单
/// 仍显示已支付——本期不做渠道退款 API，退款只做人工记录（落在订单的 PaymentAnomaly 上）。
/// 语义是「这笔已存在的支付不能退」，不在创建支付行之前拒绝。
/// </summary>
public class CollectionRefundGuard : IPaymentRefundGuard, ITransientDependency
{
    private readonly IPaymentRepository _paymentRepository;

    public CollectionRefundGuard(IPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public virtual async Task<PaymentRefundGuardResult> CheckRefundAsync(Guid paymentId)
    {
        var queryable = await _paymentRepository.WithDetailsAsync();
        var hasCollectionItem = await queryable
            .Where(p => p.Id == paymentId)
            .SelectMany(p => p.PaymentItems)
            .AnyAsync(i => i.ItemType != null
                && i.ItemType.StartsWith(Contracts.CollectionPaymentItemTypes.Prefix, StringComparison.Ordinal));

        if (hasCollectionItem)
        {
            return PaymentRefundGuardResult.Refuse(
                "This payment backs a collection order; channel refunds are manual-record only in this phase.");
        }

        return PaymentRefundGuardResult.Allow();
    }
}
