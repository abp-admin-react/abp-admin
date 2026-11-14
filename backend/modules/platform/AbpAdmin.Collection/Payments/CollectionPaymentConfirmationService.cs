using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Payments;

public enum CollectionPaymentConfirmationOutcome
{
    Accepted,
    Duplicate,
    Conflict,
    NotFound,
}

public record CollectionPaymentConfirmationResult(
    CollectionPaymentConfirmationOutcome Outcome, CollectionDto? Collection = null);

/// <summary>统一验证回调、主动查单和关单的实付结果，在同一事务内确认收款并记录事件。</summary>
public class CollectionPaymentConfirmationService(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    IClock clock,
    CollectionPaymentEventDispatcher dispatcher,
    ILogger<CollectionPaymentConfirmationService> logger) : ITransientDependency
{
    public virtual Task<CollectionPaymentConfirmationResult> ConfirmAsync(
        CollectionPaymentObservation observation, CancellationToken cancellationToken = default) =>
        ConfirmAsync(observation, expectedCollectionId: null, cancellationToken);

    internal async Task<CollectionPaymentConfirmationResult> ConfirmAsync(
        CollectionPaymentObservation observation, Guid? expectedCollectionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.CollectionNo)
            || observation.CollectionNo.Length > CollectionConsts.MaxCollectionNoLength
            || string.IsNullOrWhiteSpace(observation.MerchantAccountId)
            || observation.MerchantAccountId.Length > CollectionPaymentIdentity.MaxMerchantAccountIdLength
            || string.IsNullOrWhiteSpace(observation.TradeNo)
            || observation.TradeNo.Length > CollectionConsts.MaxTradeNoLength
            || observation.Amount <= 0)
        {
            return new(CollectionPaymentConfirmationOutcome.Conflict);
        }

        CollectionDto confirmed;
        Guid eventId;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var db = await dbContextProvider.GetDbContextAsync();
            var candidate = await db.Collections.AsNoTracking()
                .FirstOrDefaultAsync(row => row.CollectionNo == observation.CollectionNo, cancellationToken);
            if (candidate is null)
            {
                return new(CollectionPaymentConfirmationOutcome.NotFound);
            }

            if ((expectedCollectionId.HasValue && candidate.Id != expectedCollectionId)
                || candidate.Channel != observation.Channel
                || !string.Equals(candidate.MerchantAccountId, observation.MerchantAccountId, StringComparison.Ordinal))
            {
                return new(CollectionPaymentConfirmationOutcome.Conflict);
            }

            // 两笔收款可能收到同一个外部交易号。先锁渠道交易身份，再锁业务单和收款行。
            var tradeKey = $"{(int)observation.Channel}:{observation.MerchantAccountId}:{observation.TradeNo}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext('Collection:ChannelTrade'), hashtext({tradeKey}))",
                cancellationToken);
            await CollectionPaymentIdentity.LockBusinessAsync(
                db, candidate.BusinessType, candidate.BusinessId, cancellationToken);
            var row = await CollectionPaymentIdentity.FindForUpdateAsync(db, candidate.Id, cancellationToken);
            if (row is null)
            {
                return new(CollectionPaymentConfirmationOutcome.NotFound);
            }

            if (row.Currency != observation.Currency || row.Amount != observation.Amount)
            {
                MarkConflict(row, "Channel payment amount or currency does not match the collection.", observation.TradeNo);
                await uow.CompleteAsync(cancellationToken);
                return new(CollectionPaymentConfirmationOutcome.Conflict, CollectionIntegrationService.Map(row));
            }

            if (row.Status == CollectionStatus.Completed)
            {
                if (row.ChannelTradeNo == observation.TradeNo)
                {
                    return new(CollectionPaymentConfirmationOutcome.Duplicate, CollectionIntegrationService.Map(row));
                }

                MarkConflict(row, "A different channel transaction was reported for a completed collection.", observation.TradeNo);
                await uow.CompleteAsync(cancellationToken);
                return new(CollectionPaymentConfirmationOutcome.Conflict, CollectionIntegrationService.Map(row));
            }

            var usedByAnother = await db.Collections.AsNoTracking().AnyAsync(
                other => other.Id != row.Id && other.Status == CollectionStatus.Completed
                    && other.Channel == observation.Channel
                    && other.MerchantAccountId == observation.MerchantAccountId
                    && other.ChannelTradeNo == observation.TradeNo,
                cancellationToken);
            if (usedByAnother)
            {
                MarkConflict(row, "Channel transaction is already linked to a different collection.", observation.TradeNo);
                await uow.CompleteAsync(cancellationToken);
                return new(CollectionPaymentConfirmationOutcome.Conflict, CollectionIntegrationService.Map(row));
            }

            var now = clock.Now;
            var previousStatus = row.Status;
            row.Status = CollectionStatus.Completed;
            row.StatusNote = previousStatus == CollectionStatus.Pending
                ? null
                : "Channel confirmed payment after the collection reached a terminal status.";
            row.CompletedAt = now;
            row.ChannelOrderCreated = true;
            row.ChannelResultUnknown = false;
            row.ChannelPaymentMismatch = false;
            row.ChannelCreateLeaseId = null;
            row.ChannelCreateLeaseUntil = null;
            row.CloseRequested = false;
            row.NextChannelQueryAt = null;
            row.ChannelTradeNo = observation.TradeNo;
            eventId = CollectionPaymentEventOutbox.Enqueue(db, new CollectionCompletedEto
            {
                CollectionId = row.Id,
                CollectionNo = row.CollectionNo,
                BusinessType = row.BusinessType,
                BusinessId = row.BusinessId,
                UserId = row.UserId,
                Channel = row.Channel,
                Currency = row.Currency,
                Amount = CollectionAmounts.Format(row.Amount),
                CompletedAt = now,
                ChannelTradeNo = observation.TradeNo,
            });
            await uow.CompleteAsync(cancellationToken);
            confirmed = CollectionIntegrationService.Map(row);
        }

        try
        {
            await dispatcher.DispatchAsync(eventId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "收款 {CollectionNo} 已确认，完成事件等待重试", confirmed.CollectionNo);
        }

        return new(CollectionPaymentConfirmationOutcome.Accepted, confirmed);
    }

    private static void MarkConflict(ColCollection row, string reason, string tradeNo)
    {
        row.ChannelPaymentMismatch = true;
        row.ChannelResultUnknown = true;
        row.StatusNote = $"{reason} Reported transaction: {tradeNo}";
    }
}
