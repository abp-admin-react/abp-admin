using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Collection;

public class VaultRpcReconciliationTests : CollectionTestBase
{
    [Fact]
    public async Task Confirmed_transfer_before_local_booking_can_be_the_only_difference_or_coexist_with_a_shortfall()
    {
        var contract = "0x" + new string('c', 40);
        var vault = "0x" + new string('a', 40);
        var sender = "0x" + new string('b', 40);
        var sweepHash = "0x" + new string('1', 64);
        var receipt = new
        {
            transactionHash = sweepHash, blockNumber = "0x63", status = "0x1",
            logs = new[] { new {
                address = contract, logIndex = "0x0", data = "0x" + 20.ToString("x64"),
                topics = new[] { AnkrBscChainGateway.TransferTopic, "0x" + sender[2..].PadLeft(64, '0'),
                    "0x" + vault[2..].PadLeft(64, '0') }
            } }
        };
        // Docker 中的 JSON-RPC 测试服务只在指定合约、金库地址和确认块 100 匹配时返回余额。
        await using var node = new ContainerBuilder("node:22-alpine")
            .WithEnvironment("RECEIPT", JsonSerializer.Serialize(receipt))
            .WithEnvironment("CONTRACT", contract)
            .WithEnvironment("CALLDATA", "0x70a08231" + vault[2..].PadLeft(64, '0'))
            .WithCommand("node", "-e", """
                const receipt = JSON.parse(process.env.RECEIPT);
                require('node:http').createServer(async (req, res) => {
                  let body = '';
                  for await (const chunk of req) body += chunk;
                  const { id, method, params } = JSON.parse(body);
                  let result;
                  if (method === 'eth_blockNumber') result = '0x6e';
                  if (method === 'eth_getTransactionReceipt' && params[0] === receipt.transactionHash) result = receipt;
                  if (method === 'eth_call' && params[1] === '0x64' && params[0].to === process.env.CONTRACT
                      && params[0].data === process.env.CALLDATA) result = '0x' + (120).toString(16).padStart(64, '0');
                  res.setHeader('content-type', 'application/json');
                  res.end(JSON.stringify({jsonrpc:'2.0', id, ...(result === undefined
                      ? {error:{code:-32602,message:'unexpected RPC request'}} : {result})}));
                }).listen(8545, '0.0.0.0');
                """)
            .WithPortBinding(8545, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8545))
            .Build();
        await node.StartAsync();
        using var http = new HttpClient();
        var gateway = new AnkrBscChainGateway(http, [$"http://{node.Hostname}:{node.GetMappedPublicPort(8545)}"],
            [], 100, 0, 0, NullLogger<AnkrBscChainGateway>.Instance);
        await GetRequiredService<ISettingManager>().SetGlobalAsync(CollectionSettings.Chain.Confirmations, "10");
        var network = new ColNetwork(Guid.NewGuid()) { Name = "BSC-RPC-QA", ChainId = 97 };
        var token = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id, Symbol = "USDT", ContractAddress = contract,
            VaultAddress = vault, Decimals = 0, MinDeposit = "1", IsEnabled = true,
        };
        var owner = Guid.NewGuid();
        var address = new ColDepositAddress(Guid.NewGuid())
        {
            NetworkId = network.Id, UserId = owner, Address = sender, AddressIndex = 2,
        };
        var sweep = new ColSweepTask(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = owner, AddressId = address.Id,
            Address = sender, Amount = 20, Status = SweepTaskStatus.Sweeping,
            BroadcastVaultAddress = vault, TxHash = sweepHash,
        };
        var dbProvider = GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await dbProvider.GetDbContextAsync();
            db.AddRange(network, token, address, sweep, new ColDepositOrder(Guid.NewGuid())
            {
                NetworkId = network.Id, TokenId = token.Id, UserId = owner, ToAddress = sender,
                FromAddress = vault, TxHash = "0x" + new string('2', 64), RawAmount = "20", Amount = 20,
                Status = DepositOrderStatus.Sweeping, SweepTaskId = sweep.Id,
            }, new ColBalance(Guid.NewGuid()) { UserId = owner, Currency = CollectionCurrency.Usdt, Balance = 100 });
            await db.SaveChangesAsync();
        });

        var audit = GetRequiredService<VaultAuditManager>();
        await audit.RunAsync(network, token, gateway, CancellationToken.None);
        var timing = Fakes.FakeChainPushNotifier.Instance.VaultChecks[0];
        timing.ReconciliationStatus.ShouldBe("TimingDifference");
        timing.ReferenceBlock.ShouldBe(100);
        timing.Difference.ShouldBe("20");
        timing.PendingCredit.ShouldBe("20");
        timing.ReconciledDifference.ShouldBe("0");

        // 同一笔待记收入不能抵掉另一用户的冻结余额。
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await dbProvider.GetDbContextAsync();
            db.Balances.Add(new ColBalance(Guid.NewGuid()) { UserId = Guid.NewGuid(), Currency = CollectionCurrency.Usdt, Frozen = 7 });
            await db.SaveChangesAsync();
        });
        await audit.RunAsync(network, token, gateway, CancellationToken.None);
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await dbProvider.GetDbContextAsync();
            var shortfall = await db.VaultChecks.SingleAsync(x => x.ReconciliationStatus == VaultReconciliationStatus.Shortfall);
            shortfall.ReferenceBlock.ShouldBe(100);
            shortfall.Difference.ShouldBe(13m);
            shortfall.PendingCredit.ShouldBe(20m);
            shortfall.ReconciledDifference.ShouldBe(-7m);
        });
    }
}
