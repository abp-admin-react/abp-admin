using AbpAdmin.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace AbpAdmin.Authorization;

public class HostLoginTenantResolveTests
{
    [Theory]
    [InlineData("/connect/authorize", "?hostLogin=1", true)]
    [InlineData("/connect/authorize", "?hostLogin=1&__tenant=demo", false)]
    [InlineData("/connect/authorize", "?hostLogin=1&hostLogin=1", false)]
        [InlineData("/connect/authorize", "?hostLogin=1&__tenant=", true)]
        [InlineData("/connect/authorize", "?hostLogin=%201%20", true)]
        [InlineData("/Account/Login", "?__tenant=other&ReturnUrl=/connect/authorize?hostLogin=1", true)]
    [InlineData("/connect/token", "?hostLogin=1", false)]
    [InlineData("/api/abp/application-configuration", "?hostLogin=1", false)]
    [InlineData("/Account/Login", "?hostLogin=1", false)]
    public void Direct_marker_only_applies_to_the_authorize_request(string path, string query, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;
        request.QueryString = new QueryString(query);

        Assert.Equal(expected, HostLoginTenantResolveContributor.IsHostLogin(request));
    }

    [Theory]
    [InlineData("/Account/Login", "/connect/authorize?hostLogin=1", true)]
    [InlineData("/Account/TwoFactorVerification", "/connect/authorize?hostLogin=1&state=opaque", true)]
    [InlineData("/Account/Login", "/connect/authorize?hostLogin=1&__tenant=demo", false)]
    [InlineData("/Account/Login", "/connect/authorize-extra?hostLogin=1", false)]
    [InlineData("/Account/Login", "https://example.test/connect/authorize?hostLogin=1", false)]
    [InlineData("/Account/Manage", "/connect/authorize?hostLogin=1", false)]
    [InlineData("/api/abp/application-configuration", "/connect/authorize?hostLogin=1", false)]
    public void Account_pages_only_accept_the_local_authorize_return_url(string path, string returnUrl, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;
        request.Method = HttpMethods.Post;
        request.QueryString = new QueryString(QueryHelpers.AddQueryString(string.Empty, "returnUrl", returnUrl));

        Assert.Equal(expected, HostLoginTenantResolveContributor.IsHostLogin(request));
    }
}
