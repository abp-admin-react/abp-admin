using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.TokenGateway.Core;

public record Plan(string Id, string Name, decimal PriceCny, long Tokens, int SortOrder, bool IsActive);

public record PlanPatch(string? Name, decimal? PriceCny, long? Tokens, int? SortOrder, bool? IsActive);

/// <summary>价表在 TgPlans；改代币数走管理接口，不发版。</summary>
public class PlanCatalog(IDbContextFactory<TokenGatewayDbContext> dbf, TimeProvider clock)
{
    public async Task<IReadOnlyList<Plan>> AllAsync(bool includeInactive = false)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Plans.AsNoTracking().Where(p => includeInactive || p.IsActive).OrderBy(p => p.SortOrder)
            .Select(p => new Plan(p.Id, p.Name, p.PriceCny, p.Tokens, p.SortOrder, p.IsActive)).ToListAsync();
    }

    public async Task<Plan> FindActiveAsync(string? id) =>
        (await AllAsync()).FirstOrDefault(p => p.Id == id) ?? throw GatewayException.BadRequest("planId 无效");

    public async Task<Plan> UpdateAsync(string id, PlanPatch patch)
    {
        if (patch.Tokens is <= 0) throw GatewayException.BadRequest("tokens 必须 > 0");
        if (patch.PriceCny is <= 0) throw GatewayException.BadRequest("priceCny 必须 > 0");
        if (patch.Name is { Length: 0 or > 64 }) throw GatewayException.BadRequest("name 长度 1~64");
        await using var db = await dbf.CreateDbContextAsync();
        var p = await db.Plans.FirstOrDefaultAsync(x => x.Id == id) ?? throw GatewayException.NotFound();
        if (patch.Name is not null) p.Name = patch.Name;
        if (patch.PriceCny is { } price) p.PriceCny = price;
        if (patch.Tokens is { } t) p.Tokens = t;
        if (patch.SortOrder is { } so) p.SortOrder = so;
        if (patch.IsActive is { } a) p.IsActive = a;
        p.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return new Plan(p.Id, p.Name, p.PriceCny, p.Tokens, p.SortOrder, p.IsActive);
    }
}
