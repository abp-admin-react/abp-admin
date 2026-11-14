using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Payments;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款侧的本地状态机。调用渠道前提交收款准备或关单意图，渠道响应在新的工作单元回写。
/// </summary>
public class CollectionIntegrationService : Contracts.ICollectionIntegrationService, ITransientDependency
{
    /// <summary>Stripe Checkout 会话的有效期下限（创建后至少 30 分钟）。</summary>
    public static readonly TimeSpan StripeMinimumValidity = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ChannelCreateLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ChannelCallTimeout = TimeSpan.FromMinutes(1);

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IEnumerable<ICollectionChannelGateway> _gateways;
    private readonly ISettingProvider _settingProvider;
    private readonly CollectionPaymentConfirmationService _confirmation;
    private readonly Balances.CollectionBalanceManager _balanceManager;
    private readonly Chain.DepositAddressManager _depositAddressManager;
    private readonly Chain.ChainKeyProvider _chainKeyProvider;
    private readonly IClock _clock;
    private readonly ILogger<CollectionIntegrationService> _logger;

    public CollectionIntegrationService(
        IUnitOfWorkManager unitOfWorkManager,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IEnumerable<ICollectionChannelGateway> gateways,
        ISettingProvider settingProvider,
        CollectionPaymentConfirmationService confirmation,
        Balances.CollectionBalanceManager balanceManager,
        Chain.DepositAddressManager depositAddressManager,
        Chain.ChainKeyProvider chainKeyProvider,
        IClock clock,
        ILogger<CollectionIntegrationService> logger)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _dbContextProvider = dbContextProvider;
        _gateways = gateways;
        _settingProvider = settingProvider;
        _confirmation = confirmation;
        _balanceManager = balanceManager;
        _depositAddressManager = depositAddressManager;
        _chainKeyProvider = chainKeyProvider;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task<CollectionDto> CreateAsync(CreateCollectionInput input, Func<CollectionDto, Task>? onPrepared = null)
    {
        var amount = CollectionAmounts.Parse(input.Amount);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", input.Amount);
        }

        await AssertWithinChannelLimitAsync(input.Channel, input.Currency, amount);

        if (input.Channel == CollectionChannel.StripeCheckout && input.ExpiresAt - _clock.Now < StripeMinimumValidity)
        {
            throw new BusinessException(CollectionErrorCodes.StripeExpiryTooShort).WithData("ExpiresAt", input.ExpiresAt);
        }

        var gateway = ResolveGateway(input.Channel);
        var merchantAccount = gateway.MerchantAccountId;
        if (string.IsNullOrWhiteSpace(merchantAccount)
            || merchantAccount.Length > CollectionPaymentIdentity.MaxMerchantAccountIdLength)
        {
            throw new BusinessException(CollectionErrorCodes.ChannelNotConfigured).WithData("Channel", input.Channel);
        }

        CollectionDto prepared;
        Guid? leaseId = null;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var db = await _dbContextProvider.GetDbContextAsync();
            await CollectionPaymentIdentity.LockBusinessAsync(db, input.BusinessType, input.BusinessId);
            var now = _clock.Now;
            var original = await db.Collections.AsNoTracking()
                .Where(row => row.BusinessType == input.BusinessType && row.BusinessId == input.BusinessId
                    && (row.Status == CollectionStatus.Pending || row.Status == CollectionStatus.Completed))
                .OrderByDescending(row => row.Status)
                .ThenByDescending(row => row.CreationTime)
                .FirstOrDefaultAsync();

            ColCollection row;
            if (original is not null)
            {
                if (original.Channel != input.Channel || original.Currency != input.Currency
                    || original.Amount != amount || original.UserId != input.UserId
                    || !string.Equals(original.MerchantAccountId, merchantAccount, StringComparison.Ordinal))
                {
                    throw new BusinessException(CollectionErrorCodes.PendingCollectionConflict)
                        .WithData("BusinessType", input.BusinessType)
                        .WithData("BusinessId", input.BusinessId);
                }

                if (original.Status == CollectionStatus.Completed)
                {
                    return Map(original);
                }

                row = await CollectionPaymentIdentity.FindForUpdateAsync(db, original.Id)
                    ?? throw new BusinessException(CollectionErrorCodes.CollectionNotFound);
                if (row.Status != CollectionStatus.Pending || row.CloseRequested || row.ChannelPaymentMismatch)
                {
                    throw new BusinessException(CollectionErrorCodes.PendingCollectionConflict)
                        .WithData("BusinessId", input.BusinessId);
                }
            }
            else
            {
                row = new ColCollection(Guid.NewGuid())
                {
                    CollectionNo = NextCollectionNo(),
                    UserId = input.UserId,
                    BusinessType = input.BusinessType,
                    BusinessId = input.BusinessId,
                    Channel = input.Channel,
                    MerchantAccountId = merchantAccount,
                    Currency = input.Currency,
                    Amount = amount,
                    Status = CollectionStatus.Pending,
                    ChannelResultUnknown = true,
                    ExpiresAt = input.ExpiresAt,
                    CreationTime = now,
                };
                db.Collections.Add(row);
            }

