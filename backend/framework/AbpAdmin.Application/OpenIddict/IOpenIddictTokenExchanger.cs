using System.Threading.Tasks;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// Client Credentials 代取 access token 的出站 HTTP 交换器。
/// 把「HTTP POST /connect/token + OAuth 错误响应翻译 + JSON 解析」从应用服务里分离出来：
/// 应用服务保留业务校验（Confidential / CC flow / scope 分配），
/// 本接口只负责与认证服务器的报文交换，也便于对 HTTP 失败分支单独做单测。
/// </summary>
public interface IOpenIddictTokenExchanger
{
    /// <summary>
    /// 用 client_credentials 授权向 AuthServer:Authority 的 /connect/token 端点换取 access token。
    /// scopes 为已去重去空白、且已通过业务校验的请求集合。
    /// </summary>
    Task<GenerateAccessTokenResultDto> ExchangeAsync(string clientId, string clientSecret, string[] scopes);
}
