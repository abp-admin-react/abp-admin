using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class AddressTransferFallbackTests
{
    private const string Contract = "0xc000000000000000000000000000000000000003";
    private const string Watched = "0xa000000000000000000000000000000000000001";
    private const string Sender = "0xb000000000000000000000000000000000000002";
    private const string OtherContract = "0xd000000000000000000000000000000000000004";
    private const string Hash = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string KnownHash = "0x2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public async Task Missing_Indexes_Use_One_Canonical_Receipt_For_All_Logs_In_A_Transaction()
    {
        var explorer = JsonSerializer.Serialize(new
        {
            status = "1",
            result = new[] { ExplorerItem(Hash, null), ExplorerItem(Hash, null) },
        });
        var receipt = JsonSerializer.Serialize(new
        {
            result = new
            {
                transactionHash = Hash,
                blockNumber = "0x65",
                status = "0x1",
                logs = new[] { ReceiptLog(Contract, "0xe"), ReceiptLog(Contract, "0xf"), ReceiptLog(OtherContract, "0x10") },
            },
        });
        var handler = new StubHandler(explorer, receipt);
        using var http = new HttpClient(handler);

        var page = await NewGateway(http).GetTransfersForAddressAsync(Contract, Watched, 101, 101, 1, 2);

        handler.ReceiptRequests.ShouldBe(1);
        page.Incomplete.ShouldBeTrue(); // 分页取决于浏览器原始行数
        page.HasUnresolvedLogs.ShouldBeFalse();
        page.Logs.Select(log => log.LogIndex).ShouldBe(new[] { 14, 15 });
        page.Logs.All(log => log.BlockNumber == 101 && log.To == Watched && log.TxHash == Hash).ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_Receipt_Preserves_Resolved_Log_And_Reports_Incomplete_Evidence()
    {
        var explorer = JsonSerializer.Serialize(new
        {
            status = "1",
            result = new[] { ExplorerItem(Hash, null), ExplorerItem(KnownHash, "0x11") },
        });
        var handler = new StubHandler(explorer, "{\"result\":null}");
        using var http = new HttpClient(handler);

        var page = await NewGateway(http).GetTransfersForAddressAsync(Contract, Watched, 101, 101, 1, 20);

        handler.ReceiptRequests.ShouldBe(1);
        page.Incomplete.ShouldBeFalse();
        page.HasUnresolvedLogs.ShouldBeTrue();
        page.Logs.Single().TxHash.ShouldBe(KnownHash);
        page.Logs.Single().LogIndex.ShouldBe(17);
    }

    [Fact]
    public async Task Explorer_Error_With_An_Empty_Array_Is_Not_A_Confirmed_Empty_Page()
    {
        var handler = new StubHandler("""{"status":"0","message":"NOTOK","result":[]}""", "{}");
        using var http = new HttpClient(handler);

        await Should.ThrowAsync<ChainRpcException>(() =>
            NewGateway(http).GetTransfersForAddressAsync(Contract, Watched, 101, 101, 1, 20));
        handler.ReceiptRequests.ShouldBe(0);
    }

    private static object ExplorerItem(string txHash, string? logIndex) => new
    {
        hash = txHash,
        transactionIndex = "2",
        contractAddress = Contract,
        logIndex,
        from = Sender,
        to = Watched,
        value = "1",
        blockNumber = "101",
    };

    private static object ReceiptLog(string contract, string index) => new
    {
        address = contract,
        logIndex = index,
        topics = new[] { AnkrBscChainGateway.TransferTopic, PadTopic(Sender), PadTopic(Watched) },
        data = "0x" + new string('0', 63) + "1",
    };

    private static string PadTopic(string address) => "0x" + address[2..].PadLeft(64, '0');

    private static AnkrBscChainGateway NewGateway(HttpClient http) => new(
        http, ["https://rpc.example.test/"], ["https://explorer.example.test/?module=account&action=tokentx"],
        100, 0, 0, NullLogger<AnkrBscChainGateway>.Instance);

    private sealed class StubHandler(string explorer, string receipt) : HttpMessageHandler
    {
        public int ReceiptRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                using var rpc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                rpc.RootElement.GetProperty("method").GetString().ShouldBe("eth_getTransactionReceipt");
                rpc.RootElement.GetProperty("params")[0].GetString().ShouldBe(Hash);
                ReceiptRequests++;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post ? receipt : explorer),
            };
        }
    }
}
