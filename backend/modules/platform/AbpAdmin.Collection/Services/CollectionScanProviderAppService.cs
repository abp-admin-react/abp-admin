using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Localization;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

public record ScanProviderNetworkDto(Guid NetworkId, int? LogResultCap);

public record ScanProviderDto(Guid Id, string Provider, string? Label, bool IsEnabled, int SortOrder,
    string KeyMasked, string? PlatformKeyMasked, string? Group, IReadOnlyList<ScanProviderNetworkDto> Networks);

public record ScanProvidersDto(IReadOnlyList<ScanProviderDto> Providers, bool LegacyBalancePending, bool LegacyRpcActive);

public class SaveScanProviderInput
{
    public string Provider { get; set; } = "";
    public string? Label { get; set; }
    public bool IsEnabled { get; set; }
    public string? ApiKey { get; set; }
    public string? PlatformAuthKey { get; set; }
    public bool ClearPlatformAuthKey { get; set; }
    public string? Group { get; set; }
    public ScanProviderNetworkDto[] Networks { get; set; } = [];
}

public record ReorderScanProvidersInput(Guid[] Ids);
public record TestScanProviderInput(Guid? Id, string Provider, string? ApiKey, Guid NetworkId);
public record TestScanBalanceInput(Guid? Id, string? PlatformAuthKey, string? Group);
public record ScanProviderTestDto(bool Passed, string Message);
public record ScanDashboardBalanceDto(string Status, string? Credits, string? BalanceUsd, DateTime? CheckedAt, string? Label);

