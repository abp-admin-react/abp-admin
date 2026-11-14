using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Channels;

/// <summary>
/// 渠道网关抽象，用于接入微信 Native、支付宝当面付和 Stripe Checkout。
/// 渠道 HTTP 一律在数据库事务之外调用（计划第 3.1 节）；M1 只有抽象与测试桩。
/// </summary>
public interface ICollectionChannelGateway
{
    CollectionChannel Channel { get; }

    /// <summary>创建收款时固定的渠道商户标识。</summary>
    string MerchantAccountId { get; }

    /// <summary>以 CollectionNo 为稳定商户单号幂等创建。同号重试必须复用渠道支付单。</summary>
    Task<ChannelCreateResult> CreateAsync(ChannelCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>以原商户单号查单。Paid 必须包含经渠道验证的支付结果。</summary>
    Task<ChannelQueryResult> QueryAsync(ChannelQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>按原商户单号幂等关单。已支付时返回渠道实付证据或由调用方补查。</summary>
    Task<ChannelCloseResult> CloseAsync(ChannelCloseRequest request, CancellationToken cancellationToken = default);
}

public record ChannelCreateRequest(
    Guid CollectionId,
    string CollectionNo,
    decimal Amount,
    CollectionCurrency Currency,
    DateTime ExpiresAt);

/// <param name="Succeeded">渠道创建成功，带回凭据（code_url / qr_code / Checkout 地址）。</param>
/// <param name="Unknown">超时或结果未知——保持 Pending 由补单作业按商户订单号查询，不算失败。</param>
/// <param name="FailureNote">渠道确定未创建可支付订单时的原因说明。</param>
public record ChannelCreateResult(
    bool Succeeded,
    bool Unknown = false,
    string? TradeNo = null,
    string? Payload = null,
    string? FailureNote = null);

public record ChannelCloseRequest(
    Guid CollectionId,
    string CollectionNo,
    string? TradeNo,
    decimal Amount,
    CollectionCurrency Currency);

public record ChannelQueryRequest(Guid CollectionId, string CollectionNo, string? TradeNo);

public enum ChannelQueryOutcome
{
    Unknown,
    Pending,
    Paid,
    /// <summary>渠道确证该商户单号已关闭、不可再支付。</summary>
    Closed,
    OrderNotFound,
}

public record ChannelQueryResult(ChannelQueryOutcome Outcome, CollectionPaymentObservation? Payment = null);

/// <summary>渠道已经验签或由可信服务端查单取得的实付事实。各字段必须源自渠道响应。</summary>
public record CollectionPaymentObservation(
    CollectionChannel Channel,
    string MerchantAccountId,
    string CollectionNo,
    string TradeNo,
    decimal Amount,
    CollectionCurrency Currency);

public enum ChannelCloseOutcome
{
    /// <summary>渠道确认已关单。</summary>
    Closed = 0,

    /// <summary>关单接口确证订单不存在且不可付款。暂时查不到应返回 Unknown。</summary>
    OrderNotFound = 1,

    /// <summary>渠道侧已支付。</summary>
    AlreadyPaid = 2,

    /// <summary>超时或网络失败，本地保留待核实状态。</summary>
    Unknown = 3,
}

public record ChannelCloseResult(ChannelCloseOutcome Outcome, CollectionPaymentObservation? Payment = null);
