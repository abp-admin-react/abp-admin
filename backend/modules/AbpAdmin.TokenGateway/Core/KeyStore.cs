using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public enum Principal { ApiKey, Session }

/// <summary>每个用户一把长期 hb-xxx（登录复用），外加有时限的会话令牌 hbs-xxx。只存哈希。</summary>
public class KeyStore(TimeProvider clock, IOptions<TokenGatewayOptions> options)
{
    private readonly ConcurrentDictionary<Guid, string> _keyByUser = new();
    private readonly ConcurrentDictionary<string, Guid> _userByKeyHash = new();
    private readonly ConcurrentDictionary<string, (Guid User, DateTimeOffset Exp)> _sessions = new();

    public string IssueOrReuseKey(Guid userId) =>
        _keyByUser.GetOrAdd(userId, id =>
        {
            var key = "hb-" + Random(24);
            _userByKeyHash[Hash(key)] = id;
            return key;
        });

    public void RevokeKey(Guid userId)
    {
        if (_keyByUser.TryRemove(userId, out var key)) _userByKeyHash.TryRemove(Hash(key), out _);
    }

    public (string Token, DateTimeOffset ExpiresAt) IssueSession(Guid userId)
    {
        var token = "hbs-" + Random(24);
        var exp = clock.GetUtcNow().AddHours(options.Value.SessionHours);
        _sessions[Hash(token)] = (userId, exp);
        return (token, exp);
    }

    /// <summary>Bearer 可以是 hb-（对话/钱包都认）或 hbs-（仅钱包）。</summary>
    public Guid Resolve(string? bearer, bool allowSession)
    {
        if (string.IsNullOrWhiteSpace(bearer)) throw GatewayException.InvalidKey();
        var h = Hash(bearer.Trim());
        if (bearer.StartsWith("hb-", StringComparison.Ordinal) && _userByKeyHash.TryGetValue(h, out var u)) return u;
        if (allowSession && bearer.StartsWith("hbs-", StringComparison.Ordinal) && _sessions.TryGetValue(h, out var s))
        {
            if (s.Exp > clock.GetUtcNow()) return s.User;
            _sessions.TryRemove(h, out _);
        }
        throw GatewayException.InvalidKey();
    }

    private static string Random(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
