using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.Collection.Chain;

public sealed class BscDepositScanProviderFactory(BscDepositProviderClient client, ChainKeyProvider keys,
    IStringEncryptionService encryptor, DepositScanProviderHealth health) : ITransientDependency
{
    private const int DefaultAnkrLogResultCap = 1000;

    internal async Task<IReadOnlyList<DepositScanProvider>> CreateAsync(CollectionDbContext db, ColNetwork network,
        CancellationToken ct)
    {
        var rows = await db.ScanProviders.AsNoTracking()
            .Where(provider => provider.IsEnabled && provider.Networks.Any(link => link.NetworkId == network.Id))
            .OrderBy(provider => provider.SortOrder).ThenBy(provider => provider.Id)
            .Select(provider => new
            {
                provider.Id,
                provider.Provider,
                provider.Label,
                provider.KeyCipher,
                LogResultCap = provider.Networks.Where(link => link.NetworkId == network.Id).Select(link => link.LogResultCap).Single(),
            })
            .ToListAsync(ct);

        if (rows.Count == 0 && network.LegacyDepositRpcCipher == null)
        {
            return [];
        }

        string? masterKey;
        try
        {
            masterKey = await keys.GetAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or FormatException)
        {
            masterKey = null;
        }

        var providers = new List<DepositScanProvider>();
        foreach (var row in rows)
        {
            var key = Decrypt(row.KeyCipher, masterKey);
            var source = row.Provider switch
            {
                ChainApiKeyProvider.RpcBsc when IsProjectKey(key) =>
                    client.Create(row.Provider, key!, row.LogResultCap ?? DefaultAnkrLogResultCap),
                ChainApiKeyProvider.EtherscanV2 when IsExplorerKey(key) =>
                    client.Create(row.Provider, key!, int.MaxValue),
                _ => null,
            };
            var fallbackLabel = row.Provider == ChainApiKeyProvider.RpcBsc ? "Ankr" : "Etherscan";
            providers.Add(new DepositScanProvider(row.Id, string.IsNullOrWhiteSpace(row.Label) ? fallbackLabel : row.Label,
                source, health));
        }

        if (network.LegacyDepositRpcCipher is { } cipher)
        {
            var url = Decrypt(cipher, masterKey);
            var source = url != null && ChainHttpTransport.IsAllowedEndpoint(url) && network.NodeLogResultCap is > 0
                ? client.CreateCustomRpc(url, network.NodeLogResultCap.Value) : null;
            var legacy = new DepositScanProvider(network.Id, "自定义 RPC", source, health, IsLegacy: true);
            providers.Insert(network.PreferManagedScanProviders ? providers.Count : 0, legacy);
        }
        return providers;
    }

    private static bool IsProjectKey(string? key) => key is { Length: 64 } && key.All(Uri.IsHexDigit);

    private static bool IsExplorerKey(string? key) => key is { Length: 34 } && key.All(char.IsAsciiLetterOrDigit);

    private string? Decrypt(string cipher, string? masterKey)
    {
        if (string.IsNullOrEmpty(cipher) || masterKey == null) return null;

        try
        {
            return encryptor.Decrypt(cipher.StartsWith('\u0001') ? cipher[1..] : cipher, masterKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or FormatException)
        {
            return null;
        }
    }
}

internal sealed record DepositScanProvider(Guid Id, string Name, IBscDepositScanSource? Source, DepositScanProviderHealth Health,
    bool IsLegacy = false);

/// <summary>Short process-local cooldown, shared across tokens without caching provider credentials.</summary>
public sealed class DepositScanProviderHealth : ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, (string Reason, DateTime RetryAt)> _failures = new();

    internal string? GetFailure(Guid providerId, DateTime now)
    {
        if (!_failures.TryGetValue(providerId, out var failure)) return null;
        if (failure.RetryAt > now) return failure.Reason;

        _failures.TryRemove(new KeyValuePair<Guid, (string Reason, DateTime RetryAt)>(providerId, failure));
        return null;
    }

    internal void MarkFailure(Guid providerId, string reason, DateTime now)
    {
        var seconds = reason switch
        {
            ProviderCredentialStatus.Invalid or ProviderCredentialStatus.PlanRequired or DepositScanFailure.Quota => 60,
            DepositScanFailure.Timeout or ProviderCredentialStatus.Unavailable => 20,
            _ => 0,
        };
        if (seconds == 0) return;

        foreach (var failure in _failures)
        {
            if (failure.Value.RetryAt <= now) _failures.TryRemove(failure);
        }
        _failures[providerId] = (reason, now.AddSeconds(seconds));
    }
}
