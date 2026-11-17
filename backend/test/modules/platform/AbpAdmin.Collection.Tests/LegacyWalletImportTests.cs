using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nethereum.HdWallet;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class LegacyWalletImportTests : CollectionTestBase
{
    [Fact]
    public async Task Legacy_Addresses_Are_Restored_And_The_Highest_Issued_Index_Stays_Reserved()
    {
        const int activeCount = 1273;
        const int lastIssued = 1278;
        const int retiredIndex = 1276;
        var mnemonic = HdKeyService.GenerateMnemonic();
        var legacyWallet = new Wallet(mnemonic, string.Empty, string.Empty, null);
        var users = Enumerable.Range(HdKeyService.FirstUserAddressIndex, activeCount)
            .Select(index => (Id: Guid.NewGuid(), Index: index)).ToArray();
        await SeedUsersAsync(users.Select(user => user.Id).ToArray());
        var active = users.Select(user => new ImportLegacyWalletAddressInput
        {
            UserId = user.Id,
            AddressIndex = user.Index,
            Address = legacyWallet.GetAccount(user.Index).Address,
            IsActive = true,
        }).ToArray();
        var retired = new ImportLegacyWalletAddressInput
        {
            AddressIndex = retiredIndex,
            Address = legacyWallet.GetAccount(retiredIndex).Address,
            IsActive = false,
        };
        var manifest = new ImportLegacyWalletInput
        {
            Mnemonic = mnemonic,
            AddressIndexHighWaterMark = lastIssued,
            Addresses = [..active, retired],
        };
        var networkId = await GetRequiredService<DepositAddressManager>().ResolveBscNetworkIdAsync();
        var admin = GetRequiredService<CollectionNetworkAdminAppService>();
        var receipt = await admin.ImportLegacyWalletAsync(manifest, networkId);
        receipt.ImportedAddressCount.ShouldBe(activeCount);
        receipt.RetiredAddressCount.ShouldBe(1);
        receipt.AddressIndexHighWaterMark.ShouldBe(lastIssued);
        receipt.NextAddressIndex.ShouldBe(lastIssued + 1);

        await InCollectionAsync(async db =>
        {
            var rows = await db.DepositAddresses.AsNoTracking().Where(address => address.NetworkId == networkId)
                .OrderBy(address => address.AddressIndex).ToListAsync();
            rows.Count.ShouldBe(activeCount + 1);
            rows.Where(address => !address.IsRetired)
                .Select(address => ((Guid?)address.UserId, address.AddressIndex, address.Address))
                .ShouldBe(active.Select(address => (address.UserId, address.AddressIndex, address.Address)));
            rows[^1].IsRetired.ShouldBeTrue();
            rows[^1].Address.ShouldBe(retired.Address);
            rows[^1].AddressIndex.ShouldBe(retiredIndex);
            (await db.Networks.SingleAsync(network => network.Id == networkId))
                .LegacyAddressIndexHighWaterMark.ShouldBe(lastIssued);
        });

        var wallet = await GetRequiredService<CollectionWalletAdminAppService>().GetAsync(networkId);
        wallet.HasMnemonic.ShouldBeTrue();
        wallet.FuelAddress.ShouldBe(legacyWallet.GetAccount(HdKeyService.GasAddressIndex).Address);
        var deposits = GetRequiredService<ICollectionIntegrationService>();
        (await deposits.GetDepositAddressAsync(users[0].Id)).Address.ShouldBe(active[0].Address);

        await InCollectionAsync(async db =>
        {
            var token = await db.Tokens.SingleAsync(item => item.NetworkId == networkId && item.Symbol == "USDT");
            token.LastScannedBlock = 100;
            token.VaultAddress = "0x" + new string('9', 40);
        });
        var newUserId = Guid.NewGuid();
        await SeedUsersAsync([newUserId]);
        (await deposits.GetDepositAddressAsync(newUserId)).Address
            .ShouldBe(legacyWallet.GetAccount(lastIssued + 1).Address);

        var replay = await admin.ImportLegacyWalletAsync(manifest, networkId);
        replay.NextAddressIndex.ShouldBe(lastIssued + 2);
        await InCollectionAsync(async db =>
        {
            var newAddress = await db.DepositAddresses.AsNoTracking()
                .SingleAsync(address => address.NetworkId == networkId && address.UserId == newUserId);
            newAddress.AddressIndex.ShouldBe(lastIssued + 1);
            (await db.DepositAddresses.CountAsync(address => address.NetworkId == networkId)).ShouldBe(activeCount + 2);
        });
    }

    [Fact]
    public async Task Incorrect_Derivation_Rolls_Back_The_Whole_Import()
    {
        var mnemonic = HdKeyService.GenerateMnemonic();
        var legacyWallet = new Wallet(mnemonic, string.Empty, string.Empty, null);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await SeedUsersAsync(users);
        var input = new ImportLegacyWalletInput
        {
            Mnemonic = mnemonic,
            AddressIndexHighWaterMark = 3,
            Addresses =
            [
                new ImportLegacyWalletAddressInput
                {
                    UserId = users[0], AddressIndex = 2, Address = legacyWallet.GetAccount(2).Address, IsActive = true,
                },
                new ImportLegacyWalletAddressInput
                {
                    UserId = users[1], AddressIndex = 3, Address = legacyWallet.GetAccount(4).Address, IsActive = true,
                },
            ],
        };
        var networkId = await GetRequiredService<DepositAddressManager>().ResolveBscNetworkIdAsync();
        var admin = GetRequiredService<CollectionNetworkAdminAppService>();
        await Should.ThrowAsync<BusinessException>(() => admin.ImportLegacyWalletAsync(input, networkId));
        await InCollectionAsync(async db =>
        {
            var network = await db.Networks.SingleAsync(item => item.Id == networkId);
            network.MnemonicCipher.ShouldBeNull();
            network.LegacyAddressIndexHighWaterMark.ShouldBeNull();
            (await db.DepositAddresses.AnyAsync(address => address.NetworkId == networkId)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Generating_A_New_Seed_Does_Not_Overwrite_Addresses_Without_A_Seed()
    {
        var networkId = await GetRequiredService<DepositAddressManager>().ResolveBscNetworkIdAsync();
        var userId = Guid.NewGuid();
        await SeedUsersAsync([userId]);
        await InCollectionAsync(async db =>
        {
            db.DepositAddresses.Add(new ColDepositAddress(Guid.NewGuid())
            {
                NetworkId = networkId,
                UserId = userId,
                Address = "0x" + new string('6', 40),
                AddressIndex = HdKeyService.FirstUserAddressIndex,
                CreationTime = DateTime.UtcNow,
            });
            await Task.CompletedTask;
        });

        await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<CollectionNetworkAdminAppService>().GenerateMnemonicAsync(networkId));
        await InCollectionAsync(async db =>
        {
            (await db.Networks.SingleAsync(network => network.Id == networkId)).MnemonicCipher.ShouldBeNull();
            (await db.DepositAddresses.CountAsync(address => address.NetworkId == networkId)).ShouldBe(1);
        });
    }

    private async Task SeedUsersAsync(Guid[] userIds)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<AbpAdminDbContext>>().GetDbContextAsync();
        db.Users.AddRange(userIds.Select(id => new IdentityUser(id, "legacy-" + id.ToString("N"), id + "@example.test")));
        await uow.CompleteAsync();
    }

    private async Task InCollectionAsync(Func<CollectionDbContext, Task> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        await action(db);
        await uow.CompleteAsync();
    }
}
