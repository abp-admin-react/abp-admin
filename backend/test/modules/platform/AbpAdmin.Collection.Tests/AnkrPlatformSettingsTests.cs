using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using HttpAgent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class AnkrPlatformSettingsTests : CollectionTestBase
{
    protected override void BeforeAddApplication(IServiceCollection services)
    {
        base.BeforeAddApplication(services);
        services.AddSingleton<CredentialHandler>();
        services.PostConfigure<HttpClientFactoryOptions>(ChainProviderProbe.HttpClientName, options =>
            options.HttpMessageHandlerBuilderActions.Add(builder =>
                builder.PrimaryHandler = builder.Services.GetRequiredService<CredentialHandler>()));
    }

    [Fact]
    public async Task Credential_Test_Rejects_Invalid_Input_And_Unsupported_Network_Without_Configuring_Them()
    {
        var bscId = await FindBscNetworkIdAsync();
        var otherNetworkId = await SeedNetworkAsync("TRON");
        var service = GetRequiredService<CollectionNetworkAdminAppService>();

        foreach (var provider in new[] { "RpcBsc", "AnkrPlatform", "EtherscanV2" })
        {
            var invalid = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
            {
                NetworkId = bscId, Provider = provider, Key = "invalid test credential",
            });
            invalid.Passed.ShouldBeFalse();
            invalid.Message.ShouldNotContain("invalid test credential");
        }
        var unsupported = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = otherNetworkId, Provider = "RpcBsc", Key = new string('a', 64),
        });
        unsupported.Passed.ShouldBeFalse();
        unsupported.Message.ShouldContain("BSC");

        (await service.GetApiKeysAsync(bscId)).RpcKeys.ShouldBeEmpty();
        (await service.GetAnkrSettingsAsync(bscId)).PlatformAuthKeyMasked.ShouldBeNull();
        GetRequiredService<IOptions<AbpAuditingOptions>>().Value.IgnoredTypes.ShouldContain(typeof(TestNodeCredentialInput));
    }

    [Fact]
    public async Task Credential_Test_Uses_Selected_Saved_Key_And_Draft_Platform_Group_Without_Writing_Settings()
    {
        var bscId = await FindBscNetworkIdAsync();
        var handler = GetRequiredService<CredentialHandler>();
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        var projectSecret = new string('a', 64);
        var project = await service.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
        {
            NetworkId = bscId, Provider = "RpcBsc", Key = projectSecret,
        });
        var explorer = await service.AddApiKeyFromBodyAsync(new AddNetworkApiKeyInput
        {
            NetworkId = bscId, Provider = "EtherscanV2", Key = new string('e', 34),
        });
        await service.SetApiKeyEnabledAsync(project.Id, false, bscId);

        (await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "RpcBsc", KeyId = explorer.Id,
        })).Passed.ShouldBeFalse();
        handler.LastUri.ShouldBeNull();
        (await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "RpcBsc", KeyId = project.Id, Key = new string('b', 64),
        })).Passed.ShouldBeFalse();
        handler.LastUri.ShouldBeNull();

        var testedProject = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "RpcBsc", KeyId = project.Id,
        });
        testedProject.Passed.ShouldBeTrue();
        testedProject.Message.ShouldNotContain(projectSecret);
        testedProject.Credits.ShouldBeNull();
        testedProject.BalanceUsd.ShouldBeNull();
        handler.LastUri.ShouldBe(new Uri("https://rpc.ankr.com/bsc/" + projectSecret));
        var listed = (await service.GetApiKeysAsync(bscId)).RpcKeys.Single();
        listed.IsEnabled.ShouldBeFalse();
        listed.LastUsedAt.ShouldBeNull();

        const string platformKey = "synthetic-platform-key-1234";
        await service.ConfigureAnkrAsync(new ConfigureAnkrInput
        {
            NetworkId = bscId, PlatformAuthKey = platformKey, Group = "saved+team",
        });
        var savedAccount = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "AnkrPlatform",
        });
        savedAccount.Passed.ShouldBeTrue();
        savedAccount.Credits.ShouldBe("100");
        savedAccount.BalanceUsd.ShouldBe("1.25");
        handler.LastAuthorization.ShouldBe("Bearer " + platformKey);
        handler.LastUri.ShouldNotBeNull().Query.ShouldBe("?group=saved%2Bteam");

        (await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "AnkrPlatform", Group = null,
        })).Passed.ShouldBeTrue();
        handler.LastUri.ShouldNotBeNull().Query.ShouldBeEmpty();
        var draftAccount = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "AnkrPlatform", Key = "platform-draft", Group = "draft+team",
        });
        draftAccount.Passed.ShouldBeTrue();
        draftAccount.Credits.ShouldBe("900719925474099300");
        draftAccount.BalanceUsd.ShouldBe("0.00");
        handler.LastAuthorization.ShouldBe("Bearer platform-draft");
        handler.LastUri.ShouldNotBeNull().Query.ShouldBe("?group=draft%2Bteam");
        handler.RequestCount.ShouldBe(4);

        var rejectedAccount = await service.TestNodeCredentialAsync(new TestNodeCredentialInput
        {
            NetworkId = bscId, Provider = "AnkrPlatform", Key = "platform-draft", Group = "denied-team",
        });
        rejectedAccount.Passed.ShouldBeFalse();
        rejectedAccount.Credits.ShouldBeNull();
        rejectedAccount.BalanceUsd.ShouldBeNull();
        (await service.GetAnkrSettingsAsync(bscId)).ShouldBe(new AnkrSettingsDto("****1234", "saved+team", ChainDefaults.ScanIntervalSeconds));
    }

    [Fact]
    public async Task Web_Settings_Encrypt_Platform_Key_And_Reuse_Scan_Interval()
    {
        var networkId = await FindBscNetworkIdAsync();
        var otherNetworkId = await SeedNetworkAsync("TRON");
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        const string platformKey = "synthetic-platform-key-1234";
        var group = "0x" + new string('3', 40);

        (await service.GetAnkrSettingsAsync()).ShouldBe(new AnkrSettingsDto(null, null, ChainDefaults.ScanIntervalSeconds));
        (await service.GetAnkrBalanceAsync()).ShouldBe(new AnkrBalanceDto("notConfigured", null, null, null));

        var invalid = await Should.ThrowAsync<BusinessException>(() => service.ConfigureAnkrAsync(new ConfigureAnkrInput
        {
            NetworkId = networkId, ScanIntervalSeconds = ChainDefaults.MinIntervalSeconds - 1,
        }));
        invalid.Code.ShouldBe(CollectionErrorCodes.InvalidAnkrSettings);
        var tooLarge = await Should.ThrowAsync<BusinessException>(() => service.ConfigureAnkrAsync(new ConfigureAnkrInput
        {
            NetworkId = networkId, ScanIntervalSeconds = ChainDefaults.MaxScanIntervalSeconds + 1,
        }));
        tooLarge.Code.ShouldBe(CollectionErrorCodes.InvalidAnkrSettings);
        await Should.ThrowAsync<BusinessException>(() => service.GetAnkrSettingsAsync(otherNetworkId));

        await service.ConfigureAnkrAsync(new ConfigureAnkrInput
        {
            NetworkId = networkId, PlatformAuthKey = platformKey, Group = group, ScanIntervalSeconds = 120,
        });

        (await service.GetAnkrSettingsAsync(networkId)).ShouldBe(new AnkrSettingsDto("****1234", group, 120));
        (await service.GetApiKeysAsync(networkId)).RpcKeys.ShouldBeEmpty();
        var settingManager = GetRequiredService<ISettingManager>();
        (await settingManager.GetOrNullGlobalAsync(CollectionSettings.Chain.ScanIntervalSeconds, fallback: false)).ShouldBe("120");

        using (var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var cipher = (await db.Networks.SingleAsync(network => network.Id == networkId)).AnkrPlatformAuthKeyCipher;
            cipher.ShouldNotBeNull().ShouldNotContain(platformKey);
            HdKeyService.UnprotectPlain(cipher, "test-master-key", GetRequiredService<IStringEncryptionService>())
                .ShouldBe(platformKey);
            await read.CompleteAsync();
        }

        await settingManager.SetGlobalAsync(CollectionSettings.Chain.ScanIntervalSeconds, "180");
        await service.ConfigureAnkrAsync(new ConfigureAnkrInput { NetworkId = networkId, PlatformAuthKey = " " });
        (await service.GetAnkrSettingsAsync(networkId)).ShouldBe(new AnkrSettingsDto("****1234", group, 180));
        await service.ConfigureAnkrAsync(new ConfigureAnkrInput { NetworkId = networkId, ClearPlatformAuthKey = true });
        (await service.GetAnkrSettingsAsync(networkId)).ShouldBe(new AnkrSettingsDto(null, group, 180));
        await service.ConfigureAnkrAsync(new ConfigureAnkrInput { NetworkId = networkId, Group = null });
        (await service.GetAnkrSettingsAsync(networkId)).ShouldBe(new AnkrSettingsDto(null, null, 180));
        await settingManager.SetGlobalAsync(CollectionSettings.Chain.ScanIntervalSeconds, int.MaxValue.ToString());
        (await service.GetAnkrSettingsAsync(networkId)).ScanIntervalSeconds.ShouldBe(ChainDefaults.MaxScanIntervalSeconds);
    }

    private async Task<Guid> SeedNetworkAsync(string name)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var id = Guid.NewGuid();
        await db.Networks.AddAsync(new ColNetwork(id) { Name = name, ChainId = 56, CreationTime = DateTime.UtcNow });
        await uow.CompleteAsync();
        return id;
    }

    private async Task<Guid> FindBscNetworkIdAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var id = await db.Networks.Where(network => network.Name == CollectionConsts.BscNetworkName)
            .Select(network => network.Id).SingleAsync();
        await uow.CompleteAsync();
        return id;
    }

    private sealed class CredentialHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            var status = LastUri?.Query == "?group=denied-team" ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
            var response = LastUri?.Host switch
            {
                "rpc.ankr.com" => """{"result":"0x38"}""",
                "mainnet.multirpc.ankr.com" when LastUri.Query == "?group=draft%2Bteam"
                    => """{"balance":"900719925474099300","balance_usd":"0.00"}""",
                "mainnet.multirpc.ankr.com" => """{"balance":"100","balance_usd":"1.25"}""",
                _ => throw new InvalidOperationException("Unexpected connection probe host."),
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response) });
        }
    }
}

public class AnkrPlatformBalanceClientTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("team+name", "?group=team%2Bname")]
    public async Task Balance_Uses_Platform_Bearer_And_Optional_Encoded_Team_Group(string? group, string expectedQuery)
    {
        using var handler = new BalanceHandler(HttpStatusCode.OK, """{"balance":"0","balance_usd":-0.01}""");
        await using var provider = BuildServices(handler);
        var client = new AnkrPlatformBalanceClient(provider.GetRequiredService<IHttpRemoteService>());

        (await client.ReadAsync("platform-key-for-test", group, CancellationToken.None)).ShouldBe(("0", "-0.01"));
        handler.Uri.ShouldBe(new Uri("https://mainnet.multirpc.ankr.com/api/v1/auth/balance" + expectedQuery));
        handler.Authorization.ShouldBe("Bearer platform-key-for-test");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "<html></html>")]
    public async Task Failed_Or_Invalid_Provider_Responses_Are_Not_Zero_Balances(HttpStatusCode status, string body)
    {
        using var handler = new BalanceHandler(status, body);
        await using var provider = BuildServices(handler);
        var client = new AnkrPlatformBalanceClient(provider.GetRequiredService<IHttpRemoteService>());

        (await client.ReadAsync("platform-key-for-test", null, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"balance\":\"0\",\"balance_usd\":\"0\"}", "available")]
    [InlineData(HttpStatusCode.Forbidden, "team denied", "invalid")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "maintenance", "unavailable")]
    public async Task Platform_Probe_Uses_Same_Balance_Call_And_Classifies_Failure(HttpStatusCode status, string body, string expected)
    {
        using var handler = new BalanceHandler(status, body);
        await using var provider = BuildServices(handler);
        var client = new AnkrPlatformBalanceClient(provider.GetRequiredService<IHttpRemoteService>());

        var result = await client.TestAsync("platform-key-for-test", "team+name", CancellationToken.None);
        result.Status.ShouldBe(expected);
        if (expected == ProviderCredentialStatus.Available)
        {
            result.Balance.ShouldBe(("0", "0"));
        }
        else
        {
            result.Balance.ShouldBeNull();
        }
        handler.Method.ShouldBe(HttpMethod.Get);
        handler.Uri.ShouldBe(new Uri("https://mainnet.multirpc.ankr.com/api/v1/auth/balance?group=team%2Bname"));
        handler.Authorization.ShouldBe("Bearer platform-key-for-test");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"result\":\"0x38\"}", "available")]
    [InlineData(HttpStatusCode.OK, "{\"result\":\"0x1\"}", "unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "bad key", "invalid")]
    [InlineData(HttpStatusCode.OK, "{\"error\":{\"code\":-32052}}", "invalid")]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":-32074}}", "unavailable")]
    public async Task Project_Probe_Only_Reads_Bsc_Chain_Id(HttpStatusCode status, string body, string expected)
    {
        using var handler = new BalanceHandler(status, body);
        await using var provider = BuildServices(handler);
        var client = new ChainProviderProbe(provider.GetRequiredService<IHttpRemoteService>());
        var key = new string('a', 64);

        (await client.TestAnkrRpcAsync(key, CancellationToken.None)).ShouldBe(expected);
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.Uri.ShouldBe(new Uri("https://rpc.ankr.com/bsc/" + key));
        handler.Authorization.ShouldBeNull();
        using var json = JsonDocument.Parse(handler.RequestBody!);
        json.RootElement.GetProperty("method").GetString().ShouldBe("eth_chainId");
        json.RootElement.GetProperty("params").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"status\":\"1\",\"message\":\"OK\",\"result\":[]}", "available")]
    [InlineData("{\"status\":\"0\",\"message\":\"No transactions found\",\"result\":[]}", "available")]
    [InlineData("{\"status\":\"1\",\"message\":\"OK\",\"result\":\"0\"}", "unavailable")]
    [InlineData("{\"status\":\"1\",\"message\":\"OK-Missing/Invalid API Key\",\"result\":[]}", "invalid")]
    [InlineData("{\"status\":\"0\",\"message\":\"NOTOK\",\"result\":\"Invalid API Key\"}", "invalid")]
    [InlineData("{\"status\":\"0\",\"message\":\"NOTOK\",\"result\":\"Free API access is not supported for this chain.\"}", "planRequired")]
    [InlineData("{\"status\":\"0\",\"message\":\"NOTOK\",\"result\":\"Too many invalid API key attempts, please try again later\"}", "unavailable")]
    public async Task Etherscan_Probe_Requires_Authenticated_Bsc_Token_Transfer_Response(string body, string expected)
    {
        using var handler = new BalanceHandler(HttpStatusCode.OK, body);
        await using var provider = BuildServices(handler);
        var client = new ChainProviderProbe(provider.GetRequiredService<IHttpRemoteService>());
        var key = new string('b', 34);

        (await client.TestEtherscanAsync(key, CancellationToken.None)).ShouldBe(expected);
        handler.Method.ShouldBe(HttpMethod.Get);
        handler.Uri.ShouldNotBeNull().GetLeftPart(UriPartial.Path).ShouldBe("https://api.etherscan.io/v2/api");
        handler.Uri.Query.ShouldContain("chainid=56&module=account&action=tokentx&");
        handler.Uri.Query.ShouldContain("&startblock=1&endblock=1&page=1&offset=1&sort=asc");
        handler.Uri.Query.ShouldContain("&apikey=" + key);
        handler.Authorization.ShouldBeNull();
    }

    private static ServiceProvider BuildServices(BalanceHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpRemote();
        services.AddHttpClient(ChainProviderProbe.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class BalanceHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
