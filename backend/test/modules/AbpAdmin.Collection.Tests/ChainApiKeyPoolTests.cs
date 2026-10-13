using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 扫链 KEY 池（同服务商多把、限流自动轮换）：
/// 网关层——429/401/403/配额消息切下一把、非 key 类错误不切、全失败原样上抛；
/// 工厂层——池优先/池空回退 RpcCipher 与配置端点、跨轮递增起步；
/// 管理层——形状校验、SHA-256 判重、启停、掩码回显。轮换端点经桩 Handler 的请求 URL 观测。
/// </summary>
public class ChainApiKeyRotationTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly HttpClient _http;

    public ChainApiKeyRotationTests()
    {
        _http = new HttpClient(_handler);
    }

    public void Dispose() => _http.Dispose();

    private static string UrlWithKey(string key) => AnkrBscChainGateway.AnkrBscRpcUrlBase + key;

    private AnkrBscChainGateway NewGateway(string[] rpcKeys, string[] etherscanKeys, int rpcOffset = 0)
    {
        return new AnkrBscChainGateway(
            _http,
            rpcKeys.Select(UrlWithKey).ToArray(),
            etherscanKeys.Select(k => $"https://api.etherscan.io/v2/api?chainid=56&apikey={k}").ToArray(),
            100, rpcOffset, 0,
            NullLogger<AnkrBscChainGateway>.Instance);
    }

    [Fact]
    public async Task Rpc_Rotates_On_429_And_Recovers_With_Next_Key()
    {
        _handler.Responder = (req) =>
        {
            if (req.Contains("keyAAAA"))
            {
                return Json(HttpStatusCode.TooManyRequests, "{}");
            }

            return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x10\"}");
        };
        var gateway = NewGateway(new[] { "keyAAAA", "keyBBBB" }, new[] { "e1" });

        (await gateway.GetLatestBlockNumberAsync()).ShouldBe(16);
        _handler.Requests.Count(r => r.Contains("keyAAAA")).ShouldBe(1);
        _handler.Requests.Count(r => r.Contains("keyBBBB")).ShouldBe(1);
    }

    [Fact]
    public async Task Rpc_Does_Not_Rotate_On_Non_Key_Errors()
    {
        // getLogs 超限是「没读完」不是 key 的问题：只有一把 key 被打，其余 key 不浪费
        _handler.Responder = (req) => Json(HttpStatusCode.OK,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32005,\"message\":\"query returned more than 10000 results\"}}");
        var gateway = NewGateway(new[] { "keyAAAA", "keyBBBB" }, new[] { "e1" });

        var (logs, complete) = await gateway.GetTransferLogsAsync("0xusdt", 1, 2);
        logs.Count.ShouldBe(0);
        complete.ShouldBeFalse();
        _handler.Requests.Count.ShouldBe(1);
        _handler.Requests[0].ShouldContain("keyAAAA");
    }

    [Fact]
    public async Task Rpc_Throws_Through_When_All_Keys_Rate_Limited()
    {
        _handler.Responder = (req) => Json(HttpStatusCode.TooManyRequests, "{}");
        var gateway = NewGateway(new[] { "keyAAAA", "keyBBBB" }, new[] { "e1" });

        await Should.ThrowAsync<HttpRequestException>(async () => await gateway.GetLatestBlockNumberAsync());
        _handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Rpc_Start_Offset_Picks_Different_Key_Per_Round()
    {
        _handler.Responder = (req) => Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x10\"}");
        var first = NewGateway(new[] { "keyAAAA", "keyBBBB" }, new[] { "e1" }, rpcOffset: 0);
        var second = NewGateway(new[] { "keyAAAA", "keyBBBB" }, new[] { "e1" }, rpcOffset: 1);

        await first.GetLatestBlockNumberAsync();
        await second.GetLatestBlockNumberAsync();

        _handler.Requests[0].ShouldContain("keyAAAA");
        _handler.Requests[1].ShouldContain("keyBBBB");
    }

    [Fact]
    public async Task Etherscan_Rotates_On_Quota_Message_In_Result_Field()
    {
        _handler.Responder = (req) =>
        {
            if (req.Contains("apikey=eKEY1"))
            {
                return Json(HttpStatusCode.OK,
                    "{\"status\":\"0\",\"message\":\"NOTOK\",\"result\":\"Max rate limit reached\"}");
            }

            return Json(HttpStatusCode.OK,
                "{\"status\":\"1\",\"message\":\"OK\",\"result\":[]}");
        };
        var gateway = NewGateway(new[] { "r1" }, new[] { "eKEY1", "eKEY2" });

        var page = await gateway.GetTransfersForAddressAsync("0xusdt", "0xabc", 1, 2, 1, 50);
        page.Logs.Count.ShouldBe(0);
        _handler.Requests.Count.ShouldBe(2);
        _handler.Requests[1].ShouldContain("apikey=eKEY2");
    }

    [Fact]
    public async Task Etherscan_No_Transactions_Is_Not_An_Error_And_Does_Not_Rotate()
    {
        _handler.Responder = (req) => Json(HttpStatusCode.OK,
            "{\"status\":\"0\",\"message\":\"No transactions found\",\"result\":[]}");
        var gateway = NewGateway(new[] { "r1" }, new[] { "eKEY1", "eKEY2" });

        var page = await gateway.GetTransfersForAddressAsync("0xusdt", "0xabc", 1, 2, 1, 50);
        page.Logs.Count.ShouldBe(0);
        _handler.Requests.Count.ShouldBe(1);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>记录请求 URL 的桩 Handler（URL 含 key，可观测轮换轨迹）。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public System.Collections.Generic.List<string> Requests { get; } = new();

        public Func<string, HttpResponseMessage> Responder { get; set; } =
            (req) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x0\"}", Encoding.UTF8, "application/json"),
            };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Responder(url);
        }
    }
}

