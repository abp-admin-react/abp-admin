using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace AbpAdmin.Collection.Settings;

public class ChainRuntimeSettings(ISettingRepository settings) : ITransientDependency
{
    internal const int MinBudgetSeconds = 60;
    internal const int MaxBudgetSeconds = 3600;
    internal const int MaxRecipients = 48; // Keep one atomic settings value within AbpSettings.Value (2048 characters).

    internal static bool IsValidBudget(int seconds) => seconds is >= MinBudgetSeconds and <= MaxBudgetSeconds;

    /// <summary>按数据库提交顺序读取配置。空值使用部署默认值，空收件人名单只写日志。</summary>
    internal async Task<ChainRuntimeSettingsDto?> ReadAsync()
    {
        var value = (await settings.FindAsync(CollectionSettings.Chain.Runtime,
            GlobalSettingValueProvider.ProviderName, providerKey: null))?.Value;
        return string.IsNullOrEmpty(value) ? null : JsonSerializer.Deserialize<ChainRuntimeSettingsDto>(value);
    }
}
