using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Settings;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;

namespace AbpAdmin.Collection.Chain;

/// <summary>The persisted key is authoritative once initialized. The deployment key remains the fallback for existing installations.</summary>
public class ChainKeyProvider(
    IConfiguration configuration,
    ISettingRepository settingRepository,
    ISettingDefinitionManager definitions,
    ISettingEncryptionService encryption) : ITransientDependency
{
    /// <summary>Reads the key from the database so a cached miss cannot change which key encrypts credentials.</summary>
    /// <returns>The effective key, or null when first-time initialization is still possible.</returns>
    /// <exception cref="InvalidOperationException">The stored key exists but cannot be decrypted.</exception>
    public virtual async Task<string?> GetAsync()
    {
        var stored = await settingRepository.FindAsync(CollectionSettings.Chain.MasterKey,
            GlobalSettingValueProvider.ProviderName, providerKey: null);
        if (stored != null)
        {
            var definition = await definitions.GetAsync(CollectionSettings.Chain.MasterKey);
            var decrypted = encryption.Decrypt(definition, stored.Value);
            if (decrypted is not { Length: 64 } || !IsHex(decrypted))
            {
                throw new InvalidOperationException("The saved Collection encryption key could not be decrypted.");
            }
            return decrypted;
        }

        var configured = configuration[CollectionSettings.Host.MasterKey];
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    private static bool IsHex(string key)
    {
        foreach (var character in key)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }
}
