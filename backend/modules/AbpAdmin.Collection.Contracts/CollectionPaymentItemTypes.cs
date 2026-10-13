namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// EasyAbp Payment 行 ItemType 的收款模块前缀常量（计划第 2、5.2 节）。
/// 收款模块创建 Payment/PaymentItem 时 ItemType 用它，ItemKey 为收款号；
/// 退款守卫实现按它拒绝已存在的支付行（不引用订单，也不被 AbpAdmin.Application 知晓）。
/// </summary>
public static class CollectionPaymentItemTypes
{
    /// <summary>
    /// 本模块全部 ItemType 的公共前缀——退款守卫按它拒绝（前缀语义，计划第 2 节：
    /// 「按本模块的 ItemType 前缀拒绝已存在的支付行」）。加新渠道常量必须落在该前缀之下，
    /// 守卫与反射测试（守卫测试）据此保证新渠道自动被拒。
    /// </summary>
    public const string Prefix = "AbpAdmin.Collection.";

    /// <summary>微信 Native 收款创建的 EasyAbp 支付行 ItemType。加新渠道时在下面追加（保持 Prefix 前缀）。</summary>
    public const string WeChatPay = Prefix + "WeChatPay";
}
