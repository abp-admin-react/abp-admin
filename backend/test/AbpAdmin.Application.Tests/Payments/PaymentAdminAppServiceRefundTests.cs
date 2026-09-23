using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.PaymentService.Payments;
using EasyAbp.PaymentService.Payments.Dtos;
using EasyAbp.PaymentService.Prepayment.Accounts;
using EasyAbp.PaymentService.Prepayment.PaymentService;
using EasyAbp.PaymentService.Refunds;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Payments;

/* PaymentAdminAppService.RefundAsync（管理端退款，资金路径）的行为锚定。
 * 建数据方式与 PaymentServiceFreePaymentTests 同型：发布 CreatePaymentEto 创建支付单，
 * 区别在于退款用例需要非 0 实付金额——Free 渠道只收 0 元单，且其 OnRefundStartedAsync
 * 直接抛 NotSupportedException，所以走 Prepayment 渠道：直接落一个有余额的 default 组
 * 预付账户（Account 是纯数据行，与 AccountAppService 建户等价），PayAsync 扣款即时完成支付，
 * 退款时 Prepayment 渠道会把款项退回账户并即时完成退款（CompleteRefundAsync），
 * 全程无外部依赖，可在集成测试里确定性断言行项金额流转。
 *
 * 覆盖：
 * 1. 指定金额超过 Σ(ActualPaymentAmount - RefundAmount - PendingRefundAmount) → RefundAmountInvalid，
 *    且不产生任何退款落库（客户端可乱填金额，服务端以库里行项为唯一真相，超额不截断成部分退）
 * 2. RefundAmount=null 全额退款成功后，再退同一单 → RefundAmountInvalid（无剩余可退）
 * 3. abpadmin:refund:{paymentId} 分布式锁被占用 → RefundInProgress；释放后同一单可正常退
 * 4. round4：指定金额的部分退款——按 ItemKey 升序确定性贪心分配（前项退满才动下一项），
 *    部分退后剩余仍可退；指定金额恰好等于 Σ 剩余可退的等值边界应成功（round4 前该分支零锚定）。
 *    期望值由测试按同一规则自推导：行项加载顺序不保证，分配只对 ItemKey 序确定性成立
 */
public abstract class PaymentAdminAppServiceRefundTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    private const decimal PaymentAmount = 100m;

    private readonly IPaymentAdminAppService _paymentAdminAppService;
    private readonly IPaymentAppService _paymentAppService;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IRefundRepository _refundRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDistributedEventBus _distributedEventBus;

    protected PaymentAdminAppServiceRefundTests()
    {
        _paymentAdminAppService = GetRequiredService<IPaymentAdminAppService>();
        _paymentAppService = GetRequiredService<IPaymentAppService>();
        _paymentRepository = GetRequiredService<IPaymentRepository>();
        _refundRepository = GetRequiredService<IRefundRepository>();
        _accountRepository = GetRequiredService<IAccountRepository>();
        _distributedEventBus = GetRequiredService<IDistributedEventBus>();
    }

    /// <summary>
    /// 创建一笔已支付完成的 Prepayment 支付单（行项金额 <paramref name="itemAmounts"/>，至少一项）。
    /// 返回（支付单 Id, 行项 ItemKey 数组——与传入金额同序, 扣款账户 Id）：ItemKey 全局唯一，
    /// 供从共享测试库里精确找回本单；账户 Id 供断言退款后的余额回流。
    /// </summary>
    /// <summary>
    /// 与产品侧同规则的分配 oracle：按 ItemKey 升序贪心，前项退满才动下一项。
    /// 期望值动态推导而非硬编码，使断言对任意 ItemKey 随机序都稳定。
    /// </summary>
    private static List<decimal> SimulateGreedyAllocation(
        IEnumerable<(decimal Amount, string Key)> items, decimal refundAmount)
    {
        var expected = new List<decimal>();
        var remaining = refundAmount;
        foreach (var item in items.OrderBy(x => x.Key))
        {
            var take = Math.Min(remaining, item.Amount);
            expected.Add(take);
            remaining -= take;
            if (remaining <= 0)
            {
                break;
            }
        }

        return expected;
    }

    private async Task<(Guid PaymentId, string[] ItemKeys, Guid AccountId)> CreatePaidPaymentAsync(params decimal[] itemAmounts)
    {
        if (itemAmounts.Length == 0)
        {
            throw new ArgumentException("至少需要一个行项金额", nameof(itemAmounts));
        }

        var totalAmount = itemAmounts.Sum();

        // 预付账户余额 = 支付总额：PayAsync 扣款后余额归零，退款成功后应回到 totalAmount，
        // 余额断言因此可以区分"退款真的执行了"与"只是支付单金额字段被改了"
        var account = new EasyAbp.PaymentService.Prepayment.Accounts.Account(Guid.NewGuid(), null, "default", AdminUserId, totalAmount, 0m);
        await WithUnitOfWorkAsync(() => _accountRepository.InsertAsync(account, autoSave: true));

        var itemKeys = itemAmounts
            .Select(amount => (Amount: amount, ItemKey: Guid.NewGuid().ToString("N")))
            .ToArray();
        await WithUnitOfWorkAsync(() => _distributedEventBus.PublishAsync(new CreatePaymentEto(
            null,
            AdminUserId,
            PrepaymentPaymentServiceProvider.PaymentMethod,
            "CNY",
            itemKeys.Select(x => new CreatePaymentItemEto
            {
                ItemType = "RefundTest",
                ItemKey = x.ItemKey,
                OriginalPaymentAmount = x.Amount
            }).ToList())));

        var created = await FindPaymentByItemKeyAsync(itemKeys[0].ItemKey);
        created.ShouldNotBeNull("CreatePaymentEto 发布后应创建 Prepayment 支付单");

        // AccountId 是 Prepayment 渠道扣款的必备配置（OnPaymentStartedAsync 从中解析扣款账户）
        var payInput = new PayInput();
        payInput.SetProperty("AccountId", account.Id);
        var paid = await _paymentAppService.PayAsync(created!.Id, payInput);

        paid.CompletionTime.ShouldNotBeNull("预付账户余额足够，Prepayment 支付应即时完成");
        paid.ActualPaymentAmount.ShouldBe(totalAmount);

        return (created!.Id, itemKeys.Select(x => x.ItemKey).ToArray(), account.Id);
    }

    private async Task<decimal> GetAccountBalanceAsync(Guid accountId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var account = await _accountRepository.GetAsync(accountId);
            return account.Balance;
        });
    }

    private async Task<Payment?> FindPaymentByItemKeyAsync(string itemKey)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _paymentRepository.WithDetailsAsync();
            return queryable.FirstOrDefault(x => x.PaymentItems.Any(i => i.ItemKey == itemKey));
        });
    }

    private async Task<Refund?> FindRefundByPaymentIdAsync(Guid paymentId)
    {
        // 仓储的 FindByPaymentIdAsync 依赖具体实现的过滤口径；这里用宽口径查询：
        // 关多租户过滤器后按 PaymentId 直查（退款记录与支付单同租户创建，理应可见）
        var dataFilter = GetRequiredService<Volo.Abp.Data.IDataFilter>();
        return await WithUnitOfWorkAsync(async () =>
        {
            using (dataFilter.Disable<Volo.Abp.MultiTenancy.IMultiTenant>())
            {
                var queryable = await _refundRepository.GetQueryableAsync();
                return await _refundRepository.AsyncExecuter.FirstOrDefaultAsync(
                    queryable.Where(x => x.PaymentId == paymentId));
            }
        });
    }

    [Fact]
    public async Task RefundAsync_Should_Reject_Over_Refundable_Amount_Without_Persisting()
    {
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(PaymentAmount);

        // 超过剩余可退（= 行项实付 100）哪怕一分钱：拒绝且不截断成部分退款
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _paymentAdminAppService.RefundAsync(paymentId,
                new RefundPaymentInput { RefundAmount = PaymentAmount + 0.01m }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Payments.RefundAmountInvalid);

        // 无任何落库：校验失败发生在 StartRefundAsync 之前，不产生 Refund 记录，
        // 行项的 RefundAmount / PendingRefundAmount 也保持原样
        (await FindRefundByPaymentIdAsync(paymentId)).ShouldBeNull("超额被拒时不应产生退款记录");

        var reloaded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        reloaded.ShouldNotBeNull();
        reloaded!.RefundAmount.ShouldBe(0m);
        reloaded.PendingRefundAmount.ShouldBe(0m);
        reloaded.PaymentItems.ShouldAllBe(i =>
            i.RefundAmount == 0m && i.PendingRefundAmount == 0m);

        // 扣款账户余额保持支付后的 0：退款渠道从未被触达，钱没有动
        (await GetAccountBalanceAsync(accountId)).ShouldBe(0m);
    }

    [Fact]
    public async Task RefundAsync_Full_Refund_Then_Refund_Again_Should_Be_Rejected()
    {
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(PaymentAmount);

        // RefundAmount = null：全额退款，金额完全由服务端按行项剩余可退计算
        await _paymentAdminAppService.RefundAsync(paymentId,
            new RefundPaymentInput { DisplayReason = "测试全额退款" });

        // Prepayment 渠道退款即时完成：款项退回账户，行项 RefundAmount 落定、无挂起金额
        var refunded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        refunded.ShouldNotBeNull();
        refunded!.RefundAmount.ShouldBe(PaymentAmount);
        refunded.PendingRefundAmount.ShouldBe(0m);
        refunded.PaymentItems.ShouldAllBe(i => i.RefundAmount == PaymentAmount);

        var refund = await FindRefundByPaymentIdAsync(paymentId);
        refund.ShouldNotBeNull();
        refund!.RefundAmount.ShouldBe(PaymentAmount);
        refund.CompletedTime.ShouldNotBeNull("Prepayment 渠道退款应即时完成");

        // 款项真的退回了预付账户（余额 0 → 100），不是只改了支付单金额字段
        (await GetAccountBalanceAsync(accountId)).ShouldBe(PaymentAmount);

        // 无剩余可退（Σ(Actual - Refund - Pending) = 0）→ 再次退款被拒，
        // 且不能又落一条新的退款记录
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _paymentAdminAppService.RefundAsync(paymentId, new RefundPaymentInput()));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Payments.RefundAmountInvalid);
        var refundAfterRejection = await FindRefundByPaymentIdAsync(paymentId);
        refundAfterRejection.ShouldNotBeNull();
        refundAfterRejection!.RefundAmount.ShouldBe(PaymentAmount, "被拒的第二次退款不得叠加退款金额");
    }

    [Fact]
    public async Task RefundAsync_Should_Return_RefundInProgress_While_Lock_Is_Held_Then_Work_After_Release()
    {
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(PaymentAmount);
        var distributedLock = GetRequiredService<IAbpDistributedLock>();

        // 先手动持住产品代码同名的退款锁（等待超时 TimeSpan.Zero 与 RefundAsync 的获取姿势一致），
        // 确定性地复现"已有退款正在处理"（两个管理员同时操作同一单），不依赖真并发的时序运气
        await using (var handle = await distributedLock.TryAcquireAsync(
            $"abpadmin:refund:{paymentId}", TimeSpan.Zero))
        {
            handle.ShouldNotBeNull("测试环境无人竞争，先取锁必须成功");

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _paymentAdminAppService.RefundAsync(paymentId, new RefundPaymentInput()));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Payments.RefundInProgress);
            exception.Data.Contains("PaymentId").ShouldBeTrue("RefundInProgress 应 WithData 携带 PaymentId");

            // 锁被占用时快速失败：不产生退款记录，也不改行项金额
            (await FindRefundByPaymentIdAsync(paymentId)).ShouldBeNull();
        }

        // 锁释放后同一单退款应正常成功（排队的场景退完款，而不是把单子永久卡死）
        await _paymentAdminAppService.RefundAsync(paymentId, new RefundPaymentInput());

        var refunded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        refunded.ShouldNotBeNull();
        refunded!.RefundAmount.ShouldBe(PaymentAmount);
        refunded.PendingRefundAmount.ShouldBe(0m);

        // 款项退回预付账户，资金路径闭环
        (await GetAccountBalanceAsync(accountId)).ShouldBe(PaymentAmount);
    }

    [Fact]
    public async Task RefundAsync_Partial_Amount_Then_Remaining_Should_Both_Succeed()
    {
        // 单行项 100：先指定退 40，再全额退剩余——部分退款分支（round4 前零锚定），
        // 验证指定金额真实落到行项并回流账户，且剩余可退不受首轮影响
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(PaymentAmount);

        await _paymentAdminAppService.RefundAsync(paymentId,
            new RefundPaymentInput { RefundAmount = 40m, DisplayReason = "部分退款 40" });

        var partiallyRefunded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        partiallyRefunded.ShouldNotBeNull();
        partiallyRefunded!.RefundAmount.ShouldBe(40m);
        partiallyRefunded.PendingRefundAmount.ShouldBe(0m);
        partiallyRefunded.PaymentItems.ShouldAllBe(i => i.RefundAmount == 40m);
        (await GetAccountBalanceAsync(accountId)).ShouldBe(40m);

        // 剩余 60 退空：RefundAmount=null 按行项剩余可退计算，累计正好 100
        await _paymentAdminAppService.RefundAsync(paymentId, new RefundPaymentInput());

        var fullyRefunded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        fullyRefunded.ShouldNotBeNull();
        fullyRefunded!.RefundAmount.ShouldBe(PaymentAmount);
        fullyRefunded.PendingRefundAmount.ShouldBe(0m);
        fullyRefunded.PaymentItems.ShouldAllBe(i => i.RefundAmount == PaymentAmount);
        (await GetAccountBalanceAsync(accountId)).ShouldBe(PaymentAmount);
    }

    [Fact]
    public async Task RefundAsync_Two_Item_Payment_Partial_Refund_Should_Fill_Items_In_Order()
    {
        // 两行项 60 + 40，指定退 80：贪心分配应为第一项退满 60、第二项退 20，
        // 不允许任何单项超过自身剩余可退（round4：多行项分配此前从未被测试执行过）
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(60m, 40m);

        await _paymentAdminAppService.RefundAsync(paymentId,
            new RefundPaymentInput { RefundAmount = 80m, DisplayReason = "跨行项部分退款" });

        var payment = await FindPaymentByItemKeyAsync(itemKeys[0]);
        payment.ShouldNotBeNull();
        payment!.RefundAmount.ShouldBe(80m);
        payment.PendingRefundAmount.ShouldBe(0m);
        payment.PaymentItems.Count.ShouldBe(2);
        // 行项加载顺序不保证；分配规则 = ItemKey 升序确定性贪心。
        // 期望值按同一规则在测试侧自推导（真 oracle，不硬编码）：key 与金额的对应关系来自建单入参
        var expectedRefunds = SimulateGreedyAllocation(
            new[] { (Amount: 60m, Key: itemKeys[0]), (Amount: 40m, Key: itemKeys[1]) }, 80m);
        payment.PaymentItems.Select(i => i.RefundAmount).OrderBy(x => x).ToList()
            .ShouldBe(expectedRefunds.OrderBy(x => x).ToList());
        payment.PaymentItems.ShouldAllBe(i => i.RefundAmount <= i.ActualPaymentAmount);
        (await GetAccountBalanceAsync(accountId)).ShouldBe(80m);

        // 退剩余 20 后全部行项退满，无一项超退
        await _paymentAdminAppService.RefundAsync(paymentId, new RefundPaymentInput());

        var drained = await FindPaymentByItemKeyAsync(itemKeys[0]);
        drained.ShouldNotBeNull();
        drained!.RefundAmount.ShouldBe(100m);
        drained.PaymentItems.Select(i => i.RefundAmount).OrderBy(x => x).ToList()
            .ShouldBe(new List<decimal> { 40m, 60m }, "两项分别退满自身可退（60 与 40）");
        (await GetAccountBalanceAsync(accountId)).ShouldBe(100m);
    }

    [Fact]
    public async Task RefundAsync_Specified_Amount_Exactly_Equal_To_Total_Refundable_Should_Succeed()
    {
        // 等值边界：指定金额 == Σ 剩余可退（不是 null、也不是超额）必须成功——
        // 若实现误用严格小于/大于比较，会把"正好退空"错误地拒绝
        var (paymentId, itemKeys, accountId) = await CreatePaidPaymentAsync(PaymentAmount);

        await _paymentAdminAppService.RefundAsync(paymentId,
            new RefundPaymentInput { RefundAmount = PaymentAmount, DisplayReason = "等值全额指定" });

        var refunded = await FindPaymentByItemKeyAsync(itemKeys[0]);
        refunded.ShouldNotBeNull();
        refunded!.RefundAmount.ShouldBe(PaymentAmount);
        refunded.PendingRefundAmount.ShouldBe(0m);
        (await GetAccountBalanceAsync(accountId)).ShouldBe(PaymentAmount);
    }
}
