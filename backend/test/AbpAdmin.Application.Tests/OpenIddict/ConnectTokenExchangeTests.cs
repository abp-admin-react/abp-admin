using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin.Account;
using AbpAdmin.HttpStubs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.OpenIddict;

/* /connect/token 声明式客户端（HttpAgent）迁移的回归锚。
 * 覆盖：client_credentials 成功映射与请求形状（URL 拼接、表单字段、无 Authorization 头）、
 * 标准 OAuth 错误体 → BusinessException reason、非 JSON 错误体 → 回落 HTTP 状态码（原 TryReadTokenError 语义）、
 * 2xx 畸形响应（缺 access_token / 非 JSON 体）→ 显式业务错误（round4 审查补）、
 * impersonation 的 Bearer 头与 extraParameters 合并、token_type 缺省回落 Bearer、
 * 网络层异常不抑制原样穿透（HttpAgent 默认无 SuppressExceptions 的契约锚）。
 * 出站请求由 RecordingConnectTokenHandler 截停（不真实外呼）。
 */
public abstract class ConnectTokenExchangeTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly RecordingConnectTokenHandler _handler;
    private readonly OpenIddictTokenExchanger _openIddictExchanger;
    private readonly ImpersonationTokenExchanger _impersonationExchanger;
    private readonly PasswordlessTokenExchanger _passwordlessExchanger;
    private readonly LinkedAccountTokenExchanger _linkedExchanger;

    protected ConnectTokenExchangeTests()
    {
        _handler = GetRequiredService<RecordingConnectTokenHandler>();
        _openIddictExchanger = GetRequiredService<OpenIddictTokenExchanger>();
        _impersonationExchanger = GetRequiredService<ImpersonationTokenExchanger>();
        _passwordlessExchanger = GetRequiredService<PasswordlessTokenExchanger>();
        _linkedExchanger = GetRequiredService<LinkedAccountTokenExchanger>();
        _handler.Reset();
    }

    [Fact]
    public async Task Client_credentials_success_posts_form_and_maps_token()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"access_token":"at-1","token_type":"Bearer","expires_in":3600,"scope":"a b"}""");

        var result = await _openIddictExchanger.ExchangeAsync("cid", "csecret", new[] { "a", "b" });

        result.AccessToken.ShouldBe("at-1");
        result.TokenType.ShouldBe("Bearer");
        result.ExpiresInSeconds.ShouldBe(3600);
        result.GrantedScopes.ShouldBe(new[] { "a", "b" });

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        // URL = 命名 HttpClient BaseAddress（AuthServer:Authority，.invalid 保留域）+ 相对路径 connect/token 拼接
        request.RequestUri.ToString().ShouldBe("https://authserver-test.invalid/connect/token");
        request.Form["grant_type"].ShouldBe("client_credentials");
        request.Form["client_id"].ShouldBe("cid");
        request.Form["client_secret"].ShouldBe("csecret");
        request.Form["scope"].ShouldBe("a b");
        request.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Oauth_error_body_maps_to_business_exception_reason()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"Invalid client secret"}""");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _openIddictExchanger.ExchangeAsync("cid", "wrong", new[] { "a" }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenFailed);
        exception.Data["reason"]?.ToString().ShouldBe("invalid_client: Invalid client secret");
    }

    [Fact]
    public async Task Non_json_error_body_falls_back_to_http_status()
    {
        // 网关错误页等非 JSON 响应：拿不到 OAuth error 字段，回落 "HTTP {status}"（原 TryReadTokenError 行为）
        _handler.Enqueue(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _openIddictExchanger.ExchangeAsync("cid", "csecret", new[] { "a" }));

        exception.Data["reason"]?.ToString().ShouldBe("HTTP 502");
    }

    [Fact]
    public async Task Success_without_access_token_throws_explicit_business_error()
    {
        // 2xx 但 JSON 缺 access_token（原版此处抛 KeyNotFoundException，现为显式业务错误——round4 审查锚）
        _handler.Enqueue(HttpStatusCode.OK, """{"token_type":"Bearer","expires_in":3600}""");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _openIddictExchanger.ExchangeAsync("cid", "csecret", new[] { "a" }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenFailed);
        exception.Data["reason"]?.ToString().ShouldBe("token 端点响应缺少 access_token");
    }

    [Fact]
    public async Task Success_with_non_json_body_throws_explicit_business_error()
    {
        // 2xx 但响应体非 JSON（反向代理回 200 登录页等）：容错转换器落 null Result，走同一守卫（覆盖 impersonation 侧）
        _handler.Enqueue(HttpStatusCode.OK, "<html>sign-in page</html>", "text/html");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _impersonationExchanger.ExchangeAsync("access-1", new Dictionary<string, string>()));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed);
        exception.Data["reason"]?.ToString().ShouldBe("token 端点响应缺少 access_token");
    }

    [Fact]
    public async Task Network_failure_propagates_without_suppression()
    {
        // HttpAgent 默认不抑制异常（SuppressExceptions 未启用）：网络层异常原样穿透，调用方自行处理。
        // 该契约未锚定的话，未来升级/配置改动引入默认重试或抑制会静默改变登录链路失败语义。
        _handler.EnqueueException(() => new HttpRequestException("connection refused"));

        await Should.ThrowAsync<HttpRequestException>(
            () => _openIddictExchanger.ExchangeAsync("cid", "csecret", new[] { "a" }));

        _handler.RecordedRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Impersonation_success_sends_bearer_and_merges_extra_parameters()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"access_token":"imp-1","expires_in":120,"refresh_token":"rt-1"}""");

        var result = await _impersonationExchanger.ExchangeAsync(
            "access-1",
            new Dictionary<string, string> { ["user_id"] = "u-1" },
            "cid-2");

        result.AccessToken.ShouldBe("imp-1");
        result.ExpiresIn.ShouldBe(120);
        result.RefreshToken.ShouldBe("rt-1");
        result.TokenType.ShouldBe("Bearer"); // token_type 缺省回落

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe("Bearer access-1");
        request.Form["grant_type"].ShouldBe(AbpAdminOpenIddictDefaults.GrantTypes.Impersonation);
        request.Form["client_id"].ShouldBe("cid-2");
        request.Form["user_id"].ShouldBe("u-1");
    }

    [Fact]
    public async Task Impersonation_oauth_error_maps_to_business_exception_reason()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"target user not found"}""");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _impersonationExchanger.ExchangeAsync("access-1", new Dictionary<string, string>()));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed);
        exception.Data["reason"]?.ToString().ShouldBe("invalid_grant: target user not found");
    }

    // ========== 六透镜审查轮补测：passwordless / linked-account 换票器 ==========

    [Fact]
    public async Task Passwordless_exchange_posts_email_and_token_without_authorization()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"access_token":"pl-1","token_type":"Bearer","expires_in":90,"refresh_token":"rt-1"}""");

        var result = await _passwordlessExchanger.ExchangeAsync("u@x.test", code: null, magicLinkToken: "tok-1", tenantId: null);

        result.AccessToken.ShouldBe("pl-1");
        result.RefreshToken.ShouldBe("rt-1");

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Form["grant_type"].ShouldBe(AbpAdminOpenIddictDefaults.GrantTypes.Passwordless);
        request.Form["email"].ShouldBe("u@x.test");
        request.Form["magic_link_token"].ShouldBe("tok-1");
        request.Authorization.ShouldBeNull();
        request.Headers.TryGetValue("__tenant", out var tenantHeader).ShouldBeTrue();
        (tenantHeader ?? string.Empty).ShouldBe(string.Empty, "host 换票不带租户值");
    }

    [Fact]
    public async Task Passwordless_exchange_error_is_masked_as_InvalidMagicLink_without_leaking_reason()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Too many attempts, please retry later"}""");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _passwordlessExchanger.ExchangeAsync("u@x.test", code: "123456", magicLinkToken: null, tenantId: null));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        // 匿名端点：限流/失败原因只进服务端日志，不随 error.data 下发（防探测限流与部署状态）
        exception.Data.Contains("reason").ShouldBeFalse();
    }

    [Fact]
    public async Task Linked_exchange_sends_bearer_target_user_and_tenant_header()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"access_token":"lk-1","expires_in":3600,"refresh_token":"rt-1"}""");

        var tenantId = Guid.NewGuid();
        var result = await _linkedExchanger.ExchangeAsync(
            Guid.NewGuid(), "access-1", "cid-3", tenantId);

        result.AccessToken.ShouldBe("lk-1");

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Form["grant_type"].ShouldBe(AbpAdminOpenIddictDefaults.GrantTypes.LinkedAccount);
        request.Form["target_user_id"].ShouldNotBeNullOrWhiteSpace();
        request.Form["client_id"].ShouldBe("cid-3");
        request.Authorization.ShouldBe("Bearer access-1");
        request.Headers["__tenant"].ShouldBe(tenantId.ToString());
    }
}
