using System.Collections.Concurrent;

namespace AbpAdmin.TokenGateway.Core;

/// <summary>用户可见用量：只记别名，不记真实模型/渠道。</summary>
public record UsageEntry(DateTimeOffset Time, string Alias, string? Bot, long PromptTokens, long CompletionTokens, long Cost, bool Success);

public class UsageLog
{
    private readonly ConcurrentDictionary<Guid, List<UsageEntry>> _byUser = new();

    public void Add(Guid user, UsageEntry e)
    {
        var list = _byUser.GetOrAdd(user, _ => new List<UsageEntry>());
        lock (list) list.Add(e);
    }

    public IReadOnlyList<UsageEntry> Recent(Guid user, int take)
    {
        if (!_byUser.TryGetValue(user, out var list)) return Array.Empty<UsageEntry>();
        lock (list) return list.AsEnumerable().Reverse().Take(take).ToList();
    }
}