[Authorize]
public class CollectionScanProviderAppService(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IStringEncryptionService encryptor,
    ChainKeyProvider chainKeys,
    BscDepositProviderClient providerClient,
    AnkrPlatformBalanceClient balanceClient,
    IStringLocalizer<CollectionResource> localizer) : ApplicationService
{
    private const string Route = "api/app/collection-scan-provider";
    private const string InvalidConfiguration = "AbpAdmin.Collection:InvalidScanProviderConfiguration";

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpGet(Route)]
    public virtual async Task<ScanProvidersDto> GetListAsync(CancellationToken cancellationToken = default)
    {
        AssertHost();
        var db = await dbContextProvider.GetDbContextAsync();
        var rows = await OrderedProviders(db).AsNoTracking().ToListAsync(cancellationToken);
        var legacy = await BscNetworks(db).AsNoTracking().Select(x => new
        {
            x.ScanBillingClaimPending,
            x.LegacyDepositRpcCipher,
        }).SingleOrDefaultAsync(cancellationToken);
        var masterKey = await chainKeys.GetAsync();
        return new ScanProvidersDto(rows.Select(x => ToDto(x, masterKey)).ToList(),
            legacy?.ScanBillingClaimPending == true, legacy?.LegacyDepositRpcCipher != null);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPost(Route)]
    public virtual Task<ScanProviderDto> CreateAsync([FromBody] SaveScanProviderInput input,
        CancellationToken cancellationToken = default) => SaveAsync(null, input, cancellationToken);

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPut(Route + "/{id:guid}")]
    public virtual Task<ScanProviderDto> UpdateAsync(Guid id, [FromBody] SaveScanProviderInput input,
        CancellationToken cancellationToken = default) => SaveAsync(id, input, cancellationToken);

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpDelete(Route + "/{id:guid}")]
    public virtual async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await LockConfigurationAsync(cancellationToken);
        db.ScanProviders.Remove(await RequireProviderAsync(db, id, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpPost(Route + "/reorder")]
    public virtual async Task ReorderAsync([FromBody] ReorderScanProvidersInput input,
        CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await LockConfigurationAsync(cancellationToken);
        var rows = await OrderedProviders(db).ToDictionaryAsync(x => x.Id, cancellationToken);
        var ids = input.Ids;
        if (ids == null || ids.Length != rows.Count || ids.Distinct().Count() != ids.Length || ids.Any(id => !rows.ContainsKey(id)))
        {
            throw new BusinessException("AbpAdmin.Collection:ScanProviderOrderChanged");
        }

        for (var i = 0; i < ids.Length; i++)
        {
            rows[ids[i]].SortOrder = i;
        }

        await PreferManagedScanProvidersAsync(db, rows.Values.Where(x => x.IsEnabled).SelectMany(x => x.Networks), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [UnitOfWork(isTransactional: false)]
    [HttpPost(Route + "/test")]
    public virtual async Task<ScanProviderTestDto> TestAsync([FromBody] TestScanProviderInput input,
        CancellationToken cancellationToken = default)
    {
        AssertHost();
        var db = await dbContextProvider.GetDbContextAsync();
        if (!await BscNetworks(db).AnyAsync(x => x.Id == input.NetworkId, cancellationToken))
        {
            return new ScanProviderTestDto(false, ScanText("ScanProviderNetworkUnavailable"));
        }

        var provider = ParseProvider(input.Provider);
        var key = Normalize(input.ApiKey);
        if (input.Id is { } id && key == null)
        {
            var row = await RequireProviderAsync(db, id, cancellationToken);
            if (row.Provider == provider)
            {
                key = ReadKey(row.KeyCipher, await chainKeys.GetAsync());
            }
        }

        if (key == null || !IsApiKey(provider, key))
        {
            return new ScanProviderTestDto(false, ScanText("NodeProbeInvalidInput"));
        }

        var status = await providerClient.ProbeAsync(provider, key, cancellationToken);
        var message = status switch
        {
            "available" => "ScanProviderProbeAvailable",
            "invalid" => "ScanProviderProbeInvalid",
            "planRequired" => "ScanProviderProbePlanRequired",
            "quota" => "ScanProviderProbeQuota",
            _ => "ScanProviderProbeUnavailable",
        };
        return new ScanProviderTestDto(status == "available", ScanText(message));
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpGet(Route + "/{id:guid}/balance")]
    public virtual async Task<AnkrBalanceDto> GetBalanceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var row = await RequireProviderAsync(await dbContextProvider.GetDbContextAsync(), id, cancellationToken);
        return await ReadBalanceAsync(row.PlatformAuthKeyCipher, row.Group, cancellationToken);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [UnitOfWork(isTransactional: false)]
    [HttpPost(Route + "/test-balance")]
    public virtual async Task<AnkrBalanceDto> TestBalanceAsync([FromBody] TestScanBalanceInput input,
        CancellationToken cancellationToken = default)
    {
        AssertHost();
        var key = Normalize(input.PlatformAuthKey);
        var group = Normalize(input.Group);
        ValidatePlatformSettings(key, group);
        if (key == null && input.Id is { } id)
        {
            var row = await RequireProviderAsync(await dbContextProvider.GetDbContextAsync(), id, cancellationToken);
            return await ReadBalanceAsync(row.PlatformAuthKeyCipher, group, cancellationToken);
        }

        return key == null ? new AnkrBalanceDto("notConfigured", null, null, null)
            : await QueryBalanceAsync(key, group, cancellationToken);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpPost(Route + "/{id:guid}/claim-legacy-balance")]
    public virtual async Task ClaimLegacyBalanceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await LockConfigurationAsync(cancellationToken);
        var row = await RequireProviderAsync(db, id, cancellationToken);
        var network = await BscNetworks(db).SingleOrDefaultAsync(cancellationToken);
        if (network is not { ScanBillingClaimPending: true, AnkrPlatformAuthKeyCipher: not null }
            || row.Provider != ChainApiKeyProvider.RpcBsc || row.PlatformAuthKeyCipher != null
            || row.Networks.All(x => x.NetworkId != network.Id))
        {
            throw new BusinessException(InvalidConfiguration);
        }

        row.PlatformAuthKeyCipher = network.AnkrPlatformAuthKeyCipher;
        row.Group = network.AnkrGroup;
        network.ScanBillingClaimPending = false;
        await db.SaveChangesAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
    }

    [HttpGet(Route + "/dashboard-balance")]
    public virtual async Task<ScanDashboardBalanceDto> GetDashboardBalanceAsync(CancellationToken cancellationToken = default)
    {
        AssertHost();
        if (!await AuthorizationService.IsGrantedAsync(CollectionPermissions.Collections.Default)
            && !await AuthorizationService.IsGrantedAsync(CollectionPermissions.Network.Admin))
        {
            throw new AbpAuthorizationException();
        }

        var db = await dbContextProvider.GetDbContextAsync();
        var bsc = BscNetworks(db);
        var row = await db.ScanProviders.AsNoTracking().Where(x => x.IsEnabled && x.Provider == ChainApiKeyProvider.RpcBsc
                && x.PlatformAuthKeyCipher != null && x.Networks.Any(n => bsc.Any(chain => chain.Id == n.NetworkId)))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.PlatformAuthKeyCipher, x.Group, x.Label })
            .FirstOrDefaultAsync(cancellationToken);
        var legacy = row == null ? await bsc.Where(x => x.ScanBillingClaimPending).AsNoTracking()
            .Select(x => new { x.AnkrPlatformAuthKeyCipher, x.AnkrGroup })
            .SingleOrDefaultAsync(cancellationToken) : null;
        var balance = await ReadBalanceAsync(row?.PlatformAuthKeyCipher ?? legacy?.AnkrPlatformAuthKeyCipher,
            row?.Group ?? legacy?.AnkrGroup, cancellationToken);
        var label = row == null ? (legacy == null ? null : ScanText("ScanProviderLegacyBalance")) : row.Label ?? "Ankr";
        return new ScanDashboardBalanceDto(balance.Status, balance.Credits, balance.BalanceUsd, balance.CheckedAt, label);
    }

    private async Task<ScanProviderDto> SaveAsync(Guid? id, SaveScanProviderInput input, CancellationToken cancellationToken)
    {
        AssertHost();
        var provider = ParseProvider(input.Provider);
        var key = Normalize(input.ApiKey);
        var platformKey = Normalize(input.PlatformAuthKey);
        var group = Normalize(input.Group);
        var label = Normalize(input.Label);
        ValidatePlatformSettings(platformKey, group);
        if ((key != null && !IsApiKey(provider, key)) || (key == null && id == null))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidApiKey);
        }

        if (label is { Length: > 64 } || (platformKey != null && input.ClearPlatformAuthKey)
            || (provider != ChainApiKeyProvider.RpcBsc && (platformKey != null || group != null || input.ClearPlatformAuthKey)))
        {
            throw new BusinessException(InvalidConfiguration);
        }

        var networks = input.Networks;
        if (networks == null || networks.Length == 0 || networks.Any(x => x == null || x.NetworkId == Guid.Empty
                || x.LogResultCap is < 100 || (x.LogResultCap.HasValue && provider != ChainApiKeyProvider.RpcBsc)))
        {
            throw new BusinessException(InvalidConfiguration);
        }

        var networkIds = networks.Select(x => x.NetworkId).Distinct().ToArray();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await LockConfigurationAsync(cancellationToken);
        if (networkIds.Length != networks.Length || await db.Networks.CountAsync(x => networkIds.Contains(x.Id), cancellationToken) != networkIds.Length)
        {
            throw new BusinessException(InvalidConfiguration);
        }

        var row = id is { } existingId ? await RequireProviderAsync(db, existingId, cancellationToken)
            : new ColScanProvider(GuidGenerator.Create()) { Provider = provider };
        if (row.Provider != provider)
        {
            throw new BusinessException(InvalidConfiguration);
        }

        var hash = key == null ? row.KeyHash : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        if (await db.ScanProviders.AnyAsync(x => x.Id != row.Id && x.Provider == provider && x.KeyHash == hash
                && x.Networks.Any(n => networkIds.Contains(n.NetworkId)), cancellationToken))
        {
            throw new BusinessException(CollectionErrorCodes.ApiKeyExists);
        }

        var masterKey = await chainKeys.GetAsync();
        if ((key != null || platformKey != null) && masterKey == null)
        {
            throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
        }

        if (key != null)
        {
            row.KeyHash = hash;
            row.KeyCipher = HdKeyService.Protect(key, masterKey!, encryptor);
        }

        if (platformKey != null)
        {
            row.PlatformAuthKeyCipher = HdKeyService.Protect(platformKey, masterKey!, encryptor);
        }
        else if (input.ClearPlatformAuthKey)
        {
            row.PlatformAuthKeyCipher = null;
        }

        row.Group = row.PlatformAuthKeyCipher == null ? null : group;
        row.Label = label;
        row.IsEnabled = input.IsEnabled;
        SetNetworks(row, networks);
        if (id == null)
        {
            row.SortOrder = (await db.ScanProviders.MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
            db.ScanProviders.Add(row);
        }

        if (row.IsEnabled)
        {
            await PreferManagedScanProvidersAsync(db, row.Networks, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return ToDto(row, masterKey);
    }

    private static void SetNetworks(ColScanProvider row, IReadOnlyList<ScanProviderNetworkDto> networks)
    {
        var selected = networks.ToDictionary(x => x.NetworkId);
        foreach (var old in row.Networks.Where(x => !selected.ContainsKey(x.NetworkId)).ToArray())
        {
            row.Networks.Remove(old);
        }

        foreach (var current in row.Networks)
        {
            current.LogResultCap = selected[current.NetworkId].LogResultCap;
            selected.Remove(current.NetworkId);
        }

        foreach (var added in selected.Values)
        {
            row.Networks.Add(new ColScanProviderNetwork { ProviderId = row.Id, NetworkId = added.NetworkId, LogResultCap = added.LogResultCap });
        }
    }

    private async Task<CollectionDbContext> LockConfigurationAsync(CancellationToken ct)
    {
        var db = await dbContextProvider.GetDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('Collection:ScanProviders'))", ct);
        return db;
    }

    /// <summary>Starts a managed scan trial without discarding the only proven RPC in the configuration transaction.</summary>
    private static async Task PreferManagedScanProvidersAsync(CollectionDbContext db, IEnumerable<ColScanProviderNetwork> networks,
        CancellationToken ct)
    {
        var ids = networks.Select(x => x.NetworkId).Distinct().ToArray();
        var bsc = await BscNetworks(db).SingleOrDefaultAsync(n => n.LegacyDepositRpcCipher != null && ids.Contains(n.Id), ct);
        if (bsc != null)
        {
            bsc.PreferManagedScanProviders = true;
        }
    }

    private static IQueryable<ColScanProvider> OrderedProviders(CollectionDbContext db) =>
        db.ScanProviders.Include(x => x.Networks).OrderBy(x => x.SortOrder).ThenBy(x => x.Id);

    private static IQueryable<ColNetwork> BscNetworks(CollectionDbContext db) =>
        db.Networks.Where(x => x.Name == CollectionConsts.BscNetworkName && x.ChainId == CollectionConsts.BscMainnetChainId);

    private static async Task<ColScanProvider> RequireProviderAsync(CollectionDbContext db, Guid id, CancellationToken ct) =>
        await db.ScanProviders.Include(x => x.Networks).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new EntityNotFoundException(typeof(ColScanProvider), id);

    private async Task<AnkrBalanceDto> ReadBalanceAsync(string? cipher, string? group, CancellationToken ct)
    {
        if (cipher == null)
        {
            return new AnkrBalanceDto("notConfigured", null, null, null);
        }

        var key = ReadKey(cipher, await chainKeys.GetAsync());
        return key == null ? new AnkrBalanceDto("unavailable", null, null, Clock.Now)
            : await QueryBalanceAsync(key, group, ct);
    }

    private async Task<AnkrBalanceDto> QueryBalanceAsync(string key, string? group, CancellationToken ct)
    {
        var balance = await balanceClient.ReadAsync(key, group, ct);
        return balance is { } result ? new AnkrBalanceDto("available", result.Credits, result.BalanceUsd, Clock.Now)
            : new AnkrBalanceDto("unavailable", null, null, Clock.Now);
    }

    private ScanProviderDto ToDto(ColScanProvider row, string? masterKey) => new(row.Id,
        row.Provider == ChainApiKeyProvider.RpcBsc ? "ankr" : "etherscan", row.Label, row.IsEnabled, row.SortOrder,
        Mask(ReadKey(row.KeyCipher, masterKey)), row.PlatformAuthKeyCipher == null ? null : Mask(ReadKey(row.PlatformAuthKeyCipher, masterKey)),
        row.Group, row.Networks.OrderBy(x => x.NetworkId).Select(x => new ScanProviderNetworkDto(x.NetworkId, x.LogResultCap)).ToList());

    private string? ReadKey(string? cipher, string? masterKey)
    {
        if (cipher == null || masterKey == null)
        {
            return null;
        }

        try
        {
            return HdKeyService.UnprotectPlain(cipher, masterKey, encryptor);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Mask(string? value) => value is { Length: > 4 } ? "****" + value[^4..] : "****";
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ChainApiKeyProvider ParseProvider(string provider) => provider switch
    {
        "ankr" => ChainApiKeyProvider.RpcBsc,
        "etherscan" => ChainApiKeyProvider.EtherscanV2,
        _ => throw new BusinessException(InvalidConfiguration),
    };

    private static bool IsApiKey(ChainApiKeyProvider provider, string value) => provider switch
    {
        ChainApiKeyProvider.RpcBsc => Regex.IsMatch(value, "\\A[0-9a-fA-F]{64}\\z"),
        ChainApiKeyProvider.EtherscanV2 => Regex.IsMatch(value, "\\A[A-Za-z0-9]{34}\\z"),
        _ => false,
    };

    private static void ValidatePlatformSettings(string? platformKey, string? group)
    {
        if ((platformKey != null && (platformKey.Length > 2048 || !Regex.IsMatch(platformKey, @"\A[A-Za-z0-9._~+/-]+=*\z")))
            || (group != null && (group.Length > 128 || group.Any(char.IsWhiteSpace))))
        {
            throw new BusinessException(InvalidConfiguration);
        }
    }

    private string ScanText(string name) => localizer[name];

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}
