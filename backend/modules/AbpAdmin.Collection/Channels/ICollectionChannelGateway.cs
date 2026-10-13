using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Channels;

/// <summary>
/// 渠道网关抽象：微信（EasyAbp 包装，M3）、支付宝当面付（M3）、Stripe Checkout（M4）落实现。
/// 渠道 HTTP 一律在数据库事务之外调用（计划第 3.1 节）；M1 只有抽象与测试桩。
/// </summary>
public interface ICollectionChannelGateway
{
    CollectionChannel Channel { get; }

    /// <summary>在渠道侧创建支付凭据（微信 Native 单、支付宝预下单、Stripe Checkout Session）。</summary>
    Task<ChannelCreateResult> CreateAsync(ChannelCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>请求渠道关单。已支付单渠道会报错，返回 <see cref="ChannelCloseOutcome.AlreadyPaid"/> 走升级路径。</summary>
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
/// <param name="FailureNote">明确失败时的原因说明（写入 StatusNote，给排障与补单看）。</param>
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

public enum ChannelCloseOutcome
{
    /// <summary>渠道确认已关单。</summary>
    Closed = 0,

    /// <summary>渠道返回订单不存在——视为关单成功（本地已 Closed，只告警）。</summary>
    OrderNotFound = 1,

    /// <summary>渠道侧已支付——按渠道单号与金额核对后转入升级路径（标 Completed + 发事件）。</summary>
    AlreadyPaid = 2,

    /// <summary>超时或网络失败——本地保持 Closed，告警；后续收到成功通知走升级路径。</summary>
    Unknown = 3,
}

public record ChannelCloseResult(
    ChannelCloseOutcome Outcome,
    string? TradeNo = null,
    /// <summary>Outcome 为 AlreadyPaid 时渠道侧实付金额（升级路径按它核对本地金额）。</summary>
    decimal? PaidAmount = null);
