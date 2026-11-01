using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款集成服务实现（计划第 3.1、5.1 节）。订单模块只看接口；本类是收款侧的本地状态机。
/// 事务纪律：本地插入与渠道回写都用 requiresNew 事务并在渠道调用前 CompleteAsync——
/// 应用服务的环境工作单元要到方法返回才提交（与 PaymentAdminAppService 退款写库同一理由），
/// 渠道 HTTP 必须在事务之外，且「另一个 scope 能读到 Pending 行」是验收标准（第 8 节）。
/// </summary>
public class CollectionIntegrationService : Contracts.ICollectionIntegrationService, ITransientDependency
{
    /// <summary>Stripe Checkout 会话的有效期下限（创建后至少 30 分钟）。</summary>
    public static readonly TimeSpan StripeMinimumValidity = TimeSpan.FromMinutes(30);

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IEnumerable<ICollectionChannelGateway> _gateways;
    private readonly ISettingProvider _settingProvider;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly Balances.CollectionBalanceManager _balanceManager;
    private readonly Chain.DepositAddressManager _depositAddressManager;
    private readonly string? _chainMasterKey;
    private readonly IClock _clock;
    private readonly ILogger<CollectionIntegrationService> _logger;

    public CollectionIntegrationService(
        IUnitOfWorkManager unitOfWorkManager,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IEnumerable<ICollectionChannelGateway> gateways,
        ISettingProvider settingProvider,
        IDistributedEventBus distributedEventBus,
        Balances.CollectionBalanceManager balanceManager,
        Chain.DepositAddressManager depositAddressManager,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IClock clock,
        ILogger<CollectionIntegrationService> logger)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _dbContextProvider = dbContextProvider;
        _gateways = gateways;
        _settingProvider = settingProvider;
        _distributedEventBus = distributedEventBus;
        _balanceManager = balanceManager;
        _depositAddressManager = depositAddressManager;
        _chainMasterKey = configuration[CollectionSettings.Host.MasterKey];
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task<Contracts.CollectionDto> CreateAsync(Contracts.CreateCollectionInput input)
    {
        var amount = Contracts.CollectionAmounts.Parse(input.Amount);
        if (amount <= 0)
        {
            throw new BusinessException(Contracts.CollectionErrorCodes.InvalidAmount)
                .WithData("Amount", input.Amount);
        }

        await AssertWithinChannelLimitAsync(input.Channel, input.Currency, amount);

        if (input.Channel == Contracts.CollectionChannel.StripeCheckout
            && input.ExpiresAt - _clock.Now < StripeMinimumValidity)
        {
            // Stripe 会话有效期必须在创建后 30 分钟到 24 小时之间；剩余不足 30 分钟直接拒绝、不插行，
            // 调用方重新报价（计划第 3.1 节）
            throw new BusinessException(Contracts.CollectionErrorCodes.StripeExpiryTooShort)
                .WithData("ExpiresAt", input.ExpiresAt);
        }

        var gateway = ResolveGateway(input.Channel);

        // ── 第一步：本地插入 Pending 行（requiresNew + 提交），ON CONFLICT 只为幂等 ──────────
        // 部分唯一索引 (BusinessType, BusinessId) WHERE Status = 0：冲突目标必须带谓词（第 5.1 节）。
        // CreateAsync 用自己的 requiresNew 事务，不和订单状态绑在一起；语句走当前工作单元连接。
        var collectionId = Guid.NewGuid();
        var collectionNo = NextCollectionNo();
        var now = _clock.Now;

        Contracts.CollectionDto? original = null;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColCollections" ("Id", "CollectionNo", "UserId", "BusinessType", "BusinessId", "Channel", "Currency", "Amount", "Status", "StatusNote", "ChannelResultUnknown", "ExpiresAt", "ChannelTradeNo", "ChannelPayload", "CompletedAt", "CreationTime")
                VALUES ({collectionId}, {collectionNo}, {input.UserId}, {input.BusinessType}, {input.BusinessId}, {(int)input.Channel}, {(int)input.Currency}, {amount}, {(int)Contracts.CollectionStatus.Pending}, NULL, FALSE, {input.ExpiresAt}, NULL, NULL, NULL, {now})
                ON CONFLICT ("BusinessType", "BusinessId") WHERE "Status" = 0 DO NOTHING
                """);

            if (inserted == 0)
            {
                // 同一业务单已有一笔 Pending。在同一事务里按业务键读取赢家，不套部分索引的谓词——
                // 赢家可能已离开谓词覆盖（本路径下不会，但与扣款共用同一纪律，第 5.1 节）。
                var winner = await dbContext.Collections
                    .Where(c => c.BusinessType == input.BusinessType && c.BusinessId == input.BusinessId)
                    .OrderByDescending(c => c.CreationTime)
                    .FirstOrDefaultAsync(c => c.Status == Contracts.CollectionStatus.Pending);

                if (winner == null
                    || winner.Channel != input.Channel
                    || winner.Currency != input.Currency
                    || winner.Amount != amount)
                {
                    // 渠道不同则冲突（调用方先取消）；金额或币种不同则冲突
                    throw new BusinessException(Contracts.CollectionErrorCodes.PendingCollectionConflict)
                        .WithData("BusinessType", input.BusinessType)
                        .WithData("BusinessId", input.BusinessId);
                }

                // 渠道、金额、币种都相同：幂等返回原单；渠道字段为空则用同一收款号重试渠道调用
                original = Map(winner);
            }

            await uow.CompleteAsync();
        }

        if (original != null && !string.IsNullOrEmpty(original.ChannelPayload))
        {
            // 渠道字段已齐：返回原单，不再打渠道（幂等重入）
            return original;
        }

        // ── 第二步：渠道调用（在两个事务之外）────────────────────────────────────────────
        var targetNo = original?.CollectionNo ?? collectionNo;
        var targetId = original?.Id ?? collectionId;
        var request = new ChannelCreateRequest(targetId, targetNo, amount, input.Currency, input.ExpiresAt);
        ChannelCreateResult channelResult;
        try
        {
            channelResult = await gateway.CreateAsync(request);
        }
        catch (Exception ex)
        {
            // 渠道异常归并为「结果未知」：保持 Pending 并标未知，由补单作业按商户订单号查询
            _logger.LogWarning(ex, "渠道创建调用抛出异常，收款单 {CollectionNo} 保持 Pending 并标记未知", targetNo);
            channelResult = new ChannelCreateResult(Succeeded: false, Unknown: true);
        }

        // ── 第三步：回写渠道结果（requiresNew + 提交）────────────────────────────────────
        if (channelResult.Succeeded)
        {
            await UpdateChannelFieldsAsync(targetId, tradeNo: channelResult.TradeNo,
                payload: channelResult.Payload, unknown: false, note: null);
        }
        else if (channelResult.Unknown)
        {
            await UpdateChannelFieldsAsync(targetId, tradeNo: null, payload: null,
                unknown: true, note: "Channel call timed out or outcome unknown; pending reconciliation");
        }
        else
        {
            // 渠道明确失败：把行改成 ChannelFailed，退出 Pending 唯一索引——同一渠道重试能拿到新二维码，
            // 不占着索引等到报价过期（计划第 3.1 节）
            await MarkChannelFailedAsync(targetId, channelResult.FailureNote ?? "Channel rejected the request");
            throw new BusinessException(Contracts.CollectionErrorCodes.ChannelFailed)
                .WithData("CollectionNo", targetNo);
        }

        return await ReadAsync(async () =>
            (await GetAsync(targetId))
            ?? throw new BusinessException(Contracts.CollectionErrorCodes.CollectionNotFound)
                .WithData("CollectionId", targetId));
    }

    public virtual async Task<Contracts.CancelCollectionResult> CancelAsync(Guid collectionId)
    {
        // ── 本地先改 Closed 并提交（requiresNew），渠道关单在事务外（计划第 3.1 节）──────────
        Contracts.CollectionDto snapshot;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var row = await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
            if (row == null)
            {
                throw new BusinessException(Contracts.CollectionErrorCodes.CollectionNotFound)
                    .WithData("CollectionId", collectionId);
            }

            if (row.Status != Contracts.CollectionStatus.Pending)
            {
                // 收款单不是 Pending 时不请求渠道关单，返回当前状态，不抛异常——
                // 调用方收到已完成时按金额币种走支付或异常，不再创建新收款
                await uow.CompleteAsync();
                return new Contracts.CancelCollectionResult { Collection = Map(row) };
            }

            row.Status = Contracts.CollectionStatus.Closed;
            row.StatusNote = "Cancelled by business caller";
            snapshot = Map(row);
            await uow.CompleteAsync();
        }

        // ── 渠道关单（事务之外）────────────────────────────────────────────────────────────
        var gateway = _gateways.FirstOrDefault(g => g.Channel == snapshot.Channel);
        if (gateway != null)
        {
            ChannelCloseResult closeResult;
            try
            {
                closeResult = await gateway.CloseAsync(new ChannelCloseRequest(
                    snapshot.Id, snapshot.CollectionNo, snapshot.ChannelTradeNo,
                    Contracts.CollectionAmounts.Parse(snapshot.Amount), snapshot.Currency));
            }
            catch (Exception ex)
            {
                // 超时或网络失败：本地仍为 Closed 并告警（计划第 3.1 节）
                _logger.LogWarning(ex, "渠道关单调用失败，收款单 {CollectionNo} 本地保持 Closed", snapshot.CollectionNo);
                return new Contracts.CancelCollectionResult { Collection = snapshot };
            }

            switch (closeResult.Outcome)
            {
                case ChannelCloseOutcome.Closed:
                case ChannelCloseOutcome.OrderNotFound:
                    // 「订单不存在」视为关单成功
                    break;

                case ChannelCloseOutcome.AlreadyPaid:
                    // 渠道返回已支付：按渠道单号和金额核对后直接标 Completed，发布完成事件（升级路径）
                    if (closeResult.PaidAmount != null
                        && closeResult.PaidAmount == Contracts.CollectionAmounts.Parse(snapshot.Amount))
                    {
                        return new Contracts.CancelCollectionResult
                        {
                            Collection = await EscalateToCompletedAsync(snapshot, closeResult.TradeNo)
                        };
                    }

                    // 拿不到渠道金额或金额对不上：保持 Closed，但把行标记为结果未知并留痕——
                    // 不能只留一条日志（无处可查），M3 的补单作业按商户单号查询时会接手
                    await UpdateChannelFieldsAsync(snapshot.Id, tradeNo: closeResult.TradeNo,
                        payload: null, unknown: true,
                        note: "Channel reported paid while closing, but the paid amount is unknown or mismatched; awaiting reconciliation.");
                    _logger.LogWarning(
                        "关单发现渠道已支付但金额未知或不一致，收款单 {CollectionNo} 保持 Closed 并标记未知：渠道金额 {PaidAmount}",
                        snapshot.CollectionNo, closeResult.PaidAmount);
                    break;

                case ChannelCloseOutcome.Unknown:
                    _logger.LogWarning("渠道关单结果未知，收款单 {CollectionNo} 本地保持 Closed", snapshot.CollectionNo);
                    break;
            }
        }

        return new Contracts.CancelCollectionResult { Collection = snapshot };
    }

    public virtual Task<Contracts.CollectionDto?> GetAsync(Guid collectionId)
    {
        return ReadAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var row = await dbContext.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == collectionId);
            return row == null ? null : Map(row);
        });
    }

    public virtual Task<List<Contracts.CollectionDto>> ListByBusinessAsync(string businessType, Guid businessId)
    {
        return ReadAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var rows = await dbContext.Collections.AsNoTracking()
                .Where(c => c.BusinessType == businessType && c.BusinessId == businessId)
                .OrderBy(c => c.CreationTime)
                .ToListAsync();
            return rows.Select(Map).ToList();
        });
    }

    /// <summary>读取助手：没有环境工作单元时（直连场景/测试）自开一个非事务 UoW 再读。</summary>
    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        if (_unitOfWorkManager.Current != null)
        {
            return await read();
        }

        using var uow = _unitOfWorkManager.Begin();
        var result = await read();
        await uow.CompleteAsync();
        return result;
    }

    /// <summary>
    /// 余额记账（计划第 3.1 节）：加入调用方环境事务（订单「扣款 + 标支付」同事务的前提），
    /// 余额行锁留到外层提交。余额不足 / 同单冲突在业务异常里给全数据。
    /// </summary>
    public virtual async Task<Contracts.DebitBalanceResult> DebitBalanceAsync(Contracts.DebitBalanceInput input)
    {
        var txnId = await _balanceManager.DebitAsync(
            input.BusinessType, input.BusinessId, input.UserId, input.Currency,
            Contracts.CollectionAmounts.Parse(input.Amount));
        return new Contracts.DebitBalanceResult { BalanceTxnId = txnId };
    }

    /// <summary>回冲该业务单未回冲的扣款；重复回冲只生效一次，没有未回冲扣款时直接成功。</summary>
    public virtual Task ReverseDebitAsync(string businessType, Guid businessId)
    {
        return _balanceManager.ReverseAsync(businessType, businessId);
    }

    /// <summary>按业务单号读取未回冲扣款（补状态作业判断「已扣未标支付」）。</summary>
    public virtual async Task<Contracts.DebitDto?> GetDebitAsync(string businessType, Guid businessId)
    {
        var debit = await _balanceManager.FindUnreversedDebitAsync(businessType, businessId);
        if (debit == null)
        {
            return null;
        }

        return new Contracts.DebitDto
        {
            BalanceTxnId = debit.Id,
            BusinessType = debit.BusinessType!,
            BusinessId = debit.BusinessId!.Value,
            UserId = debit.UserId,
            Currency = debit.Currency,
            Amount = Contracts.CollectionAmounts.Format(-debit.Amount),
        };
    }

    /// <summary>充值流水清单：单调序号正向 + 最近 7 天重扫，Cursor 推进到本批最大序号。</summary>
    public virtual Task<Contracts.ListCreditedTxnsResult> ListCreditedTxnsAsync(long afterSeq, int maxCount)
    {
        return _balanceManager.ListCreditedAsync(afterSeq, maxCount);
    }

    /// <summary>
    /// 充值地址（计划第 3.1 节）：扫链跑过一轮后允许派生；已有地址时未开放也照常返回（附说明）。
    /// 有副作用（可能派生新地址），不是纯查询。
    /// </summary>
    public virtual async Task<Contracts.DepositAddressDto> GetDepositAddressAsync(Guid userId)
    {
        var networkId = await _depositAddressManager
            .WithMasterKey(_chainMasterKey)
            .ResolveBscNetworkIdAsync();
        var (address, enabled, notice) = await _depositAddressManager
            .GetOrDeriveAsync(networkId, userId);
        return new Contracts.DepositAddressDto
        {
            UserId = userId,
            Address = address,
            DepositEnabled = enabled,
            Notice = notice,
        };
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    private ICollectionChannelGateway ResolveGateway(Contracts.CollectionChannel channel)
    {
        var gateway = _gateways.FirstOrDefault(g => g.Channel == channel);
        if (gateway == null)
        {
            throw new BusinessException(Contracts.CollectionErrorCodes.ChannelNotConfigured)
                .WithData("Channel", channel);
        }

        return gateway;
    }

    private async Task AssertWithinChannelLimitAsync(
        Contracts.CollectionChannel channel, Contracts.CollectionCurrency currency, decimal amount)
    {
        // 机械上限按渠道、币种配置（与 X_min 各管一头：下限在保存报价费率时校验，订单模块负责）。
        // 空值 = 不限。
        var key = (channel, currency) switch
        {
            (Contracts.CollectionChannel.WechatNative, Contracts.CollectionCurrency.Cny) => CollectionSettings.MaxAmount.WechatNativeCny,
            (Contracts.CollectionChannel.AlipayPrecreate, Contracts.CollectionCurrency.Cny) => CollectionSettings.MaxAmount.AlipayPrecreateCny,
            (Contracts.CollectionChannel.StripeCheckout, Contracts.CollectionCurrency.Usd) => CollectionSettings.MaxAmount.StripeCheckoutUsd,
            _ => null,
        };
        if (key == null)
        {
            return;
        }

        var raw = await _settingProvider.GetOrNullAsync(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (!Contracts.CollectionAmounts.TryParse(raw, out var limit) || limit < amount)
        {
            // 配置了非法值按「限制存在但读不出」处理：拒绝，避免坏配置静默放开上限
            throw new BusinessException(Contracts.CollectionErrorCodes.AmountExceedsChannelLimit)
                .WithData("Channel", channel)
                .WithData("Currency", currency)
                .WithData("Limit", raw)
                .WithData("Amount", Contracts.CollectionAmounts.Format(amount));
        }
    }

    private async Task UpdateChannelFieldsAsync(Guid id, string? tradeNo, string? payload, bool unknown, string? note)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var row = await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == id);
        if (row == null)
        {
            throw new BusinessException(Contracts.CollectionErrorCodes.CollectionNotFound).WithData("CollectionId", id);
        }

        if (row.Status != Contracts.CollectionStatus.Pending)
        {
            // 等待期间被完成/取消（极端并发）：不再覆盖状态，保留赢家
            return;
        }

        if (tradeNo != null)
        {
            row.ChannelTradeNo = tradeNo;
        }

        if (payload != null)
        {
            row.ChannelPayload = payload;
        }

        row.ChannelResultUnknown = unknown;
        row.StatusNote = note;
        await uow.CompleteAsync();
    }

    private async Task MarkChannelFailedAsync(Guid id, string note)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Collections.Where(c => c.Id == id && c.Status == Contracts.CollectionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, Contracts.CollectionStatus.ChannelFailed)
                .SetProperty(c => c.StatusNote, note)
                .SetProperty(c => c.ChannelResultUnknown, false));
        if (rows == 0)
        {
            // 等待期间已被完成/取消：保留赢家，明确失败只影响仍然 Pending 的行
            _logger.LogInformation("渠道失败回写时收款单 {CollectionId} 已离开 Pending，保留现状", id);
        }

        await uow.CompleteAsync();
    }

    /// <summary>
    /// 升级路径（计划第 3.3 节）：已 Closed 的收款单核对渠道已支付后改回 Completed，
    /// 提交后发布 <see cref="Contracts.CollectionCompletedEto"/>。
    /// </summary>
    private async Task<Contracts.CollectionDto> EscalateToCompletedAsync(Contracts.CollectionDto snapshot, string? tradeNo)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var row = await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == snapshot.Id);
            if (row != null && row.Status == Contracts.CollectionStatus.Closed)
            {
                row.Status = Contracts.CollectionStatus.Completed;
                row.CompletedAt = _clock.Now;
                row.StatusNote = "Escalated: channel reported paid while closing";
                if (!string.IsNullOrEmpty(tradeNo))
                {
                    row.ChannelTradeNo = tradeNo;
                }

                _logger.LogWarning(
                    "已关闭的收款单 {CollectionNo} 在关单时发现渠道已支付，升级为 Completed",
                    snapshot.CollectionNo);

                // 事件挂在本工作单元完成之后才派发（计划第 3.2 节）
                uow.OnCompleted(async () =>
                {
                    await _distributedEventBus.PublishAsync(new Contracts.CollectionCompletedEto
                    {
                        CollectionId = row.Id,
                        CollectionNo = row.CollectionNo,
                        BusinessType = row.BusinessType,
                        BusinessId = row.BusinessId,
                        UserId = row.UserId,
                        Channel = row.Channel,
                        Currency = row.Currency,
                        Amount = Contracts.CollectionAmounts.Format(row.Amount),
                        CompletedAt = row.CompletedAt!.Value,
                        ChannelTradeNo = row.ChannelTradeNo,
                    });
                });

                await uow.CompleteAsync();
                return Map(row);
            }

            await uow.CompleteAsync();
        }

        // 状态已被并发改变（例如成功通知先到）：读当前状态返回
        return await ReadAsync(async () =>
            (await GetAsync(snapshot.Id))
            ?? throw new BusinessException(Contracts.CollectionErrorCodes.CollectionNotFound)
                .WithData("CollectionId", snapshot.Id));
    }

    private string NextCollectionNo()
    {
        // COL + 毫秒级时间戳 + 6 位随机数；唯一索引兜底
        return "COL" + _clock.Now.ToString("yyyyMMddHHmmssfff")
               + Random.Shared.Next(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Contracts.CollectionDto Map(ColCollection row)
    {
        return new Contracts.CollectionDto
        {
            Id = row.Id,
            CollectionNo = row.CollectionNo,
            BusinessType = row.BusinessType,
            BusinessId = row.BusinessId,
            UserId = row.UserId,
            Channel = row.Channel,
            Currency = row.Currency,
            Amount = Contracts.CollectionAmounts.Format(row.Amount),
            Status = row.Status,
            StatusNote = row.StatusNote,
            ChannelResultUnknown = row.ChannelResultUnknown,
            ExpiresAt = row.ExpiresAt,
            ChannelPayload = row.ChannelPayload,
            ChannelTradeNo = row.ChannelTradeNo,
            CompletedAt = row.CompletedAt,
        };
    }
}
