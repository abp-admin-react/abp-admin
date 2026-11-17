using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.SignalR;

/// <summary>
/// Execute the real connection admission and all five real notifier methods against one recorded
/// SignalR transport. Only ABP grants and the wire are substituted; tests assert recipient payloads,
/// so a notifier accidentally returning to the old shared group or another group's event is detected.
/// </summary>
public class ChainStatusHubPermissionTests
{
    [Theory]
    [InlineData(new string[0], new string[0])]
    [InlineData(new[] { "AbpAdmin.ModuleManagement" }, new string[0])]
    [InlineData(new[] { "SettingUi.ShowSettingPage" }, new string[0])]
    [InlineData(new[] { CollectionPermissions.Collections.Default },
        new[] { "ChainDepositChanged", "ChainSweepChanged", "ChainVaultCheck" })]
    [InlineData(new[] { CollectionPermissions.Network.Admin },
        new[] { "ChainScanProgress", "ChainDepositChanged", "ChainSweepChanged", "ChainVaultCheck" })]
    [InlineData(new[] { CollectionPermissions.Withdraw.Admin }, new[] { "ChainHotWalletChanged" })]
    [InlineData(new[] { CollectionPermissions.Collections.Default, CollectionPermissions.Withdraw.Admin },
        new[] { "ChainDepositChanged", "ChainSweepChanged", "ChainVaultCheck", "ChainHotWalletChanged" })]
    [InlineData(new[] { CollectionPermissions.Collections.Default, CollectionPermissions.Network.Admin, CollectionPermissions.Withdraw.Admin },
        new[] { "ChainScanProgress", "ChainDepositChanged", "ChainSweepChanged", "ChainVaultCheck", "ChainHotWalletChanged" })]
    public async Task A_Host_Connection_Receives_Only_Events_Its_Http_Read_Permissions_Allow(string[] permissions, string[] expectedEvents)
    {
        using var transport = new RecordedTransport();
        var hub = transport.CreateHub("reader", permissions);
        await hub.OnConnectedAsync();
        if (expectedEvents.Length == 0) hub.Context.Received(1).Abort();
        else hub.Context.DidNotReceive().Abort();

        await transport.PublishAllAsync();
        transport.Deliveries.Select(x => x.Event).ShouldBe(expectedEvents);
        transport.Deliveries.ShouldAllBe(x => x.Connection == "reader" && x.Payload != null);
        // The outgoing protocol stays compatible; only recipients have changed.
        transport.Publications.Select(x => x.Group).ShouldBe(new[]
        {
            ChainHubGroups.ScanProgress, ChainHubGroups.Operations, ChainHubGroups.Operations,
            ChainHubGroups.Operations, ChainHubGroups.HotWallet
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Anonymous_Or_Tenant_Connection_Is_Rejected_Even_With_All_Grants(bool tenant, bool authenticated)
    {
        using var transport = new RecordedTransport();
        var hub = transport.CreateHub("denied",
            [CollectionPermissions.Collections.Default, CollectionPermissions.Network.Admin, CollectionPermissions.Withdraw.Admin],
            tenant ? Guid.NewGuid() : null, authenticated);
        await hub.OnConnectedAsync();
        hub.Context.Received(1).Abort();
        await transport.PublishAllAsync();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_Low_Privilege_Readers_Do_Not_Receive_Another_Groups_Funds_Events()
    {
        using var transport = new RecordedTransport();
        foreach (var hub in new[]
        {
            transport.CreateHub("ordinary", []),
            transport.CreateHub("operations", [CollectionPermissions.Collections.Default]),
            transport.CreateHub("network", [CollectionPermissions.Network.Admin]),
            transport.CreateHub("withdrawal", [CollectionPermissions.Withdraw.Admin])
        }) await hub.OnConnectedAsync();

        await transport.PublishAllAsync();
        transport.Deliveries.Where(x => x.Event == "ChainScanProgress").Select(x => x.Connection).ShouldBe(["network"]);
        foreach (var eventName in new[] { "ChainDepositChanged", "ChainSweepChanged", "ChainVaultCheck" })
            transport.Deliveries.Where(x => x.Event == eventName).Select(x => x.Connection).Order().ShouldBe(["network", "operations"]);
        transport.Deliveries.Where(x => x.Event == "ChainHotWalletChanged").Select(x => x.Connection).ShouldBe(["withdrawal"]);
        transport.Deliveries.ShouldNotContain(x => x.Connection == "ordinary");
    }

    [Fact]
    public void Hub_Still_Requires_Authenticated_Transport()
    {
        typeof(ChainStatusHub).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull();
    }

    private sealed class RecordedTransport : IDisposable
    {
        private readonly Dictionary<string, HashSet<string>> _members = new();
        private readonly List<ServiceProvider> _providers = new();
        private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
        private readonly IHubContext<ChainStatusHub> _context = Substitute.For<IHubContext<ChainStatusHub>>();
        public List<(string Group, string Event, object Payload)> Publications { get; } = [];
        public List<(string Connection, string Event, object Payload)> Deliveries { get; } = [];

        public RecordedTransport()
        {
            _groups.AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var connection = call.ArgAt<string>(0);
                var group = call.ArgAt<string>(1);
                if (!_members.TryGetValue(group, out var connections)) _members[group] = connections = [];
                connections.Add(connection);
                return Task.CompletedTask;
            });
            var clients = Substitute.For<IHubClients>();
            _context.Clients.Returns(clients);
            clients.Group(Arg.Any<string>()).Returns(call =>
            {
                var group = call.Arg<string>();
                var proxy = Substitute.For<IClientProxy>();
                proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(send =>
                {
                    var eventName = send.ArgAt<string>(0);
                    var payload = send.ArgAt<object?[]>(1).Single()!;
                    Publications.Add((group, eventName, payload));
                    if (_members.TryGetValue(group, out var connections))
                        foreach (var connection in connections) Deliveries.Add((connection, eventName, payload));
                    return Task.CompletedTask;
                });
                return proxy;
            });
        }

        public ChainStatusHub CreateHub(string connectionId, string[] granted, Guid? tenantId = null, bool authenticated = true)
        {
            var tenant = Substitute.For<ICurrentTenant>();
            tenant.Id.Returns(tenantId);
            var user = Substitute.For<ICurrentUser>();
            user.IsAuthenticated.Returns(authenticated);
            user.Id.Returns(Guid.NewGuid());
            var services = new ServiceCollection().AddSingleton(tenant).AddSingleton(user).BuildServiceProvider();
            _providers.Add(services);
            var checker = Substitute.For<IPermissionChecker>();
            checker.IsGrantedAsync(Arg.Any<string[]>()).Returns(call =>
            {
                var result = new MultiplePermissionGrantResult(call.Arg<string[]>(), PermissionGrantResult.Prohibited);
                foreach (var permission in result.Result.Keys.ToArray())
                    if (granted.Contains(permission)) result.Result[permission] = PermissionGrantResult.Granted;
                return Task.FromResult(result);
            });
            var caller = Substitute.For<HubCallerContext>();
            caller.ConnectionId.Returns(connectionId);
            return new ChainStatusHub(checker)
            {
                Context = caller, Groups = _groups, LazyServiceProvider = new AbpLazyServiceProvider(services)
            };
        }

        public async Task PublishAllAsync()
        {
            var notifier = new SignalRChainPushNotifier(_context);
            await notifier.PushScanProgressAsync(new ChainScanProgressEto { Symbol = "USDT", LastScannedBlock = 7 });
            await notifier.PushDepositChangedAsync(new ChainDepositChangedEto { UserId = Guid.NewGuid(), Amount = "10" });
            await notifier.PushSweepChangedAsync(new ChainSweepChangedEto { UserId = Guid.NewGuid(), Amount = "9" });
            await notifier.PushVaultCheckAsync(new ChainVaultCheckEto { VaultBalance = "100", UserTotal = "90" });
            await notifier.PushHotWalletChangedAsync(new ChainHotWalletChangedEto { Amount = "8", To = "withdrawal-destination" });
        }

        public void Dispose()
        {
            foreach (var provider in _providers) provider.Dispose();
        }
    }
}
