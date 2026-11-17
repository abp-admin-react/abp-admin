using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class ScanProviderConfigurationTests : CollectionTestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Migrates_Legacy_Rpc_And_Billing_Without_Changing_Transaction_Configuration(int ankrCount)
    {
        var networkAdmin = GetRequiredService<CollectionNetworkAdminAppService>();
        var bsc = (await networkAdmin.GetNetworksAsync()).Single(x => x.Name == CollectionConsts.BscNetworkName).Id;
        await networkAdmin.ConfigureTransactionRpcAsync(new ConfigureTransactionRpcInput
        {
            NetworkId = bsc, RpcUrl = "https://rpc.example.com/legacy-endpoint",
        });
        await networkAdmin.SetNodeLogResultCapAsync(1234, bsc);
        await networkAdmin.ConfigureAnkrAsync(new ConfigureAnkrInput
        {
            NetworkId = bsc, PlatformAuthKey = "legacy-billing-key", Group = "prior-group",
        });
        var explorer = await networkAdmin.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
        {
            NetworkId = bsc, Provider = "EtherscanV2", Key = new string('e', 34),
        });
        var ankrIds = new Guid[ankrCount];
        for (var i = 0; i < ankrCount; i++)
        {
            var added = await networkAdmin.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
            {
                NetworkId = bsc, Provider = "RpcBsc", Key = new string((char)('a' + i), 64),
            });
            ankrIds[i] = added.Id;
        }

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261116040837_PreserveLegacyDepositWallet");
            await migrator.MigrateAsync();
            await uow.CompleteAsync();
        }

        var service = GetRequiredService<CollectionScanProviderAppService>();
        var list = await service.GetListAsync();
        list.LegacyRpcActive.ShouldBe(ankrCount == 0);
        list.LegacyBalancePending.ShouldBe(ankrCount != 1);
        list.Providers.Select(row => row.Id).ShouldBe(ankrIds.Take(1).Append(explorer.Id).Concat(ankrIds.Skip(1)));
        list.Providers.Where(row => row.Provider == "ankr").ShouldAllBe(row => row.Networks.Single().LogResultCap == 1234);
        (await networkAdmin.GetApiKeysAsync(bsc)).RpcKeys.Select(key => key.Id).ShouldBe(ankrIds);
        (await networkAdmin.GetTransactionRpcAsync(bsc)).RpcUrlMasked.ShouldBe("https://rpc.example.com/***");

        if (ankrCount == 0)
        {
            await service.ReorderAsync(new ReorderScanProvidersInput([explorer.Id]));
            (await service.GetListAsync()).LegacyRpcActive.ShouldBeTrue();
            using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            (await db.Networks.SingleAsync(network => network.Id == bsc)).PreferManagedScanProviders.ShouldBeTrue();
            await uow.CompleteAsync();
        }
        if (ankrCount == 1)
        {
            list.Providers[0].PlatformKeyMasked.ShouldBe("****-key");
            list.Providers[0].Group.ShouldBe("prior-group");
        }
        if (ankrCount == 2)
        {
            await service.ClaimLegacyBalanceAsync(ankrIds[1]);
            var claimed = await service.GetListAsync();
            claimed.LegacyBalancePending.ShouldBeFalse();
            claimed.Providers.Single(row => row.Id == ankrIds[1]).PlatformKeyMasked.ShouldBe("****-key");
        }
    }

    [Fact]
    public async Task Rolling_Back_The_Scanner_Rewinds_An_Unverified_Window()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var token = await db.Tokens.FirstAsync();
        token.LastScannedBlock = 120;
        token.ScanReplayFromBlock = 101;
        token.ScanReplayToBlock = 110;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261116040837_PreserveLegacyDepositWallet");
        }
        finally
        {
            await migrator.MigrateAsync();
        }

        (await db.Tokens.SingleAsync(row => row.Id == token.Id)).LastScannedBlock.ShouldBe(100);
        await uow.CompleteAsync();
    }

    [Fact]
    public async Task Reorders_Multinetwork_Credentials_Without_Changing_Transaction_Rpc()
    {
        var networkAdmin = GetRequiredService<CollectionNetworkAdminAppService>();
        var bsc = (await networkAdmin.GetNetworksAsync()).Single(x => x.Name == CollectionConsts.BscNetworkName).Id;
        var anotherNetwork = Guid.NewGuid();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            db.Networks.Add(new ColNetwork(anotherNetwork) { Name = "ETH", ChainId = 1, CreationTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        var oldTransactionKey = await networkAdmin.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
        {
            NetworkId = bsc, Provider = "RpcBsc", Key = new string('a', 64), Label = "Existing transaction RPC",
        });
        var service = GetRequiredService<CollectionScanProviderAppService>();
        var ankr = await service.CreateAsync(new SaveScanProviderInput
        {
            Provider = "ankr", ApiKey = new string('b', 64), PlatformAuthKey = "separate-billing-key",
            Group = "billing-team", IsEnabled = true,
            Networks = [new(bsc, 1200), new(anotherNetwork, null)],
        });
        ankr.KeyMasked.ShouldBe("****bbbb");
        ankr.PlatformKeyMasked.ShouldBe("****-key");
        var etherscan = await service.CreateAsync(new SaveScanProviderInput
        {
            Provider = "etherscan", ApiKey = new string('c', 34), IsEnabled = true,
            Networks = [new(bsc, null)],
        });

        await service.ReorderAsync(new ReorderScanProvidersInput([etherscan.Id, ankr.Id]));
        await Should.ThrowAsync<BusinessException>(() => service.ReorderAsync(new ReorderScanProvidersInput([etherscan.Id])));
        var changed = await service.UpdateAsync(ankr.Id, new SaveScanProviderInput
        {
            Provider = "ankr", Label = "Ankr backup", IsEnabled = false,
            Group = "billing-team", Networks = [new(anotherNetwork, null)],
        });

        changed.KeyMasked.ShouldBe(ankr.KeyMasked);
        changed.PlatformKeyMasked.ShouldBe(ankr.PlatformKeyMasked);
        changed.Networks.ShouldBe(new[] { new ScanProviderNetworkDto(anotherNetwork, null) });
        (await service.GetListAsync()).Providers.Select(row => row.Id).ShouldBe(new[] { etherscan.Id, ankr.Id });
        (await networkAdmin.GetApiKeysAsync(bsc)).RpcKeys.Select(key => key.Id).ShouldBe(new[] { oldTransactionKey.Id });

        await service.DeleteAsync(ankr.Id);
        (await service.GetListAsync()).Providers.Single().Id.ShouldBe(etherscan.Id);
    }
}
