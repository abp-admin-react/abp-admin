using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
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

    private const string HotWallet = "0x2222222222222222222222222222222222222222";
    private const string HotWalletKey = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string ToAddress = "0x3333333333333333333333333333333333333333";

    /// <summary>10000 USDT（18 位）——高于熔断地板默认 1000。</summary>
    private const string RichRaw = "10000000000000000000000";

    private WithdrawManager Manager => GetRequiredService<WithdrawManager>();
    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();
    private WithdrawLedgerPoster Poster => GetRequiredService<WithdrawLedgerPoster>();
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

    private async Task ConfigureHotWalletAsync()
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstAsync();
        network.WithdrawVaultAddress = HotWallet;
        network.WithdrawVaultKeyCipher = Guard.ProtectKey(HotWalletKey);
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
        await Poster.PostOpeningAsync(User1, "USDT", 1000m);
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

        // 第一轮：自动审 + 广播（回执未出，HasReceipt=false）
        await Broadcast.RunRoundAsync(network, gateway, 20, 5);
        var afterFirst = await GetOrderAsync(order.Id);
        afterFirst.Status.ShouldBe(WithdrawStatus.Broadcasting);
        afterFirst.TxHash.ShouldNotBeNull();
        afterFirst.AuditRemark.ShouldContain("小额");
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
    public async Task NoHash_Timeout_WithUnoccupiedNonce_AutoRequeues_Without_Manual()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 无哈希超时 + 热钱包 pending nonce(5) ≤ NonceUsed(7)：交易从未进 mempool——可证未出款，自动回 Processing
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60, nonceUsed: 7);
        gateway.TxCountSeed = 5;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Processing);
        after.RetryCount.ShouldBe(1);
        after.FailReason.ShouldContain("可证未出款");
    }

    [Fact]
    public async Task NoHash_Timeout_UnoccupiedNonce_RetryExhausted_AutoRefunds()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 可证未出款 + 重试次数用尽 → 自动退款终态（净额+手续费回可用），无人介入
        await ForceStateAsync(order.Id, WithdrawStatus.Broadcasting, txHash: null, minutesAgo: 60,
            retryCount: 3, nonceUsed: 7);
        gateway.TxCountSeed = 5;

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        var after = await GetOrderAsync(order.Id);
        after.Status.ShouldBe(WithdrawStatus.Rejected);
        after.FailReason.ShouldContain("自动退款");

        var balances = GetRequiredService<CollectionBalanceManager>();
        (await BucketAsync(order.UserId)).balance.ShouldBe(1000m); // 提交冻结的 100 全额回可用
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
        after.FailReason.ShouldContain("自动退款");
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
    public async Task Retry_DoubleSpend_Gate_Rejects_Recent_Payout_To_Same_Address()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        var order = await FundSubmitApproveAsync();

        // 无哈希 BroadcastFailed + 近 24h 内已有同地址 Completed 付款
        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0);
        await CompleteAnotherToSameAddressAsync();

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawRecentPayoutExists);
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

        await ForceStateAsync(order.Id, WithdrawStatus.BroadcastFailed, txHash: null, minutesAgo: 0, retryCount: 3);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Broadcast.RetryAsync(order.Id, network, gateway));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawRetryLimitReached);
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
        int retryCount = 0, long? nonceUsed = null)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var o = await dbContext.WithdrawOrders.FirstAsync(x => x.Id == orderId);
        o.Status = status;
        o.TxHash = txHash;
        o.RetryCount = retryCount;
        o.NonceUsed = nonceUsed;
        o.StatusChangedAt = DateTime.UtcNow.AddMinutes(-minutesAgo - 1);
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
