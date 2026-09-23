using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AbpAdmin;

/// <summary>
/// Replays Program.cs up to the point where the host's services are configured, so that tests can
/// inspect the real startup pipeline rather than a hand-built approximation of it.
/// </summary>
/// <remarks>
/// Shared deliberately. Every test that wants to look at startup has to look at the same startup,
/// otherwise one copy of this pipeline drifts, its tests keep passing, and they stop describing the
/// application that actually ships.
/// </remarks>
internal static class HostUnderTest
{
    /// <summary>
    /// Returns the <see cref="IServiceCollection"/> the host builds, without Build() and without
    /// Autofac.
    /// </summary>
    internal static async Task<IServiceCollection> CreateServiceCollectionAsync(
        IEnumerable<KeyValuePair<string, string?>>? configurationOverrides = null)
    {
        var builder = await CreateBuilderAsync(configurationOverrides);

        return builder.Services;
    }

    /// <summary>
    /// Builds the host's <see cref="WebApplicationBuilder"/> and runs
    /// <see cref="AbpAdminHttpApiHostModule"/> through it, stopping short of Build().
    /// </summary>
    /// <param name="configurationOverrides">
    /// Configuration entries layered on top of the host's own files, for reaching a branch the
    /// shipped configuration switches off. Callers that pass nothing get the configuration exactly
    /// as it is deployed.
    /// </param>
    /// <param name="useAutofac">
    /// Whether to install ABP's Autofac service provider factory, as Program.cs does. This changes
    /// what a later Build() means, so callers pick according to what they are trying to observe, and
    /// each one documents its choice.
    /// </param>
    /// <param name="beforeAddApplication">
    /// Runs after the builder exists with its configuration sources but before
    /// <c>AddApplicationAsync</c>, the seam Program.cs uses for
    /// <see cref="AbpAdminDbPathNormalizer"/> — module initialization reads the connection string,
    /// so anything that has to rewrite it must run in this window. Callers that intend to take the
    /// host all the way to <c>InitializeApplicationAsync</c> must isolate the database here — either
    /// normalize the path (Program.cs parity, but module init then writes to the real dev database)
    /// or override <c>ConnectionStrings:Default</c> to a throwaway copy, as
    /// <c>AnonymousEndpointSweepTests</c> does (init needs a migrated schema, so an empty file
    /// will not boot).
    /// </param>
    /// <remarks>
    /// A fresh builder is returned per call on purpose: Build() makes a ServiceCollection read-only,
    /// so one already-built collection cannot be reused.
    /// <para>
    /// The environment is pinned to Development because outside it the host's PreConfigureServices
    /// loads openiddict.pfx from disk, which no test machine has. Registrations that exist only in the
    /// non-Development branch are consequently not covered by anything built here.
    /// </para>
    /// </remarks>
    internal static async Task<WebApplicationBuilder> CreateBuilderAsync(
        IEnumerable<KeyValuePair<string, string?>>? configurationOverrides = null,
        bool useAutofac = false,
        Action<WebApplicationBuilder>? beforeAddApplication = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AbpAdminHttpApiHostModule).Assembly.GetName().Name,
            ContentRootPath = ResolveHostContentRootPath(),
            EnvironmentName = Environments.Development
        });

        builder.Host.AddAppSettingsSecretsJson();

        if (useAutofac)
        {
            builder.Host.UseAutofac();
        }

        if (configurationOverrides != null)
        {
            // Added last so it outranks appsettings.json, appsettings.secrets.json, the environment
            // variables and everything else CreateBuilder set up. ConfigureHostBuilder applies
            // AddAppSettingsSecretsJson to builder.Configuration on the spot rather than deferring
            // it to Build(), so this really is the last source in the chain.
            builder.Configuration.AddInMemoryCollection(configurationOverrides);
        }

        beforeAddApplication?.Invoke(builder);

        await builder.AddApplicationAsync<AbpAdminHttpApiHostModule>();

        return builder;
    }

    private static string ResolveHostContentRootPath()
    {
        // The host's own appsettings.json is read on purpose, so registrations gated on
        // configuration (Redis, the database provider, the auth server URL) are exercised as they
        // ship. Callers that need a different branch layer an in-memory source over this rather than
        // editing the file on disk.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AbpAdmin.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find AbpAdmin.slnx in any directory above '{AppContext.BaseDirectory}', " +
                "so the host content root cannot be located.");
        }

        return Path.Combine(directory.FullName, "src", "AbpAdmin.HttpApi.Host");
    }
}