            if (string.IsNullOrEmpty(row.ChannelPayload) && !IsCreateLeaseActive(row.ChannelCreateLeaseUntil, now)
                && row.ExpiresAt > now)
            {
                leaseId = Guid.NewGuid();
                row.ChannelCreateLeaseId = leaseId;
                row.ChannelCreateLeaseUntil = now + ChannelCreateLease;
                row.NextChannelQueryAt = row.ChannelCreateLeaseUntil;
            }

            prepared = Map(row);
            if (onPrepared is not null)
            {
                await onPrepared(prepared);
            }

            await uow.CompleteAsync();
        }

        if (!leaseId.HasValue)
        {
            return prepared;
        }

        // 渠道收到请求前，本地已持久化商户收款号和业务关联，回调可用原单号确认。
        ChannelCreateResult result;
        try
        {
            using var timeout = new CancellationTokenSource(ChannelCallTimeout);
            result = await gateway.CreateAsync(new ChannelCreateRequest(
                prepared.Id, prepared.CollectionNo, amount, input.Currency, prepared.ExpiresAt!.Value), timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "收款单 {CollectionNo} 的渠道创建结果未知", prepared.CollectionNo);
            result = new ChannelCreateResult(Succeeded: false, Unknown: true);
        }

        var updated = await SaveCreateResultAsync(prepared.Id, leaseId.Value, result);
        if (!result.Succeeded && !result.Unknown && updated.Status == CollectionStatus.ChannelFailed)
        {
            throw new BusinessException(CollectionErrorCodes.ChannelFailed).WithData("CollectionNo", prepared.CollectionNo);
        }

        return updated;
    }

    public virtual async Task<CancelCollectionResult> CancelAsync(Guid collectionId)
    {
        // 先提交关单意图，渠道超时或创建租约未释放时由查单继续处理。
        CollectionSnapshot snapshot;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var db = await _dbContextProvider.GetDbContextAsync();
            var row = await CollectionPaymentIdentity.FindForUpdateAsync(db, collectionId)
                ?? throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("CollectionId", collectionId);
            if (row.Status != CollectionStatus.Pending)
            {
                return new CancelCollectionResult { Collection = Map(row) };
            }

            row.CloseRequested = true;
            row.ChannelResultUnknown = true;
            if (!row.ChannelPaymentMismatch)
            {
                row.StatusNote = "Channel close requested; awaiting confirmation.";
            }
            snapshot = Snapshot(row);
            await uow.CompleteAsync();
        }

        var gateway = FindGateway(snapshot);
        if (gateway is null || IsCreateLeaseActive(snapshot.ChannelCreateLeaseUntil, _clock.Now))
        {
            return new CancelCollectionResult { Collection = snapshot.Collection };
        }

        var updated = await CloseChannelAsync(snapshot, gateway, CancellationToken.None);
        return new CancelCollectionResult { Collection = updated };
    }

    public virtual async Task<CollectionDto> QueryChannelAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(collectionId, cancellationToken);
        if (snapshot.Collection.Status == CollectionStatus.Completed
            || IsCreateLeaseActive(snapshot.ChannelCreateLeaseUntil, _clock.Now))
        {
            return snapshot.Collection;
        }

        var gateway = FindGateway(snapshot)
            ?? throw new BusinessException(CollectionErrorCodes.ChannelNotConfigured)
                .WithData("Channel", snapshot.Collection.Channel);
        return await QueryChannelAsync(snapshot, gateway, retryClose: true, cancellationToken);
    }

    private async Task<CollectionDto> QueryChannelAsync(
        CollectionSnapshot snapshot, ICollectionChannelGateway gateway, bool retryClose, CancellationToken cancellationToken)
    {
        var collection = snapshot.Collection;
        ChannelQueryResult query;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ChannelCallTimeout);
            query = await gateway.QueryAsync(new ChannelQueryRequest(
                collection.Id, collection.CollectionNo, collection.ChannelTradeNo), timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "收款单 {CollectionNo} 主动查单失败", collection.CollectionNo);
            query = new ChannelQueryResult(ChannelQueryOutcome.Unknown);
        }

        switch (query.Outcome)
        {
            case ChannelQueryOutcome.Paid:
                return await ConfirmObservedPaymentAsync(snapshot, query.Payment, cancellationToken);
            case ChannelQueryOutcome.Closed:
                return await ApplyChannelStateAsync(snapshot, ChannelState.Closed, cancellationToken);
            case ChannelQueryOutcome.Pending:
                var pending = await ApplyChannelStateAsync(snapshot, ChannelState.Pending, cancellationToken);
                if (retryClose && snapshot.CloseRequested && pending.Status == CollectionStatus.Pending)
                {
                    var latest = await ReadSnapshotAsync(collection.Id, cancellationToken);
                    if (latest.Collection.Status == CollectionStatus.Pending
                        && latest.ChannelCreateLeaseId == snapshot.ChannelCreateLeaseId
                        && !IsCreateLeaseActive(latest.ChannelCreateLeaseUntil, _clock.Now))
                    {
                        return await CloseChannelAsync(latest, gateway, cancellationToken);
                    }
                }

                return pending;
            default:
                return await ApplyChannelStateAsync(snapshot, ChannelState.Unknown, cancellationToken);
        }
    }

    private async Task<CollectionDto> CloseChannelAsync(
        CollectionSnapshot snapshot, ICollectionChannelGateway gateway, CancellationToken cancellationToken)
    {
        var collection = snapshot.Collection;
        ChannelCloseResult close;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ChannelCallTimeout);
            close = await gateway.CloseAsync(new ChannelCloseRequest(
                collection.Id, collection.CollectionNo, collection.ChannelTradeNo,
                CollectionAmounts.Parse(collection.Amount), collection.Currency), timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "收款单 {CollectionNo} 的渠道关单结果未知", collection.CollectionNo);
            close = new ChannelCloseResult(ChannelCloseOutcome.Unknown);
        }

        return close.Outcome switch
        {
            ChannelCloseOutcome.Closed => await ApplyChannelStateAsync(snapshot, ChannelState.Closed, cancellationToken),
            ChannelCloseOutcome.OrderNotFound => await ApplyChannelStateAsync(snapshot, ChannelState.OrderNotFound, cancellationToken),
            ChannelCloseOutcome.AlreadyPaid when close.Payment is not null =>
                await ConfirmObservedPaymentAsync(snapshot, close.Payment, cancellationToken),
            ChannelCloseOutcome.AlreadyPaid =>
                await QueryChannelAsync(snapshot, gateway, retryClose: false, cancellationToken),
            _ => await ApplyChannelStateAsync(snapshot, ChannelState.Unknown, cancellationToken),
        };
    }

    private async Task<CollectionDto> ConfirmObservedPaymentAsync(
        CollectionSnapshot snapshot, CollectionPaymentObservation? observation, CancellationToken cancellationToken)
    {
        if (observation is not null)
        {
            var result = await _confirmation.ConfirmAsync(observation, snapshot.Collection.Id, cancellationToken);
            if (result.Collection is not null)
            {
                return result.Collection;
            }
        }

        return await ApplyChannelStateAsync(snapshot, ChannelState.Unknown, cancellationToken);
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

    /// <summary>加入调用方的事务，使扣款和订单支付状态一起提交。</summary>
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

    /// <summary>按序号读取新增充值流水并重扫最近 7 天的旧流水，游标只前进。</summary>
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
        var (networkId, estimatedArrivalMinutes) = await _depositAddressManager
            .WithMasterKey(await _chainKeyProvider.GetAsync())
            .ResolveBscDepositNetworkAsync();
        var (address, enabled, notice) = await _depositAddressManager
            .GetOrDeriveAsync(networkId, userId);
        return new Contracts.DepositAddressDto
        {
            UserId = userId,
            Address = address,
            DepositEnabled = enabled,
            Notice = notice,
            EstimatedArrivalMinutes = estimatedArrivalMinutes,
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

    private async Task<CollectionDto> SaveCreateResultAsync(Guid id, Guid leaseId, ChannelCreateResult result)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var row = await CollectionPaymentIdentity.FindForUpdateAsync(db, id)
            ?? throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("CollectionId", id);
        if (row.Status != CollectionStatus.Pending || row.ChannelCreateLeaseId != leaseId)
        {
            return Map(row);
        }

        row.ChannelCreateLeaseUntil = null;
        row.NextChannelQueryAt = row.CloseRequested ? _clock.Now : _clock.Now.AddSeconds(30);

        if (result.Succeeded)
        {
            row.ChannelOrderCreated = true;
            row.ChannelPayload ??= result.Payload;
            row.ChannelTradeNo ??= result.TradeNo;
            if (!row.CloseRequested && !row.ChannelPaymentMismatch)
            {
                row.ChannelResultUnknown = false;
                row.StatusNote = null;
            }
        }
        else if (result.Unknown)
        {
            if (!row.ChannelOrderCreated && !row.ChannelPaymentMismatch)
            {
                row.ChannelResultUnknown = true;
                row.StatusNote = "Channel create result is unknown; awaiting reconciliation.";
            }
        }
        else if (!row.ChannelOrderCreated && !row.ChannelPaymentMismatch)
        {
            row.Status = row.CloseRequested ? CollectionStatus.Closed : CollectionStatus.ChannelFailed;
            row.ChannelResultUnknown = false;
            row.CloseRequested = false;
            row.NextChannelQueryAt = null;
            row.StatusNote = "Channel did not create a payable order.";
        }

        await uow.CompleteAsync();
        return Map(row);
    }

    private async Task<CollectionDto> ApplyChannelStateAsync(
        CollectionSnapshot snapshot, ChannelState state, CancellationToken cancellationToken)
    {
        var id = snapshot.Collection.Id;
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var row = await CollectionPaymentIdentity.FindForUpdateAsync(db, id, cancellationToken)
            ?? throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("CollectionId", id);
        if (row.Status != CollectionStatus.Pending
            || row.ChannelPaymentMismatch
            || row.ChannelCreateLeaseId != snapshot.ChannelCreateLeaseId
            || IsCreateLeaseActive(row.ChannelCreateLeaseUntil, _clock.Now))
        {
            return Map(row);
        }

        if (state == ChannelState.Closed
            || (state == ChannelState.OrderNotFound && row.CloseRequested && row.ChannelOrderCreated))
        {
            row.Status = CollectionStatus.Closed;
            row.StatusNote = "Channel confirmed the collection is closed.";
            row.ChannelResultUnknown = false;
            row.CloseRequested = false;
            row.NextChannelQueryAt = null;
        }
        else if (state == ChannelState.Pending)
        {
            row.ChannelOrderCreated = true;
            row.ChannelResultUnknown = row.CloseRequested;
            row.StatusNote = row.CloseRequested ? "Channel order is still payable; close will be retried." : null;
        }
        else
        {
            row.ChannelResultUnknown = true;
            row.StatusNote = "Channel result is unknown; awaiting reconciliation.";
        }

        await uow.CompleteAsync(cancellationToken);
        return Map(row);
    }

    private async Task<CollectionSnapshot> ReadSnapshotAsync(Guid id, CancellationToken cancellationToken)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var db = await _dbContextProvider.GetDbContextAsync();
        var row = await db.Collections.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("CollectionId", id);
        var snapshot = Snapshot(row);
        await uow.CompleteAsync(cancellationToken);
        return snapshot;
    }

    private ICollectionChannelGateway? FindGateway(CollectionSnapshot snapshot) =>
        _gateways.FirstOrDefault(gateway => gateway.Channel == snapshot.Collection.Channel
            && string.Equals(gateway.MerchantAccountId, snapshot.MerchantAccountId, StringComparison.Ordinal));

    private static bool IsCreateLeaseActive(DateTime? until, DateTime now) => until > now;

    private static CollectionSnapshot Snapshot(ColCollection row) =>
        new(Map(row), row.MerchantAccountId, row.CloseRequested, row.ChannelCreateLeaseId, row.ChannelCreateLeaseUntil);

    private sealed record CollectionSnapshot(
        CollectionDto Collection,
        string? MerchantAccountId,
        bool CloseRequested,
        Guid? ChannelCreateLeaseId,
        DateTime? ChannelCreateLeaseUntil);

    private enum ChannelState
    {
        Unknown,
        Pending,
        Closed,
        OrderNotFound,
    }

    private string NextCollectionNo()
    {
        // COL + 毫秒级时间戳 + 6 位随机数；唯一索引兜底
        return "COL" + _clock.Now.ToString("yyyyMMddHHmmssfff")
               + Random.Shared.Next(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static Contracts.CollectionDto Map(ColCollection row)
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
            ChannelPayload = row.Status == CollectionStatus.Pending && !row.CloseRequested && !row.ChannelPaymentMismatch
                ? row.ChannelPayload
                : null,
            ChannelTradeNo = row.ChannelTradeNo,
            CompletedAt = row.CompletedAt,
        };
    }
}
