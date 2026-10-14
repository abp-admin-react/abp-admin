using System.Security.Cryptography;
using System.Text;
using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.TokenGateway.Core;

public interface IKeyProtector
{
    string Protect(string plain);
    string Unprotect(string cipher);
}

public sealed class AbpKeyProtector(IStringEncryptionService enc) : IKeyProtector
{
    public string Protect(string plain) => enc.Encrypt(plain)!;
    public string Unprotect(string cipher) => enc.Decrypt(cipher)!;
}

/// <summary>每用户一把长期 hb-（登录复用，库里存哈希 + 密文），外加有时限会话 hbs-（只存哈希）。</summary>
public class KeyStore(IDbContextFactory<TokenGatewayDbContext> dbf, TimeProvider clock, IKeyProtector protector, IOptions<TokenGatewayOptions> options)
{
    /// <summary>已有则解密返回原 Key（登录复用，不轮换）；没有则新建。明文 Key 只在返回值里，不落日志。</summary>
    public async Task<string> IssueOrReuseKeyAsync(Guid userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var row = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId);
        if (row is not null) return protector.Unprotect(row.KeyCipher);

        var key = "hb-" + Random(24);
        db.ApiKeys.Add(new TgApiKey { UserId = userId, KeyHash = Hash(key), KeyCipher = protector.Protect(key), CreatedAt = Now() });
        try
        {
            await db.SaveChangesAsync();
            return key;
        }
        catch (DbUpdateException)
        {
            // 并发登录：另一请求先建了，读它的。
            await using var db2 = await dbf.CreateDbContextAsync();
            var other = await db2.ApiKeys.AsNoTracking().FirstAsync(k => k.UserId == userId);
            return protector.Unprotect(other.KeyCipher);
        }
    }

    /// <summary>吊销该用户的长期 hb- Key（删行）；下次登录会签发新 Key。会话 hbs- 不受影响，到期自然失效。</summary>
    public async Task RevokeKeyAsync(Guid userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.ApiKeys.Where(k => k.UserId == userId).ExecuteDeleteAsync();
    }

    public async Task<(string Token, DateTimeOffset ExpiresAt)> IssueSessionAsync(Guid userId)
    {
        var token = "hbs-" + Random(24);
        var exp = Now().AddHours(options.Value.SessionHours);
        await using var db = await dbf.CreateDbContextAsync();
        db.Sessions.Add(new TgSession { TokenHash = Hash(token), UserId = userId, ExpiresAt = exp });
        await db.SaveChangesAsync();
        return (token, new DateTimeOffset(exp, TimeSpan.Zero));
    }

    /// <summary>Bearer 可以是 hb-（对话/钱包都认）或 hbs-（仅钱包）。</summary>
    public async Task<Guid> ResolveAsync(string? bearer, bool allowSession)
    {
        if (string.IsNullOrWhiteSpace(bearer)) throw GatewayException.InvalidKey();
        bearer = bearer.Trim();
        var h = Hash(bearer);
        await using var db = await dbf.CreateDbContextAsync();
        if (bearer.StartsWith("hb-", StringComparison.Ordinal))
        {
            var k = await db.ApiKeys.AsNoTracking().Where(x => x.KeyHash == h).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync();
            if (k is { } u) return u;
        }
        else if (allowSession && bearer.StartsWith("hbs-", StringComparison.Ordinal))
        {
            var now = Now();
            var s = await db.Sessions.AsNoTracking().Where(x => x.TokenHash == h && x.ExpiresAt > now).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync();
            if (s is { } u) return u;
        }
        throw GatewayException.InvalidKey();
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
    private static string Random(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    internal static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
