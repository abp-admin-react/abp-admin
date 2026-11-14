using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

[Authorize(CollectionPermissions.BalanceAdjustments.View)]
public class CollectionBalanceAdjustmentAppService(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    IIdentityUserRepository users,
    CollectionBalanceManager balances,
    CollectionLedgerPoster ledger) : ApplicationService
{
    [HttpGet("api/app/collection-balance-adjustment")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<CollectionBalanceAdjustmentDto>> GetListAsync(
        CollectionBalanceAdjustmentListInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContextProvider.GetDbContextAsync();
        var query = db.BalanceAdjustments.AsNoTracking();
        if (input.UserId is { } userId)
        {
            query = query.Where(row => row.UserId == userId);
        }
        if (input.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }
        if (input.Filter?.Trim() is { Length: > 0 } filter)
        {
            var id = Guid.TryParse(filter, out var parsed) ? parsed : (Guid?)null;
            query = query.Where(row => row.Reason.Contains(filter) || row.ReviewNote != null && row.ReviewNote.Contains(filter)
                || id.HasValue && (row.Id == id || row.RequestId == id || row.UserId == id));
        }

        var count = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.CreationTime).ThenByDescending(row => row.Id)
            .Skip(input.SkipCount).Take(Math.Min(input.MaxResultCount, 200)).ToListAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return new(count, rows.Select(Map).ToList());
    }

    [HttpGet("api/app/collection-balance-adjustment/{id}")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<CollectionBalanceAdjustmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContextProvider.GetDbContextAsync();
        var row = await db.BalanceAdjustments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(typeof(ColBalanceAdjustment), id);
        await uow.CompleteAsync(cancellationToken);
        return Map(row);
    }

    /// <summary>创建待审核申请。同一申请人以原请求号和相同内容重试时返回原申请。</summary>
    [HttpPost("api/app/collection-balance-adjustment")]
    [Authorize(CollectionPermissions.BalanceAdjustments.Create)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<CollectionBalanceAdjustmentDto> CreateAsync(
        CreateCollectionBalanceAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var applicant = RequireCurrentUser();
        var amount = ParseAmount(input);
        var reason = RequiredReason(input.Reason);
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await dbContextProvider.GetDbContextAsync();
        var key = input.RequestId.ToString("D");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('Collection:BalanceAdjustment'), hashtext({key}))", cancellationToken);
        var existing = await db.BalanceAdjustments.SingleOrDefaultAsync(row => row.RequestId == input.RequestId, cancellationToken);
        if (existing != null)
        {
            if (existing.UserId != input.UserId || existing.Currency != input.Currency || existing.Amount != amount
                || existing.Reason != reason || existing.ApplicantId != applicant)
            {
                throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.RequestConflict);
            }

            await uow.CompleteAsync(cancellationToken);
            return Map(existing);
        }

        await RequireTargetUserAsync(input.UserId, cancellationToken);
        var adjustment = new ColBalanceAdjustment(GuidGenerator.Create(), input.RequestId, input.UserId,
            input.Currency, amount, reason, applicant, Clock.Now);
        db.BalanceAdjustments.Add(adjustment);
        await uow.CompleteAsync(cancellationToken);
        return Map(adjustment);
    }

    /// <summary>审批、可用余额、余额流水和总账凭证一起提交。重复通过返回原审批结果。</summary>
    /// <exception cref="AbpAuthorizationException">自审未获得显式用户授权。</exception>
    [HttpPost("api/app/collection-balance-adjustment/{id}/approve")]
    [Authorize(CollectionPermissions.BalanceAdjustments.Approve)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<CollectionBalanceAdjustmentDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var reviewer = RequireCurrentUser();
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await dbContextProvider.GetDbContextAsync();
        var adjustment = await LockAsync(db, id, cancellationToken);
        await CheckReviewerAsync(adjustment, reviewer);
        if (adjustment.Status == CollectionBalanceAdjustmentStatus.Approved)
        {
            await uow.CompleteAsync(cancellationToken);
            return Map(adjustment);
        }

        if (adjustment.Status != CollectionBalanceAdjustmentStatus.Pending)
        {
            throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.InvalidStatus);
        }

        await RequireTargetUserAsync(adjustment.UserId, cancellationToken);
        var txnId = await balances.ApplyAdjustmentAsync(adjustment.Id, adjustment.UserId, adjustment.Currency, adjustment.Amount);
        var voucherId = await ledger.PostBalanceAdjustmentAsync(adjustment.Id, adjustment.UserId, adjustment.Currency,
            adjustment.Amount, adjustment.Reason, reviewer);
        var balanceAfter = await db.BalanceTxns.AsNoTracking().Where(row => row.Id == txnId)
            .Select(row => row.BalanceAfter).SingleAsync(cancellationToken);
        adjustment.Approve(reviewer, Clock.Now, txnId, voucherId, balanceAfter);
        await uow.CompleteAsync(cancellationToken);
        return Map(adjustment);
    }

    /// <summary>拒绝待审申请。重复提交同一拒绝原因时返回原结果。</summary>
    [HttpPost("api/app/collection-balance-adjustment/{id}/reject")]
    [Authorize(CollectionPermissions.BalanceAdjustments.Reject)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<CollectionBalanceAdjustmentDto> RejectAsync(
        Guid id, RejectCollectionBalanceAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var reviewer = RequireCurrentUser();
        var reason = RequiredReason(input.Reason);
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await dbContextProvider.GetDbContextAsync();
        var adjustment = await LockAsync(db, id, cancellationToken);
        await CheckReviewerAsync(adjustment, reviewer);
        if (adjustment.Status == CollectionBalanceAdjustmentStatus.Rejected)
        {
            if (adjustment.ReviewNote != reason)
            {
                throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.RequestConflict);
            }
            await uow.CompleteAsync(cancellationToken);
            return Map(adjustment);
        }

        adjustment.Reject(reviewer, Clock.Now, reason);
        await uow.CompleteAsync(cancellationToken);
        return Map(adjustment);
    }

    private static async Task<ColBalanceAdjustment> LockAsync(CollectionDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.BalanceAdjustments.FromSqlInterpolated($"""
            SELECT * FROM "ColBalanceAdjustments" WHERE "Id" = {id} FOR UPDATE
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault() ?? throw new EntityNotFoundException(typeof(ColBalanceAdjustment), id);
    }

    private async Task CheckReviewerAsync(ColBalanceAdjustment adjustment, Guid reviewer)
    {
        if (adjustment.ApplicantId == reviewer &&
            !await AuthorizationService.IsGrantedAsync(CollectionPermissions.BalanceAdjustments.ApproveOwn))
        {
            throw new AbpAuthorizationException(code: CollectionBalanceAdjustmentErrorCodes.SelfApprovalDenied);
        }
    }

    private async Task RequireTargetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(userId, includeDetails: false, cancellationToken);
        if (user == null || user.TenantId != null)
        {
            throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.UserNotFound);
        }
    }

    private static decimal ParseAmount(CreateCollectionBalanceAdjustmentInput input)
    {
        var text = input.Amount?.Trim();
        var point = text?.IndexOf('.') ?? -1;
        if (input.RequestId == Guid.Empty || input.UserId == Guid.Empty
            || input.Currency is not (CollectionCurrency.Usdt or CollectionCurrency.Usdc)
            || text == null || text.Length > 64 || point >= 0 && text.Length - point - 1 > CollectionAmounts.Scale
            || !CollectionAmounts.TryParse(text, out var amount) || amount == 0m
            || amount > FinanceBizEvent.MaxAmount || amount < -FinanceBizEvent.MaxAmount)
        {
            throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.InvalidInput);
        }

        return amount;
    }

    private static string RequiredReason(string? text)
    {
        var reason = text?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 512)
        {
            throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.InvalidInput);
        }
        return reason;
    }

    private Guid RequireCurrentUser() => CurrentUser.Id ?? throw new AbpAuthorizationException();

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    private static CollectionBalanceAdjustmentDto Map(ColBalanceAdjustment row) => new()
    {
        Id = row.Id,
        RequestId = row.RequestId,
        UserId = row.UserId,
        Currency = row.Currency,
        Amount = CollectionAmounts.Format(row.Amount),
        Reason = row.Reason,
        Status = row.Status,
        ApplicantId = row.ApplicantId,
        ReviewerId = row.ReviewerId,
        ReviewedAt = row.ReviewedAt,
        ReviewNote = row.ReviewNote,
        BalanceTxnId = row.BalanceTxnId,
        LedgerVoucherId = row.LedgerVoucherId,
        BalanceAfter = row.BalanceAfter.HasValue ? CollectionAmounts.Format(row.BalanceAfter.Value) : null,
        CreationTime = row.CreationTime,
    };
}
