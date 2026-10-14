namespace AbpAdmin.TokenGateway.Core;

public static class GatewayErrorCodes
{
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string InvalidKey = "INVALID_KEY";
    public const string RateLimited = "RATE_LIMITED";
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string Internal = "INTERNAL";
    // 非对话错误码（钱包/登录接口用）
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string NotFound = "NOT_FOUND";
    public const string BadRequest = "BAD_REQUEST";
    public const string Forbidden = "FORBIDDEN";
}

public sealed class GatewayException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;

    public static GatewayException InsufficientBalance() => new(GatewayErrorCodes.InsufficientBalance, 402, "当前额度已用完，充值后才能继续对话。");
    public static GatewayException InvalidKey() => new(GatewayErrorCodes.InvalidKey, 401, "登录已失效，请重新登录");
    public static GatewayException RateLimited() => new(GatewayErrorCodes.RateLimited, 429, "请求太频繁，请稍后再试");
    public static GatewayException UpstreamUnavailable() => new(GatewayErrorCodes.UpstreamUnavailable, 503, "服务暂时不可用，请稍后再试");
    public static GatewayException Internal() => new(GatewayErrorCodes.Internal, 500, "出了点问题，请稍后再试");
    public static GatewayException NotFound() => new(GatewayErrorCodes.NotFound, 404, "不存在");
    public static GatewayException BadRequest(string m) => new(GatewayErrorCodes.BadRequest, 400, m);
}
