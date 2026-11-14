using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Claims;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 提现广播闭环用例（M3）：小额自动审 → 广播 → 回执完结的账本闭环、卡单恢复三分支
///（无哈希转人工绝不重播 / 回执失败回 Processing / 回执成功完结）、总闸与熔断拦截、
/// 重试双花门、回执完结幂等。全部走 FakeChainGateway（复用扫链验收的假件）。
/// </summary>
public class WithdrawBroadcastTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Admin = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string HotWallet = "0x19e7e376e7c213b7e7e7e46cc70a5dd086daff2a";
    private const string RotatedWallet = "0x5555555555555555555555555555555555555555";
    private const string HotWalletKey = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string ToAddress = "0x3333333333333333333333333333333333333333";

    /// <summary>10000 USDT（18 位）——高于熔断地板默认 1000。</summary>
    private const string RichRaw = "10000000000000000000000";

    private WithdrawManager Manager => GetRequiredService<WithdrawManager>();
    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();
    private CollectionLedgerPoster Poster => GetRequiredService<CollectionLedgerPoster>();
    private WithdrawGuard Guard => GetRequiredService<WithdrawGuard>();
    private WithdrawBroadcastManager Broadcast => GetRequiredService<WithdrawBroadcastManager>();

    /// <summary>装配热钱包 + 富余额假网关（网络/USDT 行来自种子）。</summary>
    private async Task<FakeChainGateway> ArrangeWalletAsync(decimal balance = 10000m)
    {
        await ConfigureHotWalletAsync();
        var gateway = new FakeChainGateway(1000);
        gateway.TokenBalances[UsdtContract(gateway) + ":" + HotWallet] =
            (balance * 1000000000000000000m).ToString("0");
        return gateway;
    }

    private static string UsdtContract(FakeChainGateway _) =>
        "0x55d398326f99059fF775485246999027B3197955"; // 种子里的 BSC USDT

    private async Task ConfigureHotWalletAsync(string address = HotWallet)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstAsync();
        network.WithdrawVaultAddress = address;
        network.WithdrawVaultKeyCipher = await Guard.ProtectKeyAsync(HotWalletKey);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    private async Task<ColNetwork> GetNetworkAsync()
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.Networks.AsNoTracking().OrderBy(n => n.CreationTime).FirstAsync();
        });
    }

    private async Task<ColWithdrawOrder> FundSubmitApproveAsync(decimal amount = 50m, bool autoApprove = false)
    {
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 1000m, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, ToAddress, amount);
        if (!autoApprove)
        {
            await Manager.ApproveAsync(order.Id, Admin, null);
        }

        return await GetOrderAsync(order.Id);
    }

    [Fact]
    public async Task Full_Round_AutoApproves_Broadcasts_And_Completes_With_Payout_Ledger()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync(50m, autoApprove: true); // ≤100 阈值 → 自动审

        gateway.BeforeSend = async () =>
        {
            var sending = await GetOrderAsync(order.Id);
            sending.Status.ShouldBe(WithdrawStatus.Broadcasting);
            sending.BroadcastSenderAddress.ShouldBe(HotWallet);
            sending.NonceUsed.ShouldBe(gateway.TxCountSeed);
        };

        // 第一轮：自动审 + 广播（回执未出，HasReceipt=false）
        await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        var afterFirst = await GetOrderAsync(order.Id);
        afterFirst.Status.ShouldBe(WithdrawStatus.Broadcasting);
        afterFirst.TxHash.ShouldNotBeNull();
        afterFirst.AuditRemark!.ShouldContain("小额");
        gateway.Broadcasts.Count.ShouldBe(1);

        // 第二轮：回执成功 → 完结 + payout 过账
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 1;
        await Broadcast.RunRoundAsync(network, gateway, 20, 5);

        var done = await GetOrderAsync(order.Id);
        done.Status.ShouldBe(WithdrawStatus.Completed);
        done.CompleteTime.ShouldNotBeNull();

        // 账本：payout 已过（应付清零、链上资产减少净额）
        var ledger = GetRequiredService<AbpAdmin.Ledger.Integration.Contracts.ILedgerIntegrationService>();
        var payable = await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.AccountsPayable,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.Platform, ""), "USDT");
        payable.ShouldBe(0m);
    }

    [Fact]
    public async Task Manual_Approval_Broadcast_And_Replayed_Receipt_Post_One_Payout()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        using var http = new HttpClient();
        var factory = new WithdrawalTestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway);
        var admin = CreateAdmin(factory);
        using var principal = UseAdminPrincipal();

        const decimal gross = 256m;
        const decimal fee = 1.024m;
        const decimal net = 254.976m;
        var order = await FundSubmitApproveAsync(gross, autoApprove: true);
        order.Status.ShouldBe(WithdrawStatus.Pending);
        (await BucketAsync(User1)).ShouldBe((744m, gross));
        (await AccountBalanceAsync(FinanceAccountCodes.UserFrozen, User1)).ShouldBe(gross);
        var initialStatus = await ReadAsync(admin.GetStatusAsync);
        initialStatus.PendingCount.ShouldBe(1);
        var pending = await ReadAsync(() => admin.GetWithdrawalsAsync(0, 10, status: "Pending"));
        pending.Items.ShouldHaveSingleItem().Id.ShouldBe(order.Id);

        await ReadAsync(async () =>
        {
            await admin.ApproveAsync(new WithdrawAuditInput { Id = order.Id, Remark = "人工核对通过" });
            return true;
        });
        factory.RequestedNetworks.ShouldBe(new[] { network.Id });
        var approved = await GetOrderAsync(order.Id);
        approved.Status.ShouldBe(WithdrawStatus.Processing);
        approved.AuditUserId.ShouldBe(Admin);
        approved.AuditRemark.ShouldBe("人工核对通过");
        approved.Amount.ShouldBe(gross);
        approved.Fee.ShouldBe(fee);
        approved.ActualAmount.ShouldBe(net);
        (await BucketAsync(User1)).ShouldBe((744m, 0m));
        (await AccountBalanceAsync(FinanceAccountCodes.UserFrozen, User1)).ShouldBe(0m);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(net);
        (await AccountBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(fee);
        var approvedStatus = await ReadAsync(admin.GetStatusAsync);
        approvedStatus.PendingCount.ShouldBe(0);
        approvedStatus.ProcessingCount.ShouldBe(1);

        await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        var sent = await GetOrderAsync(order.Id);
        sent.Status.ShouldBe(WithdrawStatus.Broadcasting);
        sent.TxHash.ShouldNotBeNullOrWhiteSpace();
        gateway.Broadcasts.ShouldHaveSingleItem().To.ShouldBe(UsdtContract(gateway));
        (await ReadAsync(admin.GetStatusAsync)).BroadcastingCount.ShouldBe(1);

        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 1;
        await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        await Broadcast.CompleteFromReceiptAsync(order.Id, sent.TxHash!);
        await Broadcast.RunRoundAsync(network, gateway, 20, 5);

        var finalStatus = await ReadAsync(admin.GetStatusAsync);
        finalStatus.PendingCount.ShouldBe(0);
        finalStatus.ProcessingCount.ShouldBe(0);
        finalStatus.BroadcastingCount.ShouldBe(0);
        finalStatus.BroadcastFailedCount.ShouldBe(0);
        var completed = await ReadAsync(() => admin.GetWithdrawalsAsync(0, 1, withdrawalId: order.Id));
        var row = completed.Items.ShouldHaveSingleItem();
        row.Status.ShouldBe(nameof(WithdrawStatus.Completed));
        row.TxHash.ShouldBe(sent.TxHash);
        row.CompleteTime.ShouldNotBeNull();
        gateway.Broadcasts.Count.ShouldBe(1);

        (await AccountBalanceAsync(FinanceAccountCodes.UserAvailable, User1)).ShouldBe(744m);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
        (await AccountBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(fee);
        (await AccountBalanceAsync(FinanceAccountCodes.OnChainAsset)).ShouldBe(1000m - net);
        await ReadAsync(async () =>
        {
            var ledger = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            var vouchers = await ledger.Vouchers.AsNoTracking()
                .Where(item => item.BizType == FinanceBizTypes.Withdraw && item.BizRefId == order.Id.ToString())
                .ToListAsync();
            vouchers.Count.ShouldBe(4);
            vouchers.ShouldAllBe(item => item.Currency == "USDT" && item.UserId == User1);
            vouchers.Single(item => item.Phase == FinanceBizTypes.Phases.Freeze).Amount.ShouldBe(gross);
            vouchers.Single(item => item.Phase == FinanceBizTypes.Phases.Approve).Amount.ShouldBe(net);
            vouchers.Single(item => item.Phase == FinanceBizTypes.Phases.FeeTake).Amount.ShouldBe(fee);
            var payout = vouchers.Single(item => item.Phase == FinanceBizTypes.Phases.Payout);
            payout.Amount.ShouldBe(net);
            payout.Summary.ShouldNotBeNull();
            payout.Summary.ShouldContain(sent.TxHash!);
            var entries = await ledger.Entries.AsNoTracking().Where(item => item.VoucherId == payout.Id).ToListAsync();
            entries.Count.ShouldBe(2);
            entries.Sum(item => item.Debit).ShouldBe(net);
            entries.Sum(item => item.Credit).ShouldBe(net);
            return true;
        });
    }

    private Task<decimal> AccountBalanceAsync(string code, Guid? userId = null) =>
        GetRequiredService<ILedgerIntegrationService>().GetBalanceAsync(
            userId.HasValue
                ? new AccountRef(code, FinOwnerType.User, userId.Value.ToString())
                : new AccountRef(code, FinOwnerType.Platform, ""), "USDT");

    private sealed class WithdrawalTestGatewayFactory(
        HttpClient http, IStringEncryptionService encryptor, FakeChainGateway gateway)
        : ChainGatewayFactory(http, encryptor, "", NullLoggerFactory.Instance)
    {
        public List<Guid> RequestedNetworks { get; } = new();

        public override Task<IBscChainGateway> CreateAsync(CollectionDbContext db, ColNetwork network, int cap)
        {
            RequestedNetworks.Add(network.Id);
            return Task.FromResult<IBscChainGateway>(gateway);
        }
    }

    private CollectionWithdrawAdminAppService CreateAdmin(ChainGatewayFactory factory) =>
        new(Manager, Poster, Guard, Broadcast, factory,
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(), GetRequiredService<ISettingProvider>(),
            GetRequiredService<ISettingManager>(), GetRequiredService<IAbpDistributedLock>())
        {
            LazyServiceProvider = GetRequiredService<CollectionWithdrawAdminAppService>().LazyServiceProvider,
        };

    private IDisposable UseAdminPrincipal() =>
        GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(AbpClaimTypes.UserId, Admin.ToString()) }, "Testing")));

    [Fact]
    public async Task NoHash_Timeout_With_Lower_Pending_Nonce_Does_Not_Requeue()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 单个节点未见该 nonce，仍无法证明原签名交易不会重新出现。
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        gateway.TxCountSeed = 5;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Broadcasting);
        after.RetryCount.ShouldBe(0);
        after.NonceUsed.ShouldBe(7);
        gateway.Broadcasts.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoHash_Timeout_With_Lower_Pending_Nonce_Even_At_Retry_Limit_Does_Not_Refund()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 即使已用尽重试次数，缺少确定链上证据也不能退款。
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60,
            retryCount: 3, nonceUsed: 7);
        gateway.TxCountSeed = 5;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Broadcasting);
        after.NonceUsed.ShouldBe(7);
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
    }

    [Fact]
    public async Task NoHash_And_Unavailable_Explorer_Moves_Overdue_Withdrawal_To_Manual_Review()
    {
        await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 95, nonceUsed: 7);
        using var http = new System.Net.Http.HttpClient(new StubChainHeadHandler());
        var gateway = new AnkrBscChainGateway(http, ["https://rpc.example.test"], [], 100, 0, 0,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AnkrBscChainGateway>.Instance);

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);

        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        after.NonceUsed.ShouldBe(7);
        after.TxHash.ShouldBeNull();
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
    }

    private sealed class StubChainHeadHandler : System.Net.Http.HttpMessageHandler
    {
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var command = System.Text.Json.JsonDocument.Parse(body);
            command.RootElement.GetProperty("method").GetString().ShouldBe("eth_blockNumber");
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("""{"result":"0xaf00"}"""),
            };
        }
    }

    [Fact]
    public async Task NoHash_Timeout_OccupiedNonce_AdoptsTxHash_FromAddressScan_And_Completes()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // pending nonce(8) > NonceUsed(7)：交易在途/已挖——从热钱包出站转账扫回哈希认领
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        gateway.TxCountSeed = 8;
        gateway.Logs[UsdtContract(gateway)] = new System.Collections.Generic.List<AbpAdmin.Collection.Chain.TokenTransferLog>
        {
            new("0xabababababababababababababababababababababababababababababababab", 0,
                HotWallet, ToAddress, (order.ActualAmount * 1000000000000000000m).ToString("0"), 999),
        };
        gateway.TxNonces["0xabababababababababababababababababababababababababababababababab"] = 7; // 认领 nonce 复核（同 nonce 才可认领）
        gateway.TxSenders["0xabababababababababababababababababababababababababababababababab"] = HotWallet;

        // 第一轮：认领哈希（Broadcasting 带哈希，回执未出）
        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        (await GetOrderAsync(order.Id)).TxHash.ShouldBe("0xabababababababababababababababababababababababababababababababab");

        // 第二轮：回执成功 → 自动完结 + payout 过账
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 1;
        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Completed);
    }

    [Fact]
    public async Task Receipt_Failed_RetryExhausted_AutoRefunds_Without_Manual()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 带哈希超时 + 回执失败（revert = 可证未出款）+ 重试用尽 → 自动退款
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, "0xdeadbeef", minutesAgo: 60, retryCount: 3);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 0;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Rejected);
        after.FailReason!.ShouldContain("自动退款");
        (await BucketAsync(order.UserId)).balance.ShouldBe(1000m);
    }

    [Fact]
    public async Task Stuck_Broadcasting_With_Failed_Receipt_Retries_To_Processing()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, "0xdeadbeef", minutesAgo: 60);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 0; // 链上 revert

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Processing);
        after.TxHash.ShouldBeNull();
        after.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task Halt_Blocks_AutoApprove_And_Broadcast()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync(50m, autoApprove: true);

        await GetRequiredService<ISettingManager>()
            .SetGlobalAsync(CollectionSettings.Withdraw.Halt, "true");
        try
        {
            await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        }
        finally
        {
            await GetRequiredService<ISettingManager>()
                .SetGlobalAsync(CollectionSettings.Withdraw.Halt, "false");
        }

        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Pending); // 总闸：自动审停
    }

    [Fact]
    public async Task Breaker_Floor_Blocks_When_Hot_Wallet_Drained()
    {
        // 热钱包只有 500 USDT（< 地板 1000）：熔断，自动审/广播都停
        var gateway = await ArrangeWalletAsync(500m);
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync(50m, autoApprove: true);

        await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Pending);
        gateway.Broadcasts.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Retry_Does_Not_Treat_A_Recent_Payout_To_Same_Address_As_Nonce_Evidence()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 无哈希 BroadcastFailed + 近 24h 内已有同地址 Completed 付款
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        await CompleteAnotherToSameAddressAsync();

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);
    }

    [Fact]
    public async Task Retry_With_Hash_Pending_Receipt_Refuses_And_Success_Completes()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, "0xfeed", minutesAgo: 0);

        // 回执未定：拒绝重试（防双花：链上结果未知）
        gateway.HasReceipt = false;
        var pending = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        pending.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);

        // 回执成功：重试路径直接完结 + payout 过账
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 1;
        await Broadcast.RetryAsync(order.Id, network, gateway);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Completed);
    }

    [Fact]
    public async Task Retry_Limit_Reached_Refuses()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, retryCount: 3, nonceUsed: 7);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawRetryLimitReached);
    }

    [Fact]
    public async Task Retry_Completes_Successful_Receipt_At_Retry_Limit_Without_Rebroadcast()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, "0xfeed", minutesAgo: 0,
            retryCount: WithdrawDefaults.MaxRetries, nonceUsed: 7);
        gateway.HasReceipt = true;

        await Broadcast.RetryAsync(order.Id, network, gateway);

        var completed = await GetOrderAsync(order.Id);
        completed.Status.ShouldBe(WithdrawStatus.Completed);
        completed.RetryCount.ShouldBe(WithdrawDefaults.MaxRetries);
        gateway.Broadcasts.ShouldBeEmpty();
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
    }

    [Fact]
    public async Task Manual_Refund_Rejects_Receipt_From_Previous_Broadcast_Attempt()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, "0xfeed", minutesAgo: 0, nonceUsed: 7);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 0;
        using var http = new HttpClient();
        var admin = CreateAdmin(new WithdrawalTestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway));
        using var principal = UseAdminPrincipal();
        gateway.BeforeReceiptRead = async hash =>
        {
            hash.ShouldBe("0xfeed");
            await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, "0xbeef", minutesAgo: 0,
                retryCount: 1, nonceUsed: 8);
        };

        var exception = await Should.ThrowAsync<BusinessException>(() => ReadAsync(async () =>
        {
            await admin.RefundAsync(new WithdrawAuditInput { Id = order.Id, Remark = "旧回执" });
            return true;
        }));

        exception.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidStatus);
        var current = await GetOrderAsync(order.Id);
        current.Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        current.TxHash.ShouldBe("0xbeef");
        (await BucketAsync(User1)).ShouldBe((950m, 0m));
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(order.ActualAmount);
        (await AccountBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(order.Fee);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Stale_Manual_Retry_Does_Not_Change_A_Newer_Attempt_With_Same_Hash_And_Nonce(int receiptStatus)
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, "0xfeed", minutesAgo: 0, nonceUsed: 7);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = receiptStatus;
        gateway.BeforeReceiptRead = _ => ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed,
            "0xfeed", minutesAgo: 0, retryCount: 1, nonceUsed: 7);

        var exception = await Should.ThrowAsync<BusinessException>(() => Broadcast.RetryAsync(order.Id, network, gateway));

        exception.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidStatus);
        var current = await GetOrderAsync(order.Id);
        current.Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        current.TxHash.ShouldBe("0xfeed");
        current.NonceUsed.ShouldBe(7);
        current.RetryCount.ShouldBe(1);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(order.ActualAmount);
    }

    [Fact]
    public async Task Auto_Refund_Leaves_A_Newer_Broadcast_Attempt_Untouched()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, "0xfeed", minutesAgo: 60,
            retryCount: WithdrawDefaults.MaxRetries - 1, nonceUsed: 7);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 0;
        gateway.BeforeReceiptRead = _ => ForceStateAsync(order.Id, WithdrawStatus.Broadcasting,
            "0xbeef", minutesAgo: 0, retryCount: WithdrawDefaults.MaxRetries, nonceUsed: 8);

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);

        var current = await GetOrderAsync(order.Id);
        current.Status.ShouldBe(WithdrawStatus.Broadcasting);
        current.TxHash.ShouldBe("0xbeef");
        (await BucketAsync(User1)).ShouldBe((950m, 0m));
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(order.ActualAmount);
    }

    [Fact]
    public async Task Retry_NoHash_Nonce_Overrun_Refuses_And_Keeps_Nonce_Evidence()
    {
        // 回归锁：BroadcastFailed 无哈希 + NonceUsed=7 + 链上 pending=8 —— 原交易可能
        // 已进 mempool/已挖。修复前：重试直接 RequeueToProcessingAsync 清掉 NonceUsed 证据，
        // 下一轮以新 nonce 二次转账 ActualAmount = 双付；RefundAsync 对同一单状态有仲裁而
        // RetryCoreAsync 没有。修复后与 RefundAsync 同裁决：pending > NonceUsed ⇒ 拒绝，
        // 且 NonceUsed 证据保留（RequeueToProcessingAsync 不执行）。
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        gateway.TxCountSeed = 8; // pending nonce 已越过 NonceUsed

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);

        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        after.NonceUsed.ShouldBe(7, "nonce 证据不得在仲裁前被回队清除");
    }

    [Fact]
    public async Task Retry_NoHash_Requires_A_Different_Confirmed_Transaction_At_The_Same_Sender_Nonce()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        gateway.TxCountSeed = 7;
        var ambiguous = await Should.ThrowAsync<BusinessException>(() => Broadcast.RetryAsync(order.Id, network, gateway));
        ambiguous.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);
        (await GetOrderAsync(order.Id)).NonceUsed.ShouldBe(7);

        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true);
        await Broadcast.RetryAsync(order.Id, network, gateway);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Processing);
    }

    [Fact]
    public async Task Adoption_Rejects_Transfer_With_Mismatched_Nonce()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 窗口内有一笔 (from,to,amount) 全匹配的转账，但它的链上 nonce ≠ 本单 NonceUsed
        //（同地址同金额的历史付款——认领唯一键是 nonce，不匹配绝不认领）
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        gateway.TxCountSeed = 8;
        var hash = "0x" + new string('c', 62) + "dd";
        gateway.Logs[UsdtContract(gateway)] = new System.Collections.Generic.List<AbpAdmin.Collection.Chain.TokenTransferLog>
        {
            new(hash, 0, HotWallet, ToAddress, (order.ActualAmount * 1000000000000000000m).ToString("0"), 999),
        };
        gateway.TxNonces[hash] = 3; // 历史交易的 nonce=3 ≠ 本单 7
        gateway.TxSenders[hash] = HotWallet;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.TxHash.ShouldBeNull(); // 未认领
        after.Status.ShouldBe(WithdrawStatus.Broadcasting);
    }

    [Fact]
    public async Task OccupiedNonce_WithNoMatch_DoesNotRefund()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // pending(8) > NonceUsed(7) 但窗口空：可能仍在 mempool / 索引滞后——退款会双付，
        // 必须保持 Broadcasting 等下一轮（双花守卫的行为锁）
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        gateway.TxCountSeed = 8;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Broadcasting);
        after.TxHash.ShouldBeNull();
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m); // 未退款（提交 50 审批出账后可用 950）
    }

    [Fact]
    public async Task Nonce_Occupied_By_Later_Order_With_Hash_Provably_Unsent_AutoRefunds()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync(50m, autoApprove: true);

        // 后继单已用同一 nonce 落哈希（同账户同 nonce 唯一 ⇒ 本单交易必未上链）→ 自动退款
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, retryCount: 3, nonceUsed: 7);
        gateway.TxCountSeed = 8;
        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true); // 证据收紧：后继必须已 Completed（广播≠上链）

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Rejected);
        after.FailReason!.ShouldContain("nonce 7");
        (await BucketAsync(order.UserId)).balance.ShouldBe(1000m);
    }

    [Fact]
    public async Task CompleteFromReceipt_Is_Idempotent_For_Same_Hash_And_Rejects_Wrong_State()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, "0xdeadbeef", minutesAgo: 0);
        gateway.HasReceipt = true;
        gateway.ReceiptStatusOverride = 1;

        await Broadcast.CompleteFromReceiptAsync(order.Id, "0xdeadbeef");
        // 同哈希重放：幂等
        await Broadcast.CompleteFromReceiptAsync(order.Id, "0xdeadbeef");
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Completed);

        // 状态不符（新单 Pending + 无关哈希）：必须报错而不是静默成功
        var other = await FundSubmitApproveAsync(50m, autoApprove: false);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.CompleteFromReceiptAsync(other.Id, "0xdeadbeef"));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidStatus);
    }

    [Fact]
    public async Task Known_Receipt_Cannot_Complete_Two_Withdrawals()
    {
        var first = await FundSubmitApproveAsync();
        var other = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, ToAddress, 50m);
        await Manager.ApproveAsync(other.Id, Admin, null);
        const string hash = "0xdeadbeef";
        await ForceStateAsync(first.Id, WithdrawStatus.Broadcasting, hash, minutesAgo: 0, nonceUsed: 7);
        await Broadcast.CompleteFromReceiptAsync(first.Id, hash, await GetOrderAsync(first.Id));
        await ForceStateAsync(other.Id, WithdrawStatus.Broadcasting, hash, minutesAgo: 0, nonceUsed: 8);
        var verified = await GetOrderAsync(other.Id);

        var duplicate = await Should.ThrowAsync<BusinessException>(() =>
            Broadcast.CompleteFromReceiptAsync(other.Id, hash, verified));
        duplicate.Code.ShouldBe(CollectionErrorCodes.WithdrawReceiptMismatch);
        (await GetOrderAsync(first.Id)).Status.ShouldBe(WithdrawStatus.Completed);
        (await GetOrderAsync(other.Id)).Status.ShouldBe(WithdrawStatus.Broadcasting);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(other.ActualAmount);
    }

    [Fact]
    public async Task Nonce_Evidence_Requires_Completed_Not_Mere_TxHash()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync(50m, autoApprove: true);

        // 后继单有 TxHash 但仍 Broadcasting（广播≠上链）：不构成「本单必未出款」的证据——
        // 该窗口里本单同 nonce 可能已挖。必须保持 Broadcasting 不退款（防护侧锁：回退到
        // TxHash!=null 旧谓词时此测试会红，双付窗口即复活）
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, retryCount: 3, nonceUsed: 7);
        gateway.TxCountSeed = 8;
        await SeedLaterOrderWithHashAsync(nonce: 7, completed: false); // 仅广播未完结

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Broadcasting); // 不退款、不重播
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
    }

    [Fact]
    public async Task Rotating_Hot_Wallet_Adopts_Transfer_From_Original_Sender_At_Original_Nonce()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        await ConfigureHotWalletAsync(RotatedWallet);
        var network = await GetNetworkAsync();
        var oldHash = "0x" + new string('a', 64);
        var newHash = "0x" + new string('b', 64);
        var rawAmount = (order.ActualAmount * 1000000000000000000m).ToString("0");
        gateway.Logs[UsdtContract(gateway)] =
        [
            new(newHash, 0, RotatedWallet, ToAddress, rawAmount, 998),
            new(oldHash, 0, HotWallet, ToAddress, rawAmount, 999),
        ];
        gateway.TxNonces[oldHash] = 7;
        gateway.TxNonces[newHash] = 7;
        gateway.TxSenders[oldHash] = HotWallet;
        gateway.TxSenders[newHash] = RotatedWallet;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        (await GetOrderAsync(order.Id)).TxHash.ShouldBe(oldHash);
        gateway.AddressTransferReads.ShouldBe([HotWallet]);

        gateway.HasReceipt = true;
        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var completed = await GetOrderAsync(order.Id);
        completed.Status.ShouldBe(WithdrawStatus.Completed);
        completed.BroadcastSenderAddress.ShouldBe(HotWallet);
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
        gateway.Broadcasts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RotatedWallet, "BSC")]
    [InlineData(HotWallet, "AnotherNetwork")]
    public async Task Completed_Payment_Requires_Original_Wallet_And_Network(string sender, string networkName)
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60,
            retryCount: WithdrawDefaults.MaxRetries - 1, nonceUsed: 7);
        await ConfigureHotWalletAsync(RotatedWallet);
        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true, sender: sender, networkName: networkName);

        await Broadcast.RecoverStuckBroadcastingAsync(await GetNetworkAsync(), gateway);

        var current = await GetOrderAsync(order.Id);
        current.Status.ShouldBe(WithdrawStatus.Broadcasting);
        current.NonceUsed.ShouldBe(7);
        current.BroadcastSenderAddress.ShouldBe(HotWallet);
        current.TxHash.ShouldBeNull();
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
        gateway.Broadcasts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Retry_Requires_Original_Wallet_Nonce_Evidence_After_Rotation()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        await ConfigureHotWalletAsync(RotatedWallet);
        var network = await GetNetworkAsync();
        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true, sender: RotatedWallet);

        var blocked = await Should.ThrowAsync<BusinessException>(() => Broadcast.RetryAsync(order.Id, network, gateway));
        blocked.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);
        (await GetOrderAsync(order.Id)).BroadcastSenderAddress.ShouldBe(HotWallet);

        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true, sender: HotWallet);
        await Broadcast.RetryAsync(order.Id, network, gateway);
        var requeued = await GetOrderAsync(order.Id);
        requeued.Status.ShouldBe(WithdrawStatus.Processing);
        requeued.BroadcastSenderAddress.ShouldBeNull();
        requeued.NonceUsed.ShouldBeNull();
    }

    [Fact]
    public async Task Refund_Requires_Original_Wallet_Nonce_Evidence_After_Rotation()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        await ConfigureHotWalletAsync(RotatedWallet);
        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true, sender: RotatedWallet);
        using var http = new HttpClient();
        var admin = CreateAdmin(new WithdrawalTestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway));
        using var principal = UseAdminPrincipal();
        var input = new WithdrawAuditInput { Id = order.Id, Remark = "原热钱包出款核对" };

        var blocked = await Should.ThrowAsync<BusinessException>(() => ReadAsync(async () =>
        {
            await admin.RefundAsync(input);
            return true;
        }));
        blocked.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(order.ActualAmount);

        await SeedLaterOrderWithHashAsync(nonce: 7, completed: true, sender: HotWallet);
        await ReadAsync(async () =>
        {
            await admin.RefundAsync(input);
            return true;
        });
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Rejected);
        (await BucketAsync(order.UserId)).balance.ShouldBe(1000m);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
        (await AccountBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(0m);
    }

    [Fact]
    public async Task Legacy_Attempt_With_Stored_Hash_Still_Completes_Without_Sender()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, "0xfeed", minutesAgo: 60,
            nonceUsed: 7, sender: null);
        await ConfigureHotWalletAsync(RotatedWallet);
        gateway.HasReceipt = true;

        await Broadcast.RecoverStuckBroadcastingAsync(await GetNetworkAsync(), gateway);

        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Completed);
        gateway.AddressTransferReads.ShouldBeEmpty();
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
    }

    [Fact]
    public async Task Legacy_Attempt_Without_Sender_And_Hash_Blocks_Refund_And_Retry()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0,
            nonceUsed: 7, sender: null);
        var network = await GetNetworkAsync();
        using var http = new HttpClient();
        var admin = CreateAdmin(new WithdrawalTestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway));
        using var principal = UseAdminPrincipal();

        var retry = await Should.ThrowAsync<BusinessException>(() => Broadcast.RetryAsync(order.Id, network, gateway));
        retry.Code.ShouldBe(CollectionErrorCodes.WithdrawBroadcastSenderUnknown);
        var refund = await Should.ThrowAsync<BusinessException>(() => ReadAsync(async () =>
        {
            await admin.RefundAsync(new WithdrawAuditInput { Id = order.Id, Remark = "旧单无发送方" });
            return true;
        }));
        refund.Code.ShouldBe(CollectionErrorCodes.WithdrawBroadcastSenderUnknown);

        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        (await BucketAsync(order.UserId)).balance.ShouldBe(950m);
        gateway.AddressTransferReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Manual_Completion_Requires_Original_Transaction_Sender_In_Addition_To_Transfer_Log()
    {
        var gateway = await ArrangeWalletAsync();
        var order = await FundSubmitApproveAsync();
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, nonceUsed: 7);
        await ConfigureHotWalletAsync(RotatedWallet);
        var hash = "0x" + new string('c', 64);
        gateway.HasReceipt = true;
        gateway.Logs[UsdtContract(gateway)] =
        [
            new(hash, 0, HotWallet, ToAddress, (order.ActualAmount * 1000000000000000000m).ToString("0"), 495),
        ];
        gateway.TxNonces[hash] = 7;
        gateway.TxSenders[hash] = RotatedWallet;
        using var http = new HttpClient();
        var admin = CreateAdmin(new WithdrawalTestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway));
        using var principal = UseAdminPrincipal();

        var mismatch = await Should.ThrowAsync<BusinessException>(() => ReadAsync(async () =>
        {
            await admin.CompleteAsync(order.Id, hash);
            return true;
        }));
        mismatch.Code.ShouldBe(CollectionErrorCodes.WithdrawReceiptMismatch);
        var unchanged = await GetOrderAsync(order.Id);
        unchanged.Status.ShouldBe(WithdrawStatus.BroadcastFailed);
        unchanged.TxHash.ShouldBeNull();
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(order.ActualAmount);

        gateway.TxSenders[hash] = HotWallet;
        await ReadAsync(async () =>
        {
            await admin.CompleteAsync(order.Id, hash);
            return true;
        });
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Completed);
        (await AccountBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
    }

    [Fact]
    public async Task HotWallet_Status_Breaker_Ors_Across_Tokens()
    {
        await ConfigureHotWalletAsync();
        var gateway = new FakeChainGateway(1000);
        var usdtContract = UsdtContract(gateway);
        // USDT 富（2000 ≥ 1000 地板）、USDC 贫（500 < 1000）→ 任一低于即熔断
        gateway.TokenBalances[usdtContract + ":" + HotWallet] = (2000m * 1000000000000000000m).ToString("0");
        gateway.TokenBalances["0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d:" + HotWallet] = (500m * 1000000000000000000m).ToString("0");

        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin();
        var status = await Guard.GetStatusAsync(gateway);
        await uow.CompleteAsync();
        status.Configured.ShouldBeTrue();
        status.BreakerOpen.ShouldBeTrue();
        status.TokenBalances["USDT"].ShouldBe("2000");
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<ColWithdrawOrder> GetOrderAsync(Guid id)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.WithdrawOrders.AsNoTracking().FirstAsync(o => o.Id == id);
        });
    }

    /// <summary>直接置态（绕过状态机；模拟 worker/链上路径的中间态）。nonce 模拟发送前落库的 NonceUsed。</summary>
    private async Task ForceStateAsync(
        Guid orderId, WithdrawStatus status, string? txHash, int minutesAgo,
        int retryCount = 0, long? nonceUsed = null, string? sender = HotWallet)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var o = await dbContext.WithdrawOrders.FirstAsync(x => x.Id == orderId);
        o.Status = status;
        o.TxHash = txHash;
        o.RetryCount = retryCount;
        o.NonceUsed = nonceUsed;
        o.BroadcastSenderAddress = sender;
        o.StatusChangedAt = DateTime.UtcNow.AddMinutes(-minutesAgo - 1);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>造一笔同 nonce 后继单（证据要求 Completed：必经回执成功，广播中不算）。</summary>
    private async Task SeedLaterOrderWithHashAsync(
        long nonce, bool completed, string sender = HotWallet, string networkName = "BSC")
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        dbContext.WithdrawOrders.Add(new ColWithdrawOrder(Guid.NewGuid())
        {
            UserId = Guid.NewGuid(),
            Currency = CollectionCurrency.Usdt,
            NetworkName = networkName,
            ToAddress = "0x4444444444444444444444444444444444444444",
            Amount = 10m, Fee = 0.04m, ActualAmount = 9.96m, FeeRate = 0.004m,
            Status = completed ? WithdrawStatus.Completed : WithdrawStatus.Broadcasting,
            TxHash = "0x" + (sender == HotWallet ? new string('e', 62) : new string('d', 62)) + "ff",
            NonceUsed = nonce,
            BroadcastSenderAddress = sender,
            StatusChangedAt = DateTime.UtcNow.AddMinutes(-5),
            CreationTime = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>造一笔同地址近期已付款的 Completed 单（双花门的判据）。</summary>
    private async Task CompleteAnotherToSameAddressAsync()
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        dbContext.WithdrawOrders.Add(new ColWithdrawOrder(Guid.NewGuid())
        {
            UserId = Guid.NewGuid(),
            Currency = CollectionCurrency.Usdt,
            NetworkName = "BSC",
            ToAddress = ToAddress,
            Amount = 10m, Fee = 0.04m, ActualAmount = 9.96m, FeeRate = 0.004m,
            Status = WithdrawStatus.Completed,
            CompleteTime = DateTime.UtcNow.AddMinutes(-10),
            StatusChangedAt = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    private async Task<(decimal balance, decimal frozen)> BucketAsync(Guid userId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Currency == CollectionCurrency.Usdt);
            return (row?.Balance ?? 0m, row?.Frozen ?? 0m);
        });
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        if (uowManager.Current != null)
        {
            return await read();
        }

        using var uow = uowManager.Begin();
        var result = await read();
        await uow.CompleteAsync();
        return result;
    }
}
