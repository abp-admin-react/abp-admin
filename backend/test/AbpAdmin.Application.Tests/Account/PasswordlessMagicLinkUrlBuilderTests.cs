using Shouldly;
using Xunit;

namespace AbpAdmin.Account;

/// <summary>
/// Magic Link URL 构造的纯字符串逻辑（Format 静态方法，不依赖容器/配置）：
/// host 去掉 "{0}." 段、租户替换占位符并追加 tenant 参数、固定域名原样、
/// 尾斜杠归一、email/租户名 URL 转义。
/// </summary>
public class PasswordlessMagicLinkUrlBuilderTests
{
    private const string Token = "abc123def456abc123def456abc12345";

    [Fact]
    public void Format_Should_Strip_Tenant_Placeholder_For_Host()
    {
        var url = PasswordlessMagicLinkUrlBuilder.Format(
            "http://{0}.localhost:8000", Token, "admin@abp.io", tenantName: null);

        url.ShouldStartWith("http://localhost:8000/user/login?");
        url.ShouldContain($"magicLinkToken={Token}");
        url.ShouldContain("email=admin%40abp.io");
        url.ShouldNotContain("tenant=");
    }

    [Fact]
    public void Format_Should_Replace_Placeholder_And_Append_Tenant_For_Tenant()
    {
        var url = PasswordlessMagicLinkUrlBuilder.Format(
            "http://{0}.localhost:8000", Token, "u@abp.io", tenantName: "acme");

        url.ShouldStartWith("http://acme.localhost:8000/user/login?");
        url.ShouldContain($"magicLinkToken={Token}");
        url.ShouldContain("email=u%40abp.io");
        url.ShouldContain("tenant=acme");
    }

    [Fact]
    public void Format_Should_Keep_Fixed_Domain_Unchanged()
    {
        var url = PasswordlessMagicLinkUrlBuilder.Format(
            "https://app.example.com", Token, "u@abp.io", tenantName: null);

        url.ShouldBe($"https://app.example.com/user/login?magicLinkToken={Token}&email=u%40abp.io");
    }

    [Fact]
    public void Format_Should_Trim_Trailing_Slash()
    {
        var url = PasswordlessMagicLinkUrlBuilder.Format(
            "https://app.example.com/", Token, "u@abp.io", tenantName: null);

        url.ShouldStartWith("https://app.example.com/user/login?");
    }

    [Fact]
    public void Format_Should_UrlEncode_Tenant_Name()
    {
        var url = PasswordlessMagicLinkUrlBuilder.Format(
            "http://{0}.localhost:8000", Token, "u@abp.io", tenantName: "acme corp");

        url.ShouldContain("tenant=acme%20corp");
    }
}
