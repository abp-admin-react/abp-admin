using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public record Plan(string Id, string Name, decimal PriceCny, long Tokens);

public class PlanCatalog(IOptions<TokenGatewayOptions> options)
{
    public IReadOnlyList<Plan> All() =>
        options.Value.EffectivePlans.Select(p => new Plan(p.Id, p.Name, p.PriceCny, p.Tokens)).ToList();

    public Plan Find(string? id) =>
        All().FirstOrDefault(p => p.Id == id) ?? throw GatewayException.BadRequest("planId 无效");
}
