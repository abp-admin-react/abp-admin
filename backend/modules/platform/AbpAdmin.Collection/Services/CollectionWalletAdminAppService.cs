using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Withdrawals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.Collection.Services;

public class CollectionNetworkSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public long ChainId { get; set; }
    public bool RpcConfigured { get; set; }
    public bool HasMnemonic { get; set; }
    public long? LatestChainHead { get; set; }
    public DateTime? ScanHeartbeatAt { get; set; }
    public decimal DepositFeeRate { get; set; }
    public string AutoCreditMaxAmount { get; set; } = default!;
}

public class CollectionWalletTokenDto
{
    public string Symbol { get; set; } = default!;
    public string? VaultAddress { get; set; }
    public string? VaultBalance { get; set; }
    public string? WithdrawBalance { get; set; }
}

public class CollectionWalletStatusDto
{
    public Guid NetworkId { get; set; }
    public string NetworkName { get; set; } = default!;
    public bool HasMnemonic { get; set; }
    public string? FuelAddress { get; set; }
    public string? FuelBnb { get; set; }
    public string? WithdrawAddress { get; set; }
    public bool HasWithdrawKey { get; set; }
    public bool IsWithdrawalNetwork { get; set; }
    public string? WithdrawalNetworkName { get; set; }
    public string? WithdrawBnb { get; set; }
    public bool BalancesAvailable { get; set; }
    public List<CollectionWalletTokenDto> Tokens { get; set; } = new();
}

internal static class CollectionNetworkQueries
{
    public static async Task<List<CollectionNetworkSummaryDto>> ListAsync(CollectionDbContext db)
    {
        var networks = await db.Networks.AsNoTracking().OrderBy(row => row.CreationTime).ThenBy(row => row.Id).ToListAsync();
        var rpcNetworks = (await db.ChainApiKeys.AsNoTracking()
            .Where(row => row.IsEnabled && row.Provider == Entities.ChainApiKeyProvider.RpcBsc)
            .Select(row => row.NetworkId).Distinct().ToListAsync()).ToHashSet();
        return networks.Select(row => new CollectionNetworkSummaryDto
        {
            Id = row.Id,
            Name = row.Name,
            ChainId = row.ChainId,
            RpcConfigured = !string.IsNullOrEmpty(row.RpcCipher) || rpcNetworks.Contains(row.Id),
            HasMnemonic = !string.IsNullOrEmpty(row.MnemonicCipher),
            LatestChainHead = row.LatestChainHead,
            ScanHeartbeatAt = row.ScanHeartbeatAt,
            DepositFeeRate = row.DepositFeeRate,
            AutoCreditMaxAmount = CollectionAmounts.Format(row.AutoCreditMaxAmount),
        }).ToList();
    }
}

/// <summary>按网络读取钱包角色与余额。任何密钥均不出现在响应中。</summary>
[Authorize]
public class CollectionWalletAdminAppService(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IStringEncryptionService encryptor,
    ChainKeyProvider chainKeyProvider,
    ChainGatewayFactory gatewayFactory) : ApplicationService
{
    public virtual async Task<List<CollectionNetworkSummaryDto>> GetNetworksAsync()
    {
        await CheckAccessAsync();
        return await CollectionNetworkQueries.ListAsync(await dbContextProvider.GetDbContextAsync());
    }

    public virtual async Task<CollectionWalletStatusDto> GetAsync(Guid id, bool includeBalances = false)
    {
        await CheckAccessAsync();
        var db = await dbContextProvider.GetDbContextAsync();
        var network = await db.Networks.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id)
                      ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        var tokens = await db.Tokens.AsNoTracking().Where(row => row.NetworkId == id)
            .OrderBy(row => row.Symbol).ToListAsync();
        var withdrawalNetwork = await WithdrawGuard.NetworkQuery(db).AsNoTracking().FirstOrDefaultAsync();
        var result = new CollectionWalletStatusDto
        {
            NetworkId = network.Id,
            NetworkName = network.Name,
            HasMnemonic = !string.IsNullOrEmpty(network.MnemonicCipher),
            WithdrawAddress = network.WithdrawVaultAddress,
            HasWithdrawKey = !string.IsNullOrEmpty(network.WithdrawVaultKeyCipher),
            IsWithdrawalNetwork = network.Id == withdrawalNetwork?.Id,
            WithdrawalNetworkName = withdrawalNetwork?.Name,
            Tokens = tokens.Select(token => new CollectionWalletTokenDto
            {
                Symbol = token.Symbol,
                VaultAddress = token.VaultAddress,
            }).ToList(),
        };

        if (result.HasMnemonic)
        {
            var masterKey = await chainKeyProvider.GetAsync();
            if (masterKey != null)
            {
                try
                {
                    using var words = HdKeyService.Unprotect(network.MnemonicCipher!, masterKey, encryptor);
                    result.FuelAddress = HdKeyService.DeriveAddress(words.Words, HdKeyService.GasAddressIndex);
                }
                catch (Exception)
                {
                    Logger.LogWarning("Unable to derive the fuel wallet for network {NetworkId}", id);
                }
            }
        }

        if (!includeBalances)
        {
            return result;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var gateway = await gatewayFactory.CreateAsync(db, network, network.NodeLogResultCap ?? 10000);
            if (result.FuelAddress != null)
            {
                result.FuelBnb = CollectionAmounts.Format(await gateway.GetBnbBalanceAsync(result.FuelAddress, timeout.Token));
            }
            if (result.WithdrawAddress != null)
            {
                result.WithdrawBnb = CollectionAmounts.Format(await gateway.GetBnbBalanceAsync(result.WithdrawAddress, timeout.Token));
            }
            foreach (var token in tokens)
            {
                var target = result.Tokens.Single(row => row.Symbol == token.Symbol);
                if (!string.IsNullOrEmpty(token.VaultAddress))
                {
                    target.VaultBalance = Scale(await gateway.GetTokenBalanceRawAsync(token.ContractAddress, token.VaultAddress, timeout.Token), token.Decimals);
                }
                if (result.WithdrawAddress != null)
                {
                    target.WithdrawBalance = Scale(await gateway.GetTokenBalanceRawAsync(token.ContractAddress, result.WithdrawAddress, timeout.Token), token.Decimals);
                }
            }
            result.BalancesAvailable = true;
        }
        catch (Exception)
        {
            Logger.LogWarning("Unable to read wallet balances for network {NetworkId}", id);
        }

        return result;
    }

    private static string? Scale(string value, int decimals)
    {
        var amount = DepositScanManager.TryScaleRaw(value, decimals, out var overflow);
        return overflow || amount == null ? null : CollectionAmounts.Format(amount.Value);
    }

    private async Task CheckAccessAsync()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
        if (!await AuthorizationService.IsGrantedAsync(CollectionPermissions.Network.Admin)
            && !await AuthorizationService.IsGrantedAsync(CollectionPermissions.Withdraw.Admin))
        {
            throw new AbpAuthorizationException();
        }
    }
}
