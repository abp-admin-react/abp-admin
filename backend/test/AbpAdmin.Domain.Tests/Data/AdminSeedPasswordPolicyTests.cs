using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Data;

public class AdminSeedPasswordPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1", "http://localhost:8100", false)]
    [InlineData("127.0.0.1", "http://127.0.0.1:8100", true)]
    [InlineData("[::1]", "https://[::1]:8100/", false)]
    public void Local_compose_accepts_the_template_password(string bind, string publicUrl, bool explicitlyConfigured)
    {
        var configuration = Configure(
            bind,
            publicUrl,
            explicitlyConfigured ? AbpAdminConsts.AdminPasswordDefaultValue : null);

        AbpAdminDbMigrationService.ResolveAdminPassword(configuration)
            .ShouldBe(AbpAdminConsts.AdminPasswordDefaultValue);
    }

    [Theory]
    [InlineData("0.0.0.0", "http://localhost:8100", false)]
    [InlineData("0.0.0.0", "http://localhost:8100", true)]
    [InlineData("127.0.0.1", "https://preview.example", false)]
    [InlineData("127.0.0.1", "ftp://localhost:8100", false)]
    [InlineData("127.0.0.1", null, false)]
    public void Compose_requires_both_the_bind_and_public_url_to_be_loopback(
        string bind,
        string? publicUrl,
        bool explicitlyConfigured)
    {
        var configuration = Configure(
            bind,
            publicUrl,
            explicitlyConfigured ? AbpAdminConsts.AdminPasswordDefaultValue : null,
            allowDefault: true);

        Should.Throw<AbpInitializationException>(() =>
            AbpAdminDbMigrationService.ResolveAdminPassword(configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_host_requires_an_explicit_development_setting_for_the_template_password(bool explicitlyConfigured)
    {
        var password = explicitlyConfigured ? AbpAdminConsts.AdminPasswordDefaultValue : null;
        Should.Throw<AbpInitializationException>(() =>
            AbpAdminDbMigrationService.ResolveAdminPassword(Configure(password: password)));

        AbpAdminDbMigrationService.ResolveAdminPassword(Configure(password: password, allowDefault: true))
            .ShouldBe(AbpAdminConsts.AdminPasswordDefaultValue);
    }

    [Fact]
    public void Non_template_password_is_preserved_for_a_network_bind()
    {
        const string password = "ExampleOnly-Admin42!";
        var configuration = Configure("0.0.0.0", "https://preview.example", password);

        AbpAdminDbMigrationService.ResolveAdminPassword(configuration).ShouldBe(password);
    }

    private static IConfiguration Configure(
        string? bind = null,
        string? publicUrl = null,
        string? password = null,
        bool allowDefault = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LocalDockerBindAddress"] = bind,
                ["OpenIddict:Applications:AbpAdmin_App:RootUrl"] = publicUrl,
                ["Identity:AdminPassword"] = password,
                ["Identity:AllowDefaultAdminPassword"] = allowDefault.ToString()
            })
            .Build();
}
