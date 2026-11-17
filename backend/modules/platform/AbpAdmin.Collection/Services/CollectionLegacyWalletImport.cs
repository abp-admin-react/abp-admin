using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

public class ImportLegacyWalletInput
{
    [Required, StringLength(250)]
    public string Mnemonic { get; set; } = string.Empty;

    public int AddressIndexHighWaterMark { get; set; }

    public List<ImportLegacyWalletAddressInput> Addresses { get; set; } = new();
}

public class ImportLegacyWalletAddressInput
{
    public Guid? UserId { get; set; }
    public string Address { get; set; } = string.Empty;
    public int AddressIndex { get; set; }

    [Required]
    public bool? IsActive { get; set; }
}

public class ImportLegacyWalletResultDto
{
    public int ImportedAddressCount { get; set; }
    public int RetiredAddressCount { get; set; }
    public int AddressIndexHighWaterMark { get; set; }
    public int? NextAddressIndex { get; set; }
}

public partial class CollectionNetworkAdminAppService
{
    private const int MaxLegacyWalletAddresses = 20000;

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPost("api/app/collection-network-admin/import-legacy-wallet")]
    public virtual async Task<ImportLegacyWalletResultDto> ImportLegacyWalletAsync(
        [FromBody] ImportLegacyWalletInput input, [FromQuery] Guid? networkId = null,
        CancellationToken cancellationToken = default)
    {
        AssertHost();
        var (mnemonic, rows) = ValidateLegacyWallet(input, cancellationToken);

        await using var mnemonicLock = await AcquireLegacyWalletLockAsync(CollectionConsts.MnemonicLockKey, cancellationToken);
        await using var scanLock = await AcquireLegacyWalletLockAsync(CollectionConsts.ScannerLockKey, cancellationToken);
        await using var sweepLock = await AcquireLegacyWalletLockAsync(CollectionConsts.SweepLockKey, cancellationToken);
        if (await SettingProvider.IsTrueAsync(CollectionSettings.Chain.ScanEnabled)
            || await SettingProvider.IsTrueAsync(CollectionSettings.Chain.SweepEnabled))
        {
            throw new BusinessException(CollectionErrorCodes.LegacyWalletWorkersRunning);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireAnkrNetworkAsync(db, networkId);
        if (!network.IsBscMainnet)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        }

        var masterKey = await RequireMasterKeyAsync();
        var existing = db.DepositAddresses.Where(address => address.NetworkId == network.Id);
        var highWaterMark = input.AddressIndexHighWaterMark;
        var highestIndex = highWaterMark;
        if (network.LegacyAddressIndexHighWaterMark.HasValue)
        {
            var imported = await existing.AsNoTracking().Where(address => address.AddressIndex <= highWaterMark)
                .ToDictionaryAsync(address => address.AddressIndex, cancellationToken);
            if (network.LegacyAddressIndexHighWaterMark != highWaterMark
                || network.MnemonicCipher == null
                || imported.Count != rows.Count
                || rows.Any(row => !imported.TryGetValue(row.AddressIndex, out var saved)
                    || saved.UserId != (row.UserId ?? Guid.Empty)
                    || saved.IsRetired != (row.IsActive != true)
                    || !string.Equals(saved.Address, row.Address, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletTargetOccupied);
            }

            using var savedMnemonic = HdKeyService.Unprotect(network.MnemonicCipher, masterKey, _encryptor);
            if (!string.Equals(savedMnemonic.Words, mnemonic, StringComparison.Ordinal))
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletTargetOccupied);
            }

            highestIndex = Math.Max(highWaterMark,
                await existing.MaxAsync(address => (int?)address.AddressIndex, cancellationToken) ?? highWaterMark);
        }
        else
        {
            if (!string.IsNullOrEmpty(network.MnemonicCipher)
                || await existing.AnyAsync(cancellationToken)
                || await db.Tokens.AnyAsync(token => token.NetworkId == network.Id && token.LastScannedBlock != null,
                    cancellationToken)
                || await db.SweepTasks.AnyAsync(task => task.NetworkId == network.Id, cancellationToken))
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletTargetOccupied);
            }

            var users = rows.Where(row => row.IsActive == true).Select(row => row.UserId!.Value).ToArray();
            var identityUsers = await _users.GetQueryableAsync();
            if (await identityUsers.CountAsync(user => user.TenantId == null && users.Contains(user.Id), cancellationToken)
                != users.Length)
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletUnknownUser);
            }

            var now = Clock.Now;
            db.DepositAddresses.AddRange(rows.Select(row => new ColDepositAddress(GuidGenerator.Create())
            {
                UserId = row.UserId ?? Guid.Empty,
                NetworkId = network.Id,
                Address = row.Address,
                AddressIndex = row.AddressIndex,
                IsRetired = row.IsActive != true,
                CreationTime = now,
            }));
            network.MnemonicCipher = HdKeyService.Protect(mnemonic, masterKey, _encryptor);
            network.LegacyAddressIndexHighWaterMark = highWaterMark;
            await db.SaveChangesAsync(cancellationToken);
        }

        await uow.CompleteAsync(cancellationToken);
        var activeCount = rows.Count(row => row.IsActive == true);
        return new ImportLegacyWalletResultDto
        {
            ImportedAddressCount = activeCount,
            RetiredAddressCount = rows.Count - activeCount,
            AddressIndexHighWaterMark = highWaterMark,
            NextAddressIndex = highestIndex < int.MaxValue ? highestIndex + 1 : null,
        };
    }

    private async Task<Volo.Abp.DistributedLocking.IAbpDistributedLockHandle> AcquireLegacyWalletLockAsync(
        string key, CancellationToken cancellationToken)
    {
        return await _distributedLock.TryAcquireAsync(key, TimeSpan.FromSeconds(5), cancellationToken)
            ?? throw new BusinessException(CollectionErrorCodes.TryAgain);
    }

    private static (string Mnemonic, List<ImportLegacyWalletAddressInput> Rows) ValidateLegacyWallet(
        ImportLegacyWalletInput input, CancellationToken cancellationToken)
    {
        if (input.Addresses is not { Count: > 0 and <= MaxLegacyWalletAddresses }
            || input.AddressIndexHighWaterMark is < HdKeyService.FirstUserAddressIndex or >= int.MaxValue)
        {
            throw new BusinessException(CollectionErrorCodes.LegacyWalletInvalidManifest);
        }

        if (string.IsNullOrWhiteSpace(input.Mnemonic) || input.Mnemonic.Length > 250)
        {
            throw new BusinessException(CollectionErrorCodes.LegacyWalletMnemonicInvalid);
        }
        var mnemonic = Regex.Replace(input.Mnemonic.Trim(), @"\s+", " ");
        Nethereum.HdWallet.Wallet wallet;
        try
        {
            if (!new NBitcoin.Mnemonic(mnemonic, NBitcoin.Wordlist.English).IsValidChecksum)
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletMnemonicInvalid);
            }
            wallet = HdKeyService.MakeWallet(mnemonic);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new BusinessException(CollectionErrorCodes.LegacyWalletMnemonicInvalid);
        }

        var rows = new List<ImportLegacyWalletAddressInput>(input.Addresses.Count);
        var indexes = new HashSet<int>();
        var activeUsers = new HashSet<Guid>();
        foreach (var row in input.Addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row == null || !row.IsActive.HasValue || row.UserId == Guid.Empty
                || row.AddressIndex < HdKeyService.FirstUserAddressIndex
                || row.AddressIndex > input.AddressIndexHighWaterMark
                || !indexes.Add(row.AddressIndex)
                || row.Address is not { Length: 42 }
                || !Regex.IsMatch(row.Address, "^0x[0-9a-fA-F]{40}$")
                || (row.IsActive.Value && (!row.UserId.HasValue || !activeUsers.Add(row.UserId.Value))))
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletInvalidManifest);
            }
            var derived = wallet.GetAccount(row.AddressIndex).Address;
            if (!string.Equals(derived, row.Address, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(CollectionErrorCodes.LegacyWalletAddressMismatch)
                    .WithData("Index", row.AddressIndex);
            }
            rows.Add(new ImportLegacyWalletAddressInput
            {
                AddressIndex = row.AddressIndex,
                Address = derived,
                UserId = row.UserId,
                IsActive = row.IsActive,
            });
        }

        return (mnemonic, rows);
    }
}
