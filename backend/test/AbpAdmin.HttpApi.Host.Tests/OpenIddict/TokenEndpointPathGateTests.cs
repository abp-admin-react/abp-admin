using AbpAdmin.OpenIddict;
using Shouldly;
using Xunit;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// NV-16 回归锁：限流器路径门的归一化语义。OpenIddict 受理判定（对本地缓存程序集
/// InferEndpointType 的反编译口径）= 路径 OrdinalIgnoreCase 等值 + 恰好一个尾斜杠；
/// 门限必须与受理同口径（TV-06b 修复），不得更宽（漏计）也不必更窄（误放）。
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
