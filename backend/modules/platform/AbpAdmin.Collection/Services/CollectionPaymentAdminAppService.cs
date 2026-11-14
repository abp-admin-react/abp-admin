using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Collection.Services;

/// <summary>宿主管理端的外部收款单列表和详情。</summary>
[Authorize(CollectionPermissions.Payments.View)]
public class CollectionPaymentAdminAppService(
    IDbContextProvider<CollectionDbContext> dbContextProvider) : ApplicationService
{
    public virtual async Task<PagedResultDto<CollectionPaymentAdminDto>> GetListAsync(CollectionPaymentListInput input)
    {
        AssertHost();

        var dbContext = await dbContextProvider.GetDbContextAsync();
        var query = dbContext.Collections.AsNoTracking();
        if (input.Channel is { } channel)
        {
            query = query.Where(row => row.Channel == channel);
        }

        if (input.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }

        if (input.Filter?.Trim() is { Length: > 0 } filter)
        {
            var search = filter.ToUpperInvariant();
            var identifier = Guid.TryParse(filter, out var parsed) ? parsed : (Guid?)null;
            query = query.Where(row =>
                row.CollectionNo.ToUpper().Contains(search) ||
                (row.ChannelTradeNo != null && row.ChannelTradeNo.ToUpper().Contains(search)) ||
                (identifier.HasValue &&
                    (row.Id == identifier || row.BusinessId == identifier || row.UserId == identifier)));
        }

        var totalCount = await query.LongCountAsync();
        var items = await query
            .OrderByDescending(row => row.CreationTime)
            .ThenByDescending(row => row.Id)
            .Skip(input.SkipCount)
            .Take(Math.Min(input.MaxResultCount, 200))
            .ToListAsync();

        return new PagedResultDto<CollectionPaymentAdminDto>(totalCount, items.Select(Map).ToList());
    }

    public virtual async Task<CollectionPaymentAdminDto> GetAsync(Guid id)
    {
        AssertHost();

        var dbContext = await dbContextProvider.GetDbContextAsync();
        var row = await dbContext.Collections.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id)
            ?? throw new EntityNotFoundException(typeof(ColCollection), id);
        return Map(row);
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    private static CollectionPaymentAdminDto Map(ColCollection row) => new()
    {
        Id = row.Id,
        CollectionNo = row.CollectionNo,
        BusinessType = row.BusinessType,
        BusinessId = row.BusinessId,
        UserId = row.UserId,
        Channel = row.Channel.ToString(),
        Currency = row.Currency.ToString(),
        Amount = CollectionAmounts.Format(row.Amount),
        Status = row.Status.ToString(),
        StatusNote = row.StatusNote,
        ChannelResultUnknown = row.ChannelResultUnknown,
        ChannelTradeNo = row.ChannelTradeNo,
        ExpiresAt = row.ExpiresAt,
        CompletedAt = row.CompletedAt,
        CreationTime = row.CreationTime,
    };
}
