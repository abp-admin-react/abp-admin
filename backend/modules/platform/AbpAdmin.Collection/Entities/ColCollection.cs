using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 收款单（ColCollections）。流水性质，不实现 ISoftDelete，不用带删除位的审计基类（计划第 2 节）。
/// 无租户列——这两套表只给 Host 用户，应用服务入口做 Host 守卫。
/// </summary>
public class ColCollection : Volo.Abp.Domain.Entities.BasicAggregateRoot<Guid>
{
    public ColCollection(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColCollection()
    {
    }
#pragma warning restore CS8618

    public string CollectionNo { get; set; } = default!;

    public Guid UserId { get; set; }

    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public CollectionChannel Channel { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>精度与币种匹配（numeric(28,18)；法币在写入前由订单模块舍入到 0.01）。</summary>
    public decimal Amount { get; set; }

    public CollectionStatus Status { get; set; }

    /// <summary>渠道明确失败/结果未知时的说明。</summary>
    public string? StatusNote { get; set; }

    /// <summary>渠道结果未知（超时/网络失败，保持 Pending 由补单作业按商户订单号查询）。</summary>
    public bool ChannelResultUnknown { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? ChannelTradeNo { get; set; }

    /// <summary>渠道侧凭据：微信 code_url、支付宝 qr_code、Stripe Checkout 跳转地址。</summary>
    public string? ChannelPayload { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreationTime { get; set; }
}
