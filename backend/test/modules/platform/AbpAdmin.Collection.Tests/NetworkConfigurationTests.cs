using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Security.Encryption;
using Volo.Abp.SettingManagement;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.Collection;

public class NetworkConfigurationTests : CollectionTestBase
{
    [Fact]
    public async Task Estimated_Arrival_Uses_Bsc_Default_And_Preserves_Existing_Deposit_Eligibility()
    {
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        var customer = GetRequiredService<CollectionDepositAppService>();
        var integration = GetRequiredService<ICollectionIntegrationService>();
        var userId = GetRequiredService<ICurrentUser>().Id!.Value;
        var otherNetworkId = Guid.NewGuid();
        var address = "0x" + new string('5', 40);
        Guid bscId;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            bscId = await db.Networks.Where(network => network.Name == CollectionConsts.BscNetworkName)
                .Select(network => network.Id).SingleAsync();
            db.Networks.Add(new ColNetwork(otherNetworkId)
            {
                Name = "OTHER", ChainId = CollectionConsts.BscMainnetChainId, CreationTime = DateTime.UtcNow,
            });
            db.DepositAddresses.Add(new ColDepositAddress(Guid.NewGuid())
            {
                UserId = userId, NetworkId = bscId, Address = address, AddressIndex = 2, CreationTime = DateTime.UtcNow,
            });
            await uow.CompleteAsync();
        }

        async Task CheckEstimateAsync(int minutes)
        {
            (await service.GetStatusAsync(bscId)).EstimatedArrivalMinutes.ShouldBe(minutes);
            var customerAddress = await customer.GetMyDepositAddressAsync();
            var integrationAddress = await integration.GetDepositAddressAsync(userId);
            customerAddress.EstimatedArrivalMinutes.ShouldBe(minutes);
            integrationAddress.EstimatedArrivalMinutes.ShouldBe(minutes);
            customerAddress.DepositEnabled.ShouldBeFalse();
            integrationAddress.DepositEnabled.ShouldBeFalse();
            customerAddress.Address.ShouldBe(address);
            integrationAddress.Address.ShouldBe(address);
        }

        ConfigureNetworkInput Request(Guid networkId, string arrivalField = "") => JsonSerializer.Deserialize<ConfigureNetworkInput>(
            $$"""{"NetworkId":"{{networkId}}","DepositFeeRate":"0","AutoCreditMaxAmount":"0"{{arrivalField}}}""")!;

        await CheckEstimateAsync(CollectionConsts.DefaultEstimatedArrivalMinutes);
        (await service.GetStatusAsync(otherNetworkId)).EstimatedArrivalMinutes.ShouldBeNull();
        foreach (var value in new[] { 0, CollectionConsts.MaxEstimatedArrivalMinutes + 1 })
        {
            var error = await Should.ThrowAsync<BusinessException>(() =>
                service.ConfigureNetworkAsync(Request(bscId, $",\"EstimatedArrivalMinutes\":{value}")));
            error.Code.ShouldBe(CollectionErrorCodes.InvalidEstimatedArrivalMinutes);
        }
        var unsupported = await Should.ThrowAsync<BusinessException>(() =>
            service.ConfigureNetworkAsync(Request(otherNetworkId, ",\"EstimatedArrivalMinutes\":120")));
        unsupported.Code.ShouldBe(CollectionErrorCodes.InvalidEstimatedArrivalMinutes);

