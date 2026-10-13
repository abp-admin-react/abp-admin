namespace AbpAdmin.Account;

/// <summary>
/// 提供当前请求的访问令牌（Bearer 原文），供应用层向 /connect/token 做令牌交换时转发。
/// Application 层不引用 ASP.NET Core，无法直接读 HttpContext；
/// 真正的实现（HttpContextCurrentAccessTokenProvider）在 HttpApi.Host 注册，
/// 测试环境未注册时 AppService 属性注入保留 <see cref="NullCurrentAccessTokenProvider"/> 默认值。
/// </summary>
public interface ICurrentAccessTokenProvider
{
    string? GetAccessToken();
}

/// <summary>
/// 空实现：非 Web 环境（集成测试等）下拿不到令牌，返回 null。
/// </summary>
public class NullCurrentAccessTokenProvider : ICurrentAccessTokenProvider
{
    public string? GetAccessToken() => null;
}
