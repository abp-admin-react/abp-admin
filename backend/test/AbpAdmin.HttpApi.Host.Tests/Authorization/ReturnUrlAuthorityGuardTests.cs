using AbpAdmin.Pages.Account;
using Shouldly;
using Xunit;

namespace AbpAdmin.Pages.Account;

public class ReturnUrlAuthorityGuardTests
{
    [Fact]
    public void Embedded_userinfo_host_is_replaced_with_the_local_root()
    {
        string? url = "http://localhost:8100@evil.example/phish";
        string? hash = "@evil.example";

        ReturnUrlAuthorityGuard.Sanitize(ref url, ref hash);

        url.ShouldBe("~/");
        hash.ShouldBeNull();
    }

    [Fact]
    public void Local_authorize_return_url_is_kept()
    {
        string? url = "/connect/authorize?hostLogin=1";
        string? hash = "#section";

        ReturnUrlAuthorityGuard.Sanitize(ref url, ref hash);

        url.ShouldBe("/connect/authorize?hostLogin=1");
        hash.ShouldBe("#section");
    }
}
