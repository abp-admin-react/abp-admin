using System;
using System.Threading.Tasks;

namespace AbpAdmin.Payments;

/// <summary>
/// 渠道退款守卫（收款/订单计划第 2 节）。<c>PaymentAdminAppService</c> 在发起退款前询问全部实现：
/// 没有任何实现时放行；任一实现拒绝即拒绝，并记下是哪个实现。
/// 框架侧（AbpAdmin.Application）不出现模块名——收款模块注册自己的实现，
/// 按本模块的 ItemType 前缀拒绝已存在的支付行，不在创建支付行之前拒绝。
/// </summary>
public interface IPaymentRefundGuard
{
    /// <summary>按支付单 id 检查这笔已存在的支付是否允许渠道退款。</summary>
    Task<PaymentRefundGuardResult> CheckRefundAsync(Guid paymentId);
}

public sealed class PaymentRefundGuardResult
{
    public bool Refused { get; private init; }

    /// <summary>拒绝理由（写入异常 WithData 与操作日志，便于客服分辨）。</summary>
    public string? Reason { get; private init; }

    public static PaymentRefundGuardResult Allow()
    {
        return new PaymentRefundGuardResult { Refused = false };
    }

    public static PaymentRefundGuardResult Refuse(string reason)
    {
        return new PaymentRefundGuardResult { Refused = true, Reason = reason };
    }
}
