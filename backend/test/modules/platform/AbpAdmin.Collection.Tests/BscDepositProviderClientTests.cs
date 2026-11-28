using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Entities;
using HttpAgent;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class BscDepositProviderClientTests
{
    private const string Token = "0x1111111111111111111111111111111111111111";
    private const string Recipient = "0x2222222222222222222222222222222222222222";
    private const string Sender = "0x3333333333333333333333333333333333333333";
    private static readonly string Hash = "0x" + new string('a', 64);
    private static readonly string BlockHash = "0x" + new string('b', 64);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Etherscan_Pages_And_Only_Accepts_Matched_Receipt_Logs(bool mismatchedReceipt)
    {
        var calls = new List<string>();
        var watched = new HashSet<string> { Recipient };
        var matched = Log(Hash, Recipient, index: 1001, amount: 42);
        using var handler = new Handler(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            query["chainid"].ToString().ShouldBe("56");
            query["apikey"].ToString().ShouldBe("key");
            var action = query["action"].ToString();
            calls.Add(action);
            switch (action)
            {
                case "eth_blockNumber":
                    return Json(new { result = "0x71" });
                case "getLogs":
                    query["address"].ToString().ShouldBe(Token);
                    query["fromBlock"].ToString().ShouldBe("101");
                    query["toBlock"].ToString().ShouldBe("101");
                    query["topic0"].ToString().ShouldBe(AnkrBscChainGateway.TransferTopic);
                    if (query["page"].ToString() == "1")
                    {
                        var page = Enumerable.Range(0, 1000).Select(i => Log("0x" + i.ToString("x64"), Sender, i, 1));
                        return Json(new { status = "1", message = "OK", result = page });
                    }
                    return Json(new { status = "1", message = "OK", result = new[] { matched } });
                case "eth_getTransactionReceipt":
                    query["txhash"].ToString().ShouldBe(Hash);
                    return Json(new { result = new { transactionHash = Hash, blockHash = BlockHash,
                        blockNumber = "0x65", status = "0x1", logs = new[] { mismatchedReceipt ? Log(Hash, Recipient, 1001, 40) : matched } } });
                default:
                    throw new InvalidOperationException(action);
            }
        });
        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.EtherscanV2, "key", 1000);

        (await source.GetHeadAsync(CancellationToken.None)).ShouldBe(113L);
        if (mismatchedReceipt)
        {
            var error = await Should.ThrowAsync<DepositScanReadException>(() => source.ReadLogsAsync(Token, 101, 101, watched, CancellationToken.None));
            error.Code.ShouldBe(DepositScanFailure.EvidenceInvalid);
        }
        else
        {
            var logs = await source.ReadLogsAsync(Token, 101, 101, watched, CancellationToken.None);
            logs.Single().ShouldBe(new TokenTransferLog(Hash, 1001, Sender, Recipient, "42", 101));
        }
        calls.ShouldBe(new[] { "eth_blockNumber", "getLogs", "getLogs", "eth_getTransactionReceipt" });
    }

    [Theory]
    [InlineData("No records found", true)]
    [InlineData("NOTOK", false)]
    public async Task Etherscan_Only_Treats_Known_Empty_Responses_As_Complete(string message, bool valid)
    {
        using var handler = new Handler(_ => Json(new { status = "0", message, result = Array.Empty<object>() }));
        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.EtherscanV2, "key", 1000);
        var watched = new HashSet<string> { Recipient };
        if (valid)
        {
            (await source.ReadLogsAsync(Token, 101, 101, watched, CancellationToken.None)).ShouldBeEmpty();
        }
        else
        {
            var error = await Should.ThrowAsync<DepositScanReadException>(() =>
                source.ReadLogsAsync(Token, 101, 101, watched, CancellationToken.None));
            error.Code.ShouldBe(ProviderCredentialStatus.Unavailable);
        }
    }

    [Fact]
    public async Task Wide_Etherscan_Window_Is_Split_Before_Receipt_Burst()
    {
        using var handler = new Handler(request =>
        {
            QueryHelpers.ParseQuery(request.RequestUri!.Query)["action"].ToString().ShouldBe("getLogs");
            return Json(new { status = "1", message = "OK",
                result = Enumerable.Range(0, 17).Select(i => Log("0x" + i.ToString("x64"), Recipient, i, 1)) });
        });
        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.EtherscanV2, "key", 1000);

        var exception = await Should.ThrowAsync<DepositScanReadException>(() =>
            source.ReadLogsAsync(Token, 101, 102, new HashSet<string> { Recipient }, CancellationToken.None));

        exception.Code.ShouldBe(DepositScanFailure.RangeIncomplete);
    }

    [Fact]
    public async Task Ankr_Range_Error_On_A_Single_Block_Reissues_A_Recipient_Filter()
    {
        var count = 0;
        using var handler = new Handler(request =>
        {
            count++;
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var topics = body.RootElement.GetProperty("params")[0].GetProperty("topics");
            if (count == 1)
            {
                topics.GetArrayLength().ShouldBe(1);
                return Json(new { error = new { code = -32062, message = "range limit" } });
            }
            topics[2][0].GetString().ShouldBe("0x" + Recipient[2..].PadLeft(64, '0'));
            return Json(new { result = new[] { Log(Hash, Recipient, 0, 99) } });
        });
        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.RpcBsc, "key", 1000);

        var logs = await source.ReadLogsAsync(Token, 101, 101, new HashSet<string> { Recipient }, CancellationToken.None);

        logs.Single().ShouldBe(new TokenTransferLog(Hash, 0, Sender, Recipient, "99", 101));
        count.ShouldBe(2);
    }

    [Fact]
    public async Task Busy_Single_Block_Chunks_Watched_Addresses_By_32_And_Handles_Internal_Server_Error()
    {
        var topic2Sizes = new List<int>();
        var addresses = Enumerable.Range(1, 40)
            .Select(i => "0x" + i.ToString("x40"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var handler = new Handler(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var topics = body.RootElement.GetProperty("params")[0].GetProperty("topics");
            if (topics.GetArrayLength() == 1)
            {
                return Json(new { error = new { code = -32000, message = "internal server error: request too complex" } });
            }

            topic2Sizes.Add(topics[2].GetArrayLength());
            return Json(new { result = Array.Empty<object>() });
        });

        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.RpcBsc, "key", 1000);

        var logs = await source.ReadLogsAsync(Token, 101, 101, addresses, CancellationToken.None);
        logs.ShouldBeEmpty();
        topic2Sizes.ShouldBe(new[] { 32, 8 });
    }

    [Theory]
    [InlineData(101, 105, false, DepositScanFailure.RangeIncomplete)]
    [InlineData(101, 101, false, DepositScanFailure.Unavailable)]
    [InlineData(101, 101, true, DepositScanFailure.Unavailable)]
    public async Task Internal_Server_Error_Maps_To_RangeIncomplete_Only_When_Splittable(
        long from, long to, bool withWatched, string expectedCode)
    {
        using var handler = new Handler(_ =>
            Json(new { error = new { code = -32000, message = "internal server error" } }));
        await using var services = CreateServices(handler);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.RpcBsc, "key", 1000);
        var watched = withWatched ? new HashSet<string> { Recipient } : new HashSet<string>();

        var ex = await Should.ThrowAsync<DepositScanReadException>(() =>
            source.ReadLogsAsync(Token, from, to, watched, CancellationToken.None));
        ex.Code.ShouldBe(expectedCode);
    }

    private static object Log(string hash, string to, int index, int amount) => new
    {
        address = Token,
        transactionHash = hash,
        blockHash = BlockHash,
        blockNumber = "0x65",
        logIndex = "0x" + index.ToString("x"),
        data = "0x" + amount.ToString("x64"),
        topics = new[] { AnkrBscChainGateway.TransferTopic, "0x" + Sender[2..].PadLeft(64, '0'), "0x" + to[2..].PadLeft(64, '0') },
    };

    [Theory]
    [InlineData(ChainApiKeyProvider.RpcBsc)]
    [InlineData(ChainApiKeyProvider.EtherscanV2)]
    public async Task Transport_Failure_Keeps_Provider_Key_Out_Of_Logger(ChainApiKeyProvider provider)
    {
        const string secret = "test-credential-value";
        var logs = new CapturingLogger();
        using var handler = new Handler(request =>
        {
            request.RequestUri!.AbsoluteUri.ShouldContain(secret);
            throw new HttpRequestException("Failed to read " + request.RequestUri);
        });
        await using var services = CreateServices(handler, logs);
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(provider, secret, 1000);

        var exception = await Should.ThrowAsync<DepositScanReadException>(() => source.GetHeadAsync(CancellationToken.None));

        exception.Code.ShouldBe(ProviderCredentialStatus.Unavailable);
        logs.Messages.ShouldContain(message => message.Contains("An error occurred while sending HTTP request"));
        string.Join("\n", logs.Messages).ShouldNotContain(secret);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value)),
    };

    private static ServiceProvider CreateServices(HttpMessageHandler handler, CapturingLogger? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs != null) logging.AddProvider(logs);
        });
        services.AddHttpRemote();
        services.AddHttpClient(BscDepositProviderClient.HttpClientName)
            .RemoveAllLoggers()
            .AddHttpMessageHandler(() => new PrivateChainEndpointHandler())
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    internal static ServiceProvider FakeTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        CreateServices(new Handler(respond));

    private sealed class CapturingLogger : ILoggerProvider, ILogger
    {
        internal List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + exception);
        public void Dispose() { }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