        await service.ConfigureNetworkAsync(Request(bscId, ",\"EstimatedArrivalMinutes\":120"));
        await CheckEstimateAsync(120);
        await service.ConfigureNetworkAsync(Request(bscId));
        await CheckEstimateAsync(120);
        await service.ConfigureNetworkAsync(Request(bscId, ",\"EstimatedArrivalMinutes\":null"));
        await CheckEstimateAsync(CollectionConsts.DefaultEstimatedArrivalMinutes);
    }

    [Fact]
    public async Task Runtime_Web_Settings_Use_Deploy_Defaults_Then_Persist_Validated_Overrides()
    {
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        var before = await service.GetRuntimeSettingsAsync();
        before.ChainRoundBudgetSeconds.ShouldBe(240);
        before.WithdrawRoundBudgetSeconds.ShouldBe(120);
        before.HasEncryptionKey.ShouldBeTrue();
        before.UseLegacyExplorerFallback.ShouldBeTrue();

        var stored = await service.UpdateRuntimeSettingsAsync(new ChainRuntimeSettingsDto
        {
            ChainRoundBudgetSeconds = 300,
            WithdrawRoundBudgetSeconds = 150,
            UseLegacyExplorerFallback = false,
        });
        stored.HasEncryptionKey.ShouldBeTrue();
        // A completion callback from an earlier save must not take precedence over the committed settings.
        var cache = GetRequiredService<IDistributedCache<SettingCacheItem>>();
        var cacheKey = SettingCacheItem.CalculateCacheKey(CollectionSettings.Chain.Runtime, "G", null);
        await cache.SetAsync(cacheKey, new SettingCacheItem(System.Text.Json.JsonSerializer.Serialize(before)));
        var reloaded = await service.GetRuntimeSettingsAsync();
        reloaded.ChainRoundBudgetSeconds.ShouldBe(300);
        reloaded.WithdrawRoundBudgetSeconds.ShouldBe(150);
        reloaded.AlertRecipientUserIds.ShouldBeEmpty();
        reloaded.UseLegacyExplorerFallback.ShouldBeFalse();
        await cache.RemoveAsync(cacheKey);

        var invalid = await Should.ThrowAsync<BusinessException>(() => service.UpdateRuntimeSettingsAsync(
            new ChainRuntimeSettingsDto { ChainRoundBudgetSeconds = 59, WithdrawRoundBudgetSeconds = 150 }));
        invalid.Code.ShouldBe(CollectionErrorCodes.InvalidChainRuntimeSettings);
        var missingUser = await Should.ThrowAsync<BusinessException>(() => service.UpdateRuntimeSettingsAsync(
            new ChainRuntimeSettingsDto
            {
                ChainRoundBudgetSeconds = 310,
                WithdrawRoundBudgetSeconds = 150,
                AlertRecipientUserIds = [Guid.NewGuid()],
            }));
        missingUser.Code.ShouldBe(CollectionErrorCodes.InvalidChainRuntimeSettings);
        (await service.GetRuntimeSettingsAsync()).ChainRoundBudgetSeconds.ShouldBe(300);
    }

    [Fact]
    public async Task Initialize_Encryption_Stores_An_Encrypted_Key_Once_And_Unlocks_Web_Configuration()
    {
        var config = GetRequiredService<IConfiguration>();
        var original = config[CollectionSettings.Host.MasterKey];
        config[CollectionSettings.Host.MasterKey] = null;
        try
        {
            var service = GetRequiredService<CollectionNetworkAdminAppService>();
            var settingManager = GetRequiredService<ISettingManager>();
            (await service.GetRuntimeSettingsAsync()).HasEncryptionKey.ShouldBeFalse();
            await service.InitializeEncryptionAsync();
            var key = await settingManager.GetOrNullGlobalAsync(CollectionSettings.Chain.MasterKey, fallback: false);
            key.ShouldNotBeNull().Length.ShouldBe(64);
            (await service.GetRuntimeSettingsAsync()).HasEncryptionKey.ShouldBeTrue();

            // ABP publishes its settings cache after commit. A stale miss must not regenerate the durable key.
            var cache = GetRequiredService<IDistributedCache<SettingCacheItem>>();
            var cacheKey = SettingCacheItem.CalculateCacheKey(CollectionSettings.Chain.MasterKey, "G", null);
            await cache.SetAsync(cacheKey, new SettingCacheItem());
            (await settingManager.GetOrNullGlobalAsync(CollectionSettings.Chain.MasterKey, fallback: false)).ShouldBeNull();
            await service.InitializeEncryptionAsync();
            (await GetRequiredService<ChainKeyProvider>().GetAsync()).ShouldBe(key);
            await cache.RemoveAsync(cacheKey);

            // Adding a legacy deployment key cannot change the key for the database's existing ciphertext.
            config[CollectionSettings.Host.MasterKey] = "different-legacy-deployment-key";
            (await GetRequiredService<ChainGatewayFactory>().GetMasterKeyAsync()).ShouldBe(key);
            var guard = GetRequiredService<AbpAdmin.Collection.Withdrawals.WithdrawGuard>();
            var encryption = GetRequiredService<IStringEncryptionService>();
            var cipher = await guard.ProtectKeyAsync("qa-wallet-key");
            HdKeyService.UnprotectPlain(cipher, key, encryption).ShouldBe("qa-wallet-key");
            using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            {
                var error = await Should.ThrowAsync<BusinessException>(() => service.GetRuntimeSettingsAsync());
                error.Code.ShouldBe(CollectionErrorCodes.HostOnly);
            }

            using (var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
            {
                var rows = await GetRequiredService<ISettingRepository>().GetListAsync("G", null);
                var row = rows.Single(item => item.Name == CollectionSettings.Chain.MasterKey);
                row.Value.ShouldNotBe(key);
                await read.CompleteAsync();
            }

            var ids = await SeedAsync();
            await service.ConfigureNetworkAsync(new ConfigureNetworkInput
            {
                NetworkId = ids.First,
                DepositFeeRate = "0",
                AutoCreditMaxAmount = "0",
                RpcUrl = "https://rpc.example.test/qa-placeholder",
            });
            (await service.GetStatusAsync(ids.First)).RpcUrlMasked.ShouldBe("https://rpc.example.test/***");
        }
        finally
        {
            config[CollectionSettings.Host.MasterKey] = original;
        }
    }

    private async Task<(Guid First, Guid Second, Guid FirstToken, Guid SecondToken)> SeedAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var first = new ColNetwork(Guid.NewGuid()) { Name = "first-" + Guid.NewGuid().ToString("N")[..16], ChainId = 56, CreationTime = DateTime.UtcNow.AddMinutes(-1) };
        var second = new ColNetwork(Guid.NewGuid()) { Name = "second-" + Guid.NewGuid().ToString("N")[..16], ChainId = 56, CreationTime = DateTime.UtcNow };
        var firstToken = new ColToken(Guid.NewGuid()) { NetworkId = first.Id, Symbol = "USDT", ContractAddress = "0x" + new string('1', 40), MinDeposit = "1", Decimals = 18, CreationTime = DateTime.UtcNow, VaultAddress = "0x" + new string('3', 40) };
        var secondToken = new ColToken(Guid.NewGuid()) { NetworkId = second.Id, Symbol = "USDT", ContractAddress = "0x" + new string('2', 40), MinDeposit = "2", Decimals = 18, CreationTime = DateTime.UtcNow, VaultAddress = "0x" + new string('4', 40) };
        db.Networks.AddRange(first, second);
        db.Tokens.AddRange(firstToken, secondToken);
        await uow.CompleteAsync();
        return (first.Id, second.Id, firstToken.Id, secondToken.Id);
    }

    [Fact]
    public async Task Network_And_Token_Settings_Apply_Only_To_The_Selected_Network()
    {
        var ids = await SeedAsync();
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        await service.ConfigureNetworkAsync(new ConfigureNetworkInput
        {
            NetworkId = ids.Second, DepositFeeRate = "0.02", AutoCreditMaxAmount = "50", NodeLogResultCap = 1000,
            RpcUrl = "https://rpc.example.test/private-key",
        });
        await service.ConfigureTokenAsync(new ConfigureNetworkTokenInput
        {
            NetworkId = ids.Second, TokenId = ids.SecondToken, MinDeposit = "12", IsEnabled = true,
        });

        var first = await service.GetStatusAsync(ids.First);
        var second = await service.GetStatusAsync(ids.Second);
        first.Id.ShouldBe(ids.First);
        first.DepositFeeRate.ShouldBe(0m);
        first.Tokens.Single().MinDeposit.ShouldBe("1");
        second.Id.ShouldBe(ids.Second);
        second.DepositFeeRate.ShouldBe(0.02m);
        second.AutoCreditMaxAmount.ShouldBe("50");
        second.Tokens.Single().MinDeposit.ShouldBe("12");
        second.RpcUrlMasked.ShouldNotBeNull().ShouldNotContain("private-key");
        await Should.ThrowAsync<BusinessException>(() => service.ConfigureTokenAsync(new ConfigureNetworkTokenInput
        {
            NetworkId = ids.First, TokenId = ids.SecondToken, MinDeposit = "99", IsEnabled = false,
        }));
        (await service.GetStatusAsync(ids.Second)).Tokens.Single().MinDeposit.ShouldBe("12");
    }

    [Fact]
    public async Task Legacy_Setters_And_Key_Pool_Honor_Explicit_Network_Ids()
    {
        var ids = await SeedAsync();
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        await service.SetDepositPolicyAsync(0.03m, 100m, ids.Second);
        await service.SetMinDepositAsync("USDT", "7", ids.Second);
        await service.SetVaultAddressAsync("USDT", "0x" + new string('5', 40), ids.Second);
        var key = await service.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
        {
            NetworkId = ids.Second, Provider = "RpcBsc", Key = new string('a', 64), Label = "test",
        });
        (await service.GetApiKeysAsync(ids.First)).RpcKeys.ShouldBeEmpty();
        (await service.GetApiKeysAsync(ids.Second)).RpcKeys.Single().Id.ShouldBe(key.Id);
        await service.RemoveApiKeyAsync(key.Id, ids.First);
        (await service.GetApiKeysAsync(ids.Second)).RpcKeys.Count.ShouldBe(1);
        await service.SetApiKeyEnabledAsync(key.Id, false, ids.Second);
        (await service.GetApiKeysAsync(ids.Second)).RpcKeys.Single().IsEnabled.ShouldBeFalse();
        (await service.GetStatusAsync(ids.First)).DepositFeeRate.ShouldBe(0m);
        (await service.GetStatusAsync(ids.First)).Tokens.Single().MinDeposit.ShouldBe("1");
    }

    [Fact]
    public async Task Wallet_Configuration_Uses_Selected_Network_And_Returns_No_Secrets()
    {
        var ids = await SeedAsync();
        var networkService = GetRequiredService<CollectionNetworkAdminAppService>();
        await networkService.GenerateMnemonicAsync(ids.Second);
        var privateKey = new string('1', 64);
        var address = new Nethereum.Signer.EthECKey(privateKey).GetPublicAddress();
        await GetRequiredService<CollectionWithdrawAdminAppService>().SetWithdrawVaultAsync(new SetWithdrawVaultInput
        {
            NetworkId = ids.First, Address = address, PrivateKey = privateKey,
        });
        var service = GetRequiredService<CollectionWalletAdminAppService>();
        var first = await service.GetAsync(ids.First);
        var second = await service.GetAsync(ids.Second);
        first.HasMnemonic.ShouldBeFalse();
        first.HasWithdrawKey.ShouldBeTrue();
        first.IsWithdrawalNetwork.ShouldBeTrue();
        first.WithdrawAddress.ShouldBe(address.ToLowerInvariant());
        second.HasMnemonic.ShouldBeTrue();
        second.HasWithdrawKey.ShouldBeFalse();
        second.IsWithdrawalNetwork.ShouldBeFalse();
        second.WithdrawalNetworkName.ShouldBe(first.NetworkName);
        second.FuelAddress.ShouldNotBeNullOrWhiteSpace();
        second.Tokens.Single().VaultAddress.ShouldBe("0x" + new string('4', 40));
        second.BalancesAvailable.ShouldBeFalse();
        var serialized = System.Text.Json.JsonSerializer.Serialize(second);
        serialized.ShouldNotContain(privateKey);
        serialized.ShouldNotContain("MnemonicCipher");
        (await service.GetNetworksAsync()).Select(row => row.Id).ShouldContain(ids.Second);
    }

    [Fact]
    public async Task Withdraw_Wallet_Rejects_A_Network_The_Runtime_Does_Not_Use()
    {
        var ids = await SeedAsync();
        var key = new string('1', 64);
        var error = await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<CollectionWithdrawAdminAppService>().SetWithdrawVaultAsync(new SetWithdrawVaultInput
            {
                NetworkId = ids.Second, Address = new Nethereum.Signer.EthECKey(key).GetPublicAddress(), PrivateKey = key,
            }));
        error.Code.ShouldBe(CollectionErrorCodes.WithdrawNetworkUnsupported);
        var wallet = await GetRequiredService<CollectionWalletAdminAppService>().GetAsync(ids.Second);
        wallet.HasWithdrawKey.ShouldBeFalse();
        wallet.WithdrawAddress.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://rpc.example/private-credential")]
    [InlineData("https://127.0.0.1/private-credential")]
    [InlineData("https://[::ffff:192.168.1.1]/private-credential")]
    public async Task Invalid_Rpc_Error_Does_Not_Return_Credentials(string endpoint)
    {
        var ids = await SeedAsync();
        var error = await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<CollectionNetworkAdminAppService>().ConfigureNetworkAsync(new ConfigureNetworkInput { NetworkId = ids.First, RpcUrl = endpoint, DepositFeeRate = "0", AutoCreditMaxAmount = "0" }));
        error.Code.ShouldBe(CollectionErrorCodes.InvalidRpcUrl);
        System.Text.Json.JsonSerializer.Serialize(error.Data).ShouldNotContain("private-credential");
    }

    [Fact]
    public async Task Wallet_Balances_Are_Opt_In_Network_Scoped_And_Failure_Preserves_Configuration()
    {
        var ids = await SeedAsync();
        using var http = new HttpClient();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        var factory = new RecordingWalletGatewayFactory(http, encryptor);
        var source = GetRequiredService<CollectionWalletAdminAppService>();
        var service = new CollectionWalletAdminAppService(
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(), encryptor,
            GetRequiredService<ChainKeyProvider>(), factory) { LazyServiceProvider = source.LazyServiceProvider };
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        var configured = await service.GetAsync(ids.Second);
        factory.Networks.ShouldBeEmpty();
        var vault = configured.Tokens.Single().VaultAddress!;
        factory.Gateway.TokenBalances["0x" + new string('2', 40) + ":" + vault] = "7123000000000000000";
        var balances = await service.GetAsync(ids.Second, includeBalances: true);
        factory.Networks.ShouldBe([ids.Second]);
        balances.BalancesAvailable.ShouldBeTrue();
        balances.Tokens.Single().VaultBalance.ShouldBe("7.123");
        factory.Fail = true;
        var unavailable = await service.GetAsync(ids.Second, includeBalances: true);
        unavailable.BalancesAvailable.ShouldBeFalse();
        unavailable.Tokens.Single().VaultAddress.ShouldBe(vault);
        unavailable.Tokens.Single().VaultBalance.ShouldBeNull();
        await uow.CompleteAsync();
    }

    private sealed class RecordingWalletGatewayFactory(HttpClient http, IStringEncryptionService encryptor)
        : ChainGatewayFactory(http, encryptor, "", NullLoggerFactory.Instance)
    {
        public List<Guid> Networks { get; } = [];
        public FakeChainGateway Gateway { get; } = new(1);
        public bool Fail { get; set; }

        public override Task<IBscChainGateway> CreateAsync(CollectionDbContext db, ColNetwork network, int cap)
        {
            Networks.Add(network.Id);
            if (Fail) throw new InvalidOperationException("Simulated RPC failure");
            return Task.FromResult<IBscChainGateway>(Gateway);
        }
    }

    [Fact]
    public async Task Invalid_Network_Settings_Do_Not_Persist_Partial_Changes()
    {
        var ids = await SeedAsync();
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        await Should.ThrowAsync<BusinessException>(() => service.ConfigureNetworkAsync(new ConfigureNetworkInput
        {
            NetworkId = ids.Second, DepositFeeRate = "0.2", AutoCreditMaxAmount = "50", RpcUrl = "http://unsafe.example.test",
        }));
        (await service.GetStatusAsync(ids.Second)).DepositFeeRate.ShouldBe(0m);
    }

    [Fact]
    public async Task Wallet_Reads_Reject_Tenant_Context()
    {
        using var tenant = GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid());
        var error = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<CollectionWalletAdminAppService>().GetNetworksAsync());
        error.Code.ShouldBe(CollectionErrorCodes.HostOnly);
    }

    [Theory]
    [InlineData("https://user:pass@rpc.example?apiKey=secret", "https://rpc.example/***")]
    [InlineData("https://rpc.example?apiKey=secret#fragment", "https://rpc.example/***")]
    [InlineData("https://user:pass@rpc.example:8443/private-key?q=secret", "https://rpc.example:8443/***")]
    [InlineData("https://user:pass@[::1]:8545/?apiKey=secret", "https://[::1]:8545/***")]
    public async Task Rpc_Mask_Contains_Only_The_Endpoint(string url, string expected)
    {
        var ids = await SeedAsync();
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        // Existing encrypted endpoints remain masked even when the new write policy rejects them.
        var masterKey = await GetRequiredService<ChainKeyProvider>().GetAsync();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var network = await db.Networks.FindAsync(ids.Second);
            network.ShouldNotBeNull().RpcCipher = '\u0001' + encryptor.Encrypt(url, masterKey);
            await uow.CompleteAsync();
        }
        (await service.GetStatusAsync(ids.Second)).RpcUrlMasked.ShouldBe(expected);
    }
}
