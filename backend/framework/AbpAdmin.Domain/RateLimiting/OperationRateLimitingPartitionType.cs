namespace AbpAdmin.RateLimiting;

/// <summary>
/// 分区类型。
/// </summary>
public enum OperationRateLimitingPartitionType
{
    /// <summary>直接用 context.Parameter 的值，原样不做任何规范化。</summary>
    Parameter,

    /// <summary>CurrentUser.Id，未登录时为 "anonymous"。</summary>
    CurrentUser,

    /// <summary>CurrentTenant.Id，host 用字符串 "host"。</summary>
    CurrentTenant,

    /// <summary>客户端 IP（IWebClientInfoProvider.ClientIpAddress）。</summary>
    ClientIp,

    /// <summary>优先 context.Parameter，为空回退 CurrentUser.Email；规范化 ToUpperInvariant()。</summary>
    Email,

    /// <summary>优先 context.Parameter，为空回退 CurrentUser.PhoneNumber；剥离空格/短横线/点/圆括号，保留 + 与数字。</summary>
    PhoneNumber,

    /// <summary>具名自定义解析器，通过 AddPartitionKeyResolver 注册。</summary>
    Custom
}
