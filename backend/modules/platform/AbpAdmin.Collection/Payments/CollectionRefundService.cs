using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Payments;

public class CollectionRefundService(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    ICurrentTenant currentTenant,
    IEnumerable<ICollectionRefundChannelGateway> gateways,
    IClock clock,
    ILogger<CollectionRefundService> logger) : ICollectionRefundService, ITransientDependency
{
    private static readonly TimeSpan SubmissionLease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ChannelCallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);
    private const int ReconcileBatchSize = 50;

    public virtual async Task<CollectionRefundDto> RequestAsync(
        RequestCollectionRefundInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var amount = CollectionAmounts.Parse(input.Amount);
        if (amount <= 0 || (input.Currency is CollectionCurrency.Cny or CollectionCurrency.Usd &&
            decimal.Round(amount, 2) != amount))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount);
        }

        if (input.CollectionId == Guid.Empty || input.RequestId == Guid.Empty)
        {
            throw new BusinessException(CollectionRefundErrorCodes.RefundNotAllowed);
        }

        ColCollectionRefund refund;
        ColCollection collection;
        ICollectionRefundChannelGateway gateway;
        using (var uow = BeginTransaction())
        {
            var db = await dbContextProvider.GetDbContextAsync();
            // 同一收款的创建和结果确认共用父行锁。失败释放的额度也在此锁下可见。
            collection = await LockCollectionAsync(db, input.CollectionId, cancellationToken);
            var existing = await db.Set<ColCollectionRefund>().SingleOrDefaultAsync(
                row => row.CollectionId == input.CollectionId && row.RequestId == input.RequestId, cancellationToken);
            if (existing != null)
            {
                if (existing.Amount != amount || existing.Currency != input.Currency)
                {
                    throw new BusinessException(CollectionRefundErrorCodes.RefundIdempotencyConflict);
                }

                await uow.CompleteAsync(cancellationToken);
                return Map(existing);
            }

            if (collection.Status != CollectionStatus.Completed || collection.Currency != input.Currency ||
                string.IsNullOrWhiteSpace(collection.MerchantAccountId) || string.IsNullOrWhiteSpace(collection.ChannelTradeNo))
            {
                throw new BusinessException(CollectionRefundErrorCodes.RefundNotAllowed);
            }

            gateway = ResolveGateway(collection.Channel, collection.MerchantAccountId);
            var reserved = await db.Set<ColCollectionRefund>()
                .Where(row => row.CollectionId == collection.Id &&
                    (row.Status == CollectionRefundStatus.Pending || row.Status == CollectionRefundStatus.Succeeded))
                .SumAsync(row => row.Amount, cancellationToken);
            if (amount > collection.Amount - reserved)
            {
                throw new BusinessException(CollectionRefundErrorCodes.RefundAmountExceeded);
            }

            var id = Guid.NewGuid();
            var now = clock.Now;
            refund = new ColCollectionRefund(id)
            {
                CollectionId = collection.Id,
                RequestId = input.RequestId,
                MerchantRefundNo = id.ToString("N"),
                MerchantAccountId = collection.MerchantAccountId,
                Channel = collection.Channel,
                Currency = collection.Currency,
                Amount = amount,
                Status = CollectionRefundStatus.Pending,
                CreationTime = now,
                LastCheckedAt = now,
                NextSubmissionAt = now.Add(SubmissionLease),
            };
            db.Set<ColCollectionRefund>().Add(refund);
            await uow.CompleteAsync(cancellationToken);
        }

        return await SubmitAsync(gateway, refund, collection, cancellationToken);
    }

    public virtual async Task<CollectionRefundDto> GetAsync(Guid refundId, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = BeginRead();
        var db = await dbContextProvider.GetDbContextAsync();
        var refund = await ReadRefundAsync(db, refundId, cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return Map(refund);
    }

    public virtual async Task<IReadOnlyList<CollectionRefundDto>> ListAsync(
        Guid collectionId, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = BeginRead();
        var db = await dbContextProvider.GetDbContextAsync();
        var refunds = await db.Set<ColCollectionRefund>().AsNoTracking().Where(row => row.CollectionId == collectionId)
            .OrderByDescending(row => row.CreationTime).ThenByDescending(row => row.Id).ToListAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return refunds.Select(Map).ToList();
    }

    public virtual async Task<CollectionRefundDto> QueryAsync(Guid refundId, CancellationToken cancellationToken = default)
    {
        AssertHost();
        ColCollectionRefund refund;
        ColCollection collection;
        using (var uow = BeginRead())
        {
            var db = await dbContextProvider.GetDbContextAsync();
            refund = await ReadRefundAsync(db, refundId, cancellationToken);
            collection = await db.Collections.AsNoTracking().SingleAsync(row => row.Id == refund.CollectionId, cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        if (refund.Status != CollectionRefundStatus.Pending)
        {
            return Map(refund);
        }

        var gateway = ResolveGateway(refund.Channel, refund.MerchantAccountId);
        var outcome = await CallChannelAsync(refund,
            token => gateway.QueryRefundAsync(ToChannelRequest(refund, collection), token), cancellationToken);
        if (outcome.Outcome == CollectionRefundOutcome.NotFound &&
            await TryClaimSubmissionAsync(refund, cancellationToken))
        {
            // 只有渠道确认查无此单才补提交，始终复用原商户退款号。
            return await SubmitAsync(gateway, refund, collection, cancellationToken);
        }

        return await ApplyChannelResultAsync(refund, collection, outcome, cancellationToken);
    }

    public virtual async Task<CollectionRefundDto> ConfirmAsync(
        CollectionRefundConfirmationInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        if (input.Outcome is not (CollectionRefundOutcome.Pending or CollectionRefundOutcome.Succeeded or CollectionRefundOutcome.Failed)
            || string.IsNullOrWhiteSpace(input.MerchantRefundNo)
            || (input.Outcome == CollectionRefundOutcome.Succeeded && input.ChannelRefundNo == null)
            || (input.ChannelRefundNo != null &&
                (string.IsNullOrWhiteSpace(input.ChannelRefundNo) || input.ChannelRefundNo.Length > CollectionConsts.MaxTradeNoLength)))
        {
            throw new BusinessException(CollectionRefundErrorCodes.RefundEvidenceMismatch);
        }

        try
        {
            using var uow = BeginTransaction();
            var db = await dbContextProvider.GetDbContextAsync();
            var key = await db.Set<ColCollectionRefund>().AsNoTracking()
                .Where(row => row.MerchantRefundNo == input.MerchantRefundNo)
                .Select(row => new { row.Id, row.CollectionId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(typeof(ColCollectionRefund), input.MerchantRefundNo);
            var collection = await LockCollectionAsync(db, key.CollectionId, cancellationToken);
            var refund = await db.Set<ColCollectionRefund>().SingleAsync(row => row.Id == key.Id, cancellationToken);
            AssertEvidence(refund, collection, input);

            if (refund.Status == CollectionRefundStatus.Pending)
            {
                refund.ChannelRefundNo ??= input.ChannelRefundNo;
                refund.ChannelResultUnknown = false;
                refund.LastCheckedAt = clock.Now;
                refund.StatusNote = LimitNote(input.StatusNote);
                if (input.Outcome == CollectionRefundOutcome.Succeeded)
                {
                    refund.Status = CollectionRefundStatus.Succeeded;
                    // PostgreSQL stores timestamps at microsecond precision. Return the same value on replay.
                    var now = clock.Now;
                    refund.SucceededAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
                    CollectionPaymentEventOutbox.Enqueue(db, new CollectionRefundSucceededEto
                    {
                        RefundId = refund.Id,
                        CollectionId = collection.Id,
                        BusinessType = collection.BusinessType,
                        BusinessId = collection.BusinessId,
                        Channel = refund.Channel,
                        Currency = refund.Currency,
                        Amount = CollectionAmounts.Format(refund.Amount),
                        ChannelRefundNo = refund.ChannelRefundNo!,
                        SucceededAt = refund.SucceededAt.Value,
                    });
                }
                else if (input.Outcome == CollectionRefundOutcome.Failed)
                {
                    refund.Status = CollectionRefundStatus.Failed;
                }
            }
            else if (refund.Status == CollectionRefundStatus.Failed && input.Outcome == CollectionRefundOutcome.Succeeded)
            {
                // Failed 已释放可退额度。收到相反的终态证据必须对账，不能静默修改已释放额度。
                throw new BusinessException(CollectionRefundErrorCodes.RefundEvidenceMismatch);
            }

            await uow.CompleteAsync(cancellationToken);
            return Map(refund);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CollectionRefundConfiguration.ChannelRefundNoIndex })
        {
            throw new BusinessException(CollectionRefundErrorCodes.RefundProviderReferenceConflict, innerException: exception);
        }
    }

    public virtual async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        AssertHost();
        var routes = gateways.Select(gateway => (gateway.Channel, gateway.MerchantAccountId)).Distinct().ToArray();
        if (routes.Length == 0)
        {
            return;
        }

        var candidates = new List<Guid>();
        using (var uow = BeginRead())
        {
            var db = await dbContextProvider.GetDbContextAsync();
            var due = clock.Now.Subtract(ReconcileInterval);
            foreach (var route in routes)
            {
                var rows = await db.Set<ColCollectionRefund>().AsNoTracking()
                    .Where(row => row.Status == CollectionRefundStatus.Pending && row.LastCheckedAt <= due &&
                        row.Channel == route.Channel && row.MerchantAccountId == route.MerchantAccountId)
                    .OrderBy(row => row.LastCheckedAt).ThenBy(row => row.Id)
                    .Select(row => row.Id)
                    .Take(ReconcileBatchSize)
                    .ToListAsync(cancellationToken);
                candidates.AddRange(rows);
            }

            await uow.CompleteAsync(cancellationToken);
        }

        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await QueryAsync(id, cancellationToken);
            }
            catch (BusinessException exception)
            {
                logger.LogWarning(exception, "Refund {RefundId} requires reconciliation", id);
                await MarkUnknownAsync(id, cancellationToken);
            }
        }
    }

    private async Task<CollectionRefundDto> SubmitAsync(
        ICollectionRefundChannelGateway gateway, ColCollectionRefund refund, ColCollection collection,
        CancellationToken cancellationToken)
    {
        var result = await CallChannelAsync(refund,
            token => gateway.RefundAsync(ToChannelRequest(refund, collection), token), cancellationToken);
        return await ApplyChannelResultAsync(refund, collection, result, cancellationToken);
    }

    private async Task<CollectionRefundChannelResult> CallChannelAsync(
        ColCollectionRefund refund, Func<CancellationToken, Task<CollectionRefundChannelResult>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ChannelCallTimeout);
            return await call(timeout.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Channel outcome for refund {RefundId} is unknown", refund.Id);
            return new CollectionRefundChannelResult(CollectionRefundOutcome.Unknown);
        }
    }

    private async Task<CollectionRefundDto> ApplyChannelResultAsync(
        ColCollectionRefund refund, ColCollection collection, CollectionRefundChannelResult result,
        CancellationToken cancellationToken)
    {
        if (result.Confirmation is not { } evidence)
        {
            return await MarkUnknownAsync(refund.Id, cancellationToken);
        }

        try
        {
            if (evidence.Outcome != result.Outcome)
            {
                throw new BusinessException(CollectionRefundErrorCodes.RefundEvidenceMismatch);
            }

            AssertEvidence(refund, collection, evidence);
            return await ConfirmAsync(evidence, cancellationToken);
        }
        catch (BusinessException exception) when (exception.Code is CollectionRefundErrorCodes.RefundEvidenceMismatch
            or CollectionRefundErrorCodes.RefundProviderReferenceConflict)
        {
            // 渠道已接到请求，证据不一致也保留退款额度，由查单补偿重新核实。
            await MarkUnknownAsync(refund.Id, cancellationToken);
            throw;
        }
    }

    private async Task<CollectionRefundDto> MarkUnknownAsync(Guid refundId, CancellationToken cancellationToken)
    {
        using var uow = BeginTransaction();
        var db = await dbContextProvider.GetDbContextAsync();
        await db.Set<ColCollectionRefund>()
            .Where(row => row.Id == refundId && row.Status == CollectionRefundStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ChannelResultUnknown, true)
                .SetProperty(row => row.LastCheckedAt, clock.Now), cancellationToken);
        var refund = await ReadRefundAsync(db, refundId, cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return Map(refund);
    }

    private async Task<bool> TryClaimSubmissionAsync(ColCollectionRefund refund, CancellationToken cancellationToken)
    {
        using var uow = BeginTransaction();
        var db = await dbContextProvider.GetDbContextAsync();
        var now = clock.Now;
        var claimed = await db.Set<ColCollectionRefund>()
            .Where(row => row.Id == refund.Id && row.Status == CollectionRefundStatus.Pending &&
                row.ChannelRefundNo == null && row.NextSubmissionAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.NextSubmissionAt, now.Add(SubmissionLease))
                .SetProperty(row => row.LastCheckedAt, now), cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return claimed == 1;
    }

    private static void AssertEvidence(ColCollectionRefund refund, ColCollection collection, CollectionRefundConfirmationInput input)
    {
        if (input.Channel != refund.Channel || input.MerchantAccountId != refund.MerchantAccountId ||
            input.MerchantRefundNo != refund.MerchantRefundNo || input.CollectionNo != collection.CollectionNo ||
            input.Currency != refund.Currency || !CollectionAmounts.TryParse(input.Amount, out var amount) ||
            amount != refund.Amount || (input.ChannelRefundNo != null && refund.ChannelRefundNo != null &&
            input.ChannelRefundNo != refund.ChannelRefundNo) ||
            (refund.Status == CollectionRefundStatus.Pending && input.Outcome == CollectionRefundOutcome.Failed &&
             refund.ChannelRefundNo != null && input.ChannelRefundNo == null))
        {
            throw new BusinessException(CollectionRefundErrorCodes.RefundEvidenceMismatch);
        }
    }

    private ICollectionRefundChannelGateway ResolveGateway(CollectionChannel channel, string merchantAccountId) =>
        gateways.SingleOrDefault(gateway => gateway.Channel == channel && gateway.MerchantAccountId == merchantAccountId)
        ?? throw new BusinessException(CollectionErrorCodes.ChannelNotConfigured);

    private static async Task<ColCollection> LockCollectionAsync(
        CollectionDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.Collections.FromSqlInterpolated($"""
            SELECT * FROM "ColCollections" WHERE "Id" = {id} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
        return rows.SingleOrDefault() ?? throw new EntityNotFoundException(typeof(ColCollection), id);
    }

    private static async Task<ColCollectionRefund> ReadRefundAsync(
        CollectionDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Set<ColCollectionRefund>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
        ?? throw new EntityNotFoundException(typeof(ColCollectionRefund), id);

    private static CollectionRefundChannelRequest ToChannelRequest(ColCollectionRefund refund, ColCollection collection) =>
        new(refund.MerchantRefundNo, collection.CollectionNo, collection.ChannelTradeNo!, collection.Amount,
            refund.Amount, refund.Currency);

    private static string? LimitNote(string? note) =>
        note?.Length > CollectionConsts.MaxNoteLength ? note[..CollectionConsts.MaxNoteLength] : note;

    private IUnitOfWork BeginTransaction() =>
        unitOfWorkManager.Begin(requiresNew: true, isTransactional: true, isolationLevel: IsolationLevel.ReadCommitted);

    private IUnitOfWork BeginRead() => unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);

    private void AssertHost()
    {
        if (currentTenant.Id.HasValue)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    private static CollectionRefundDto Map(ColCollectionRefund refund) => new()
    {
        Id = refund.Id,
        CollectionId = refund.CollectionId,
        RequestId = refund.RequestId,
        MerchantRefundNo = refund.MerchantRefundNo,
        ChannelRefundNo = refund.ChannelRefundNo,
        Channel = refund.Channel,
        Currency = refund.Currency,
        Amount = CollectionAmounts.Format(refund.Amount),
        Status = refund.Status,
        ChannelResultUnknown = refund.ChannelResultUnknown,
        StatusNote = refund.StatusNote,
        CreationTime = refund.CreationTime,
        SucceededAt = refund.SucceededAt,
    };
}