/// <summary>KEY 池的持久化侧：工厂取池/回退、管理 API 形状与判重（真 PG）。</summary>
public class ChainApiKeyPoolTests : CollectionTestBase
{
    private static string NextAnkrKey() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private string Protect(string plain)
    {
        var encryptor = GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>();
        return HdKeyService.Protect(plain, "test-master-key", encryptor);
    }

    private ChainGatewayFactory NewFactory(ChainApiKeyRotationTestsCapture handler)
    {
        return new ChainGatewayFactory(
            new HttpClient(handler),
            GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>(),
            "https://api.etherscan.io/v2/api?chainid=56&apikey=cfgFallbackKey",
            NullLoggerFactory.Instance)
        {
            MasterKey = "test-master-key",
        };
    }

    /// <summary>整段用例（建网/加 key/工厂调用）包在同一工作单元里——工厂要活上下文。</summary>
    private async Task<T> WithUowAsync<T>(Func<CollectionDbContext, Task<T>> body)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var result = await body(dbContext);
        await uow.CompleteAsync();
        return result;
    }

    private async Task<ColNetwork> SeedNetworkAsync(CollectionDbContext db, string? rpcUrl = "https://rpc.ankr.com/bsc/legacykey")
    {
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-KEY-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            RpcCipher = rpcUrl == null ? null : Protect(rpcUrl),
            CreationTime = DateTime.UtcNow,
        };
        await db.Networks.AddAsync(network);
        await db.SaveChangesAsync();
        return network;
    }

    private async Task AddKeyAsync(CollectionDbContext db, ColNetwork network, ChainApiKeyProvider provider, string key)
    {
        var encryptor = GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>();
        await db.ChainApiKeys.AddAsync(new ColChainApiKey(Guid.NewGuid())
        {
            NetworkId = network.Id,
            Provider = provider,
            KeyHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant(),
            KeyCipher = '\u0001' + encryptor.Encrypt(key, "test-master-key"),
            IsEnabled = true,
            CreationTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Factory_Prefers_Key_Pool_And_Rotates_Start_Offset_Per_Create()
    {
        var key1 = NextAnkrKey();
        var key2 = NextAnkrKey();
        var urls = await WithUowAsync(async db =>
        {
            var network = await SeedNetworkAsync(db);
            await AddKeyAsync(db, network, ChainApiKeyProvider.RpcBsc, key1);
            await AddKeyAsync(db, network, ChainApiKeyProvider.RpcBsc, key2);

            var handler = new ChainApiKeyRotationTestsCapture();
            var factory = NewFactory(handler);
            await (await factory.CreateAsync(db, network, 100)).GetLatestBlockNumberAsync();
            await (await factory.CreateAsync(db, network, 100)).GetLatestBlockNumberAsync();
            return handler.Requests.ToList();
        });

        urls.Count.ShouldBe(2);
        urls[0].ShouldEndWith(key1);   // 第一轮从第 0 把起步
        urls[1].ShouldEndWith(key2);   // 第二轮偏移 +1：轮着用不同 key 起步
    }

    [Fact]
    public async Task Factory_Falls_Back_To_Network_RpcCipher_When_Pool_Empty()
    {
        var urls = await WithUowAsync(async db =>
        {
            var network = await SeedNetworkAsync(db);
            var handler = new ChainApiKeyRotationTestsCapture();
            var factory = NewFactory(handler);
            await (await factory.CreateAsync(db, network, 100)).GetLatestBlockNumberAsync();
            return handler.Requests.ToList();
        });

        urls.Count.ShouldBe(1);
        urls[0].ShouldBe("https://rpc.ankr.com/bsc/legacykey");
    }

    [Fact]
    public async Task Factory_Throws_When_Pool_Empty_And_RpcCipher_Empty()
    {
        await WithUowAsync(async db =>
        {
            var network = await SeedNetworkAsync(db, rpcUrl: null);
            var factory = NewFactory(new ChainApiKeyRotationTestsCapture());
            await Should.ThrowAsync<InvalidOperationException>(
                async () => await factory.CreateAsync(db, network, 100));
            return true;
        });
    }

    [Fact]
    public async Task Admin_Add_Validates_Shape_Rejects_Duplicate_Masks_Key()
    {
        var service = GetRequiredService<CollectionNetworkAdminAppService>();

        // 形状：Ankr 必须 64 位十六进制
        await Should.ThrowAsync<BusinessException>(async () =>
            await service.AddApiKeyAsync("RpcBsc", "short-key", null));

        var key = NextAnkrKey();
        var added = await service.AddApiKeyAsync("RpcBsc", key, "ankr-main");
        added.Provider.ShouldBe("RpcBsc");
        added.IsEnabled.ShouldBeTrue();
        added.MaskedKey.ShouldBe("****" + key[^4..]);        // 掩码 = **** + 尾 4 位
        added.MaskedKey.ShouldNotContain(key[..8]);           // 明文前缀不回显

        // 同服务商判重（SHA-256），跨服务商不判
        await Should.ThrowAsync<BusinessException>(async () =>
            await service.AddApiKeyAsync("RpcBsc", key, "dup"));
        var crossProvider = await service.AddApiKeyAsync("EtherscanV2", key[..34], "es-as-eth");
        crossProvider.Provider.ShouldBe("EtherscanV2");

        // Etherscan 形状：34 位字母数字
        await Should.ThrowAsync<BusinessException>(async () =>
            await service.AddApiKeyAsync("EtherscanV2", "ab", null));

        var listed = await service.GetApiKeysAsync();
        listed.RpcKeys.ShouldContain(k => k.Id == added.Id);
        listed.EtherscanKeys.ShouldContain(k => k.Id == crossProvider.Id);
    }

    [Fact]
    public async Task Admin_Toggle_And_Remove_Take_Effect()
    {
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        var added = await service.AddApiKeyAsync("RpcBsc", NextAnkrKey(), "to-toggle");

        await service.SetApiKeyEnabledAsync(added.Id, false);
        var afterToggle = await service.GetApiKeysAsync();
        afterToggle.RpcKeys.First(k => k.Id == added.Id).IsEnabled.ShouldBeFalse();

        await service.RemoveApiKeyAsync(added.Id);
        var afterRemove = await service.GetApiKeysAsync();
        afterRemove.RpcKeys.ShouldNotContain(k => k.Id == added.Id);
    }
}

/// <summary>工厂测试用的桩 Handler（记录 URL，回合法 JSON-RPC 响应）。</summary>
public sealed class ChainApiKeyRotationTestsCapture : HttpMessageHandler
{
    public System.Collections.Generic.List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x10\"}", Encoding.UTF8, "application/json"),
        });
    }
}
