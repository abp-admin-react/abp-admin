using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.TokenGateway.Core;

/// <summary>用户可见用量：只记别名，不记真实模型/渠道。</summary>
public record UsageEntry(DateTimeOffset Time, string Alias, string? Bot, long PromptTokens, long CompletionTokens, long Cost, bool Success);

public class UsageLog(IDbContextFactory<TokenGatewayDbContext> dbf, TimeProvider clock)
{
    public async Task AddAsync(Guid user, string alias, string? bot, long prompt, long completion, long cost, bool success)
    {
        await using var db = await dbf.CreateDbContextAsync();
        db.Usage.Add(new TgUsage
        {
            Id = Guid.NewGuid(), UserId = user, Time = clock.GetUtcNow().UtcDateTime, Alias = alias,
            Bot = bot is { Length: > 128 } ? bot[..128] : bot,
            PromptTokens = prompt, CompletionTokens = completion, Cost = cost, Success = success,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<UsageEntry>> RecentAsync(Guid user, int take)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var rows = await db.Usage.AsNoTracking().Where(u => u.UserId == user).OrderByDescending(u => u.Time).Take(take).ToListAsync();
        return rows.Select(u => new UsageEntry(new DateTimeOffset(u.Time, TimeSpan.Zero), u.Alias, u.Bot, u.PromptTokens, u.CompletionTokens, u.Cost, u.Success)).ToList();
    }
}
