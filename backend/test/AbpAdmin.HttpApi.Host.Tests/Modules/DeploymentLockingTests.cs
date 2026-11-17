using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.DistributedLocking;
using Xunit;

namespace AbpAdmin.Modules.Tests;

public class DeploymentLockingTests
{
    [Fact]
    public async Task Separate_Nodes_Using_Shared_Configuration_Exclude_Each_Other()
    {
        await using var redis = new ContainerBuilder("redis:7-alpine")
            .WithPortBinding(6379, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();
        await redis.StartAsync();
        var values = new Dictionary<string, string?>
        {
            ["Redis:IsEnabled"] = "true",
            ["Redis:Configuration"] = $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        async Task<ServiceProvider> BuildNode()
        {
            var services = await HostUnderTest.CreateServiceCollectionAsync(values);
            var first = DeploymentLockingConfiguration.Configure(services, configuration);
            DeploymentLockingConfiguration.Configure(services, configuration).ShouldBeSameAs(first);
            return services.BuildServiceProvider();
        }
        await using var firstNode = await BuildNode();
        await using var secondNode = await BuildNode();
        var firstLock = firstNode.GetRequiredService<IAbpDistributedLock>();
        var secondLock = secondNode.GetRequiredService<IAbpDistributedLock>();
        const string key = "AbpAdmin:DeploymentModules:integration-test";
        await using (var held = await firstLock.TryAcquireAsync(key, TimeSpan.FromSeconds(2)))
        {
            held.ShouldNotBeNull();
            await using var competing = await secondLock.TryAcquireAsync(key, TimeSpan.Zero);
            competing.ShouldBeNull();
        }
        await using var afterRelease = await secondLock.TryAcquireAsync(key, TimeSpan.FromSeconds(2));
        afterRelease.ShouldNotBeNull();
    }
}
