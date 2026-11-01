using System.Text.Json.Nodes;

namespace AbpAdmin.TokenGateway.Core;

/// <summary>
/// one-api 及其后真实上游的失败一律在这里就地变成我方错误码，原文（模型 id、渠道名、URL、错误文本）不出这一层。
/// one-api 的额度不足是平台在 one-api 的内部额度，不是用户余额，所以不映射成 INSUFFICIENT_BALANCE（用户余额只由 Wallet 预占判定）。
/// </summary>
public static class UpstreamErrors
{
    public static GatewayException FromStatus(int status) =>
        status == 429 ? GatewayException.RateLimited() : GatewayException.UpstreamUnavailable();

    /// <summary>200 体或 SSE 帧里带 error 对象时返回映射后的异常，否则 null。</summary>
    public static GatewayException? FromBody(JsonObject body) =>
        body["error"] is { } error ? FromErrorObject(error) : null;

    private static GatewayException FromErrorObject(JsonNode error)
    {
        var tag = error is JsonObject o
            ? $"{o["code"]} {o["type"]}".ToLowerInvariant()
            : "";
        return tag.Contains("rate_limit") || tag.Contains("429")
            ? GatewayException.RateLimited()
            : GatewayException.UpstreamUnavailable();
    }

    public static GatewayException FromException(Exception e) => e switch
    {
        GatewayException g => g,
        // OperationCanceledException 走到这里只剩流空闲超时（客户端断开在 catch 前已按
        // RequestAborted 静默返回）——与传输失败同语义，不是 500
        OperationCanceledException or System.Text.Json.JsonException or HttpRequestException or IOException => GatewayException.UpstreamUnavailable(),
        _ => GatewayException.Internal(),
    };
}
