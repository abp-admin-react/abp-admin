using System;
using AbpAdmin.Pages.Account;
using Shouldly;
using Xunit;

namespace AbpAdmin.Authorization;

/// <summary>
/// <see cref="AbpAdminLoginModel.StripPromptFromUrl"/> 的 characterization：
/// prompt=login 一次性摘除是防「authorize 回环」的关键一步（SPA 租户上下文恒发 prompt=login，
/// 认证完成后回跳若仍带 prompt，OpenIddict 会再次强制出登录页）。这里钉住字符串手术的全部
/// 旁路行为——state 等其余参数不做解码重编码、大小写不敏感、首/中/尾三个位置、无 query、
/// prompt 为唯一参数。改实现（尤其换 ParseQuery 重编码）前先读这些断言。
/// </summary>
public class LoginPromptStripTests
{
    [Theory]
    [InlineData("https://host/connect/authorize?client_id=x&state=abc", "https://host/connect/authorize?client_id=x&state=abc")]
    [InlineData("https://host/connect/authorize", "https://host/connect/authorize")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Without_Prompt_Returns_Url_Unchanged(string? url, string expected)
    {
        AbpAdminLoginModel.StripPromptFromUrl(url).ShouldBe(expected);
    }

    [Fact]
    public void Strips_Trailing_Prompt()
    {
        var stripped = AbpAdminLoginModel.StripPromptFromUrl(
            "https://host/connect/authorize?client_id=x&state=abc&prompt=login");
        stripped.ShouldBe("https://host/connect/authorize?client_id=x&state=abc");
    }

    [Fact]
    public void Strips_Leading_Prompt()
    {
        var stripped = AbpAdminLoginModel.StripPromptFromUrl(
            "https://host/connect/authorize?prompt=login&client_id=x");
        stripped.ShouldBe("https://host/connect/authorize?client_id=x");
    }

    [Fact]
    public void Strips_Middle_Prompt_With_Separator_Collapse()
    {
        var stripped = AbpAdminLoginModel.StripPromptFromUrl(
            "https://host/connect/authorize?client_id=x&prompt=login&state=abc");
        stripped.ShouldBe("https://host/connect/authorize?client_id=x&state=abc");
    }

    [Fact]
    public void Strips_Prompt_As_Only_Parameter()
    {
        var stripped = AbpAdminLoginModel.StripPromptFromUrl("https://host/connect/authorize?prompt=login");
        stripped.ShouldBe("https://host/connect/authorize");
    }

    [Fact]
    public void Strips_All_Prompt_Values_Case_Insensitively()
    {
        var stripped = AbpAdminLoginModel.StripPromptFromUrl(
            "https://host/connect/authorize?Prompt=login&client_id=x&prompt=consent");
        stripped.ShouldBe("https://host/connect/authorize?client_id=x");
    }

    [Fact]
    public void Keeps_Encoded_Values_Intact_No_Decode_Reencode()
    {
        // state 是 base64url/编码串：纯字符串手术不得经 ParseQuery 的 +→空格语义漂移
        var stripped = AbpAdminLoginModel.StripPromptFromUrl(
            "https://host/connect/authorize?state=a%2Fb%2Bc%3Dd&prompt=login&scope=openid");
        stripped.ShouldBe("https://host/connect/authorize?state=a%2Fb%2Bc%3Dd&scope=openid");
    }
}
