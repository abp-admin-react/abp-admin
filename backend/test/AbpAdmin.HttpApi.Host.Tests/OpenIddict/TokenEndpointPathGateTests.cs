using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 回归锁：限流器路径门的归一化语义。OpenIddict 受理判定（对本地缓存程序集
/// InferEndpointType 的反编译口径）= 路径 OrdinalIgnoreCase 等值 + 恰好一个尾斜杠；
/// 门限必须与受理同口径（修复），不得更宽（漏计）也不必更窄（误放）。
/// </summary>
public class TokenEndpointPathGateTests
{
    [Theory]
    [InlineData("/connect/token", true)]
    [InlineData("/CONNECT/TOKEN", true)]      // OpenIddict 大小写不敏感
    [InlineData("/connect/token/", true)]     // 恰好一个尾斜杠：OpenIddict 同样受理（修复前旁路半区）
    [InlineData("/connect/token//", false)]   // 双尾斜杠：OpenIddict 不受理，门不放大计数面
    [InlineData("/connect/authorize", false)]
    [InlineData("/connect/tokenx", false)]
    [InlineData("/api/connect/token", false)]
    [InlineData("", false)]
    public void Gate_matches_OpenIddict_acceptance_semantics(string path, bool expected)
    {
        TokenEndpointRateLimitingMiddleware.IsTokenEndpointPath(
            Microsoft.AspNetCore.Http.PathString.FromUriComponent(path)).ShouldBe(expected);
    }
}

public class TokenEndpointMalformedFormTests
{
    [Fact]
    public async Task Null_byte_form_is_400_and_still_counts_the_ip_bucket()
    {
        var checker = new RecordingChecker();
        var nextCalled = false;
        var middleware = new TokenEndpointRateLimitingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IOperationRateLimitingChecker>(checker)
            .BuildServiceProvider();
        context.Response.Body = new System.IO.MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/connect/token";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var bytes = Encoding.UTF8.GetBytes("grant_type=password&username=a%00&password=x");
        context.Request.Body = new System.IO.MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeFalse();
        checker.Checks.ShouldBe(1);
        checker.LastPolicy.ShouldBe(OperationRateLimitingPolicyNames.TokenEndpoint);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        document.RootElement.GetProperty("error").GetString().ShouldBe("invalid_request");
    }

    private sealed class RecordingChecker : IOperationRateLimitingChecker
    {
        public int Checks { get; private set; }

        public string? LastPolicy { get; private set; }

        public Task CheckAsync(string policyName, OperationRateLimitingContext context)
        {
            Checks++;
            LastPolicy = policyName;
            return Task.CompletedTask;
        }

        public Task<bool> IsAllowedAsync(string policyName, OperationRateLimitingContext context) =>
            Task.FromResult(true);

        public Task<OperationRateLimitingStatus> GetStatusAsync(string policyName, OperationRateLimitingContext context) =>
            Task.FromResult(new OperationRateLimitingStatus());

        public Task ResetAsync(string policyName, OperationRateLimitingContext context) =>
            Task.CompletedTask;
    }
}
