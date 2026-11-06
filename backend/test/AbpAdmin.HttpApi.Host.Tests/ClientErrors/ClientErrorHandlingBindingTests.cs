using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientErrorHandlingBindingTests
{
    [Fact]
    public void Unknown_ClientErrorHandling_Key_Should_Fail_Strict_Bind()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientErrorHandling:IncludeCorrelationId"] = "true",
                ["ClientErrorHandling:ShowAllErrors"] = "true",
            })
            .Build();

        var options = new ClientErrorHandlingOptions();
        Should.Throw<InvalidOperationException>(() =>
            configuration.GetSection(ClientErrorHandlingOptions.SectionName)
                .Bind(options, binder => binder.ErrorOnUnknownConfiguration = true));
    }

    [Fact]
    public void Known_ClientErrorHandling_Keys_Should_Bind()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientErrorHandling:IncludeCorrelationId"] = "false",
                ["ClientErrorHandling:MaxPublicMessageLength"] = "800",
            })
            .Build();

        var options = new ClientErrorHandlingOptions();
        configuration.GetSection(ClientErrorHandlingOptions.SectionName)
            .Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        options.IncludeCorrelationId.ShouldBeFalse();
        options.MaxPublicMessageLength.ShouldBe(800);
    }

    [Fact]
    public void Host_Should_Only_Strict_Bind_ClientErrorHandling_Section()
    {
        var root = FindRepositoryRoot();
        var hostModule = File.ReadAllText(Path.Combine(
            root, "backend", "framework-app", "AbpAdmin.HttpApi.Host", "AbpAdminHttpApiHostModule.cs"));
        hostModule.ShouldContain("ErrorOnUnknownConfiguration = true");
        var occurrences = 0;
        var index = 0;
        while ((index = hostModule.IndexOf("ErrorOnUnknownConfiguration = true", index, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            index += "ErrorOnUnknownConfiguration = true".Length;
        }

        occurrences.ShouldBe(1);
        hostModule.ShouldContain("ClientErrorHandlingOptions");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "AbpAdmin.slnx"))
                && File.Exists(Path.Combine(directory.FullName, "web", "package.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find repository root with dual anchors.");
    }
}
