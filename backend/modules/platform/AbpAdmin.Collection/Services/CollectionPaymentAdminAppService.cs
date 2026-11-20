using Microsoft.AspNetCore.Mvc;
using AbpAdmin.Grids;
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
    public virtual Task<PagedResultDto<CollectionPaymentAdminDto>> GetListAsync(CollectionPaymentListInput input) =>
        ReadListPageAsync(null, input);

    [HttpPost("api/app/collection-payment-admin/query")]
    public virtual Task<PagedResultDto<CollectionPaymentAdminDto>> QueryListAsync([FromBody] GridQueryInput grid, [FromQuery] CollectionPaymentListInput input) =>
        ReadListPageAsync(grid, input);

    private async Task<PagedResultDto<CollectionPaymentAdminDto>> ReadListPageAsync(GridQueryInput? grid, CollectionPaymentListInput input)
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

        var definition = new GridQueryDefinition<ColCollection>()
            .SearchableField("id", x => x.Id.ToString())
            .SearchableField("collectionNo", x => x.CollectionNo)
            .SearchableField("userId", x => x.UserId.ToString())
            .SearchableField("businessId", x => x.BusinessId.ToString())
            .SearchableEnumField("channel", x => x.Channel)
            .SearchableEnumField("currency", x => x.Currency)
            .SearchableEnumField("status", x => x.Status)
            .Field("amount", x => x.Amount)
            .SearchableField("channelTradeNo", x => x.ChannelTradeNo)
            .Field("creationTime", x => x.CreationTime);
        if (grid != null) query = definition.Filter(query, grid);
        var totalCount = await query.LongCountAsync();
        var items = await (grid == null
            ? query.OrderByDescending(row => row.CreationTime)
            .ThenByDescending(row => row.Id)
            : definition.OrderBy(query, grid, "creationTime", true, x => x.Id))
            .Skip(grid?.SkipCount ?? input.SkipCount)
            .Take(grid?.MaxResultCount ?? Math.Min(input.MaxResultCount, 200))
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
