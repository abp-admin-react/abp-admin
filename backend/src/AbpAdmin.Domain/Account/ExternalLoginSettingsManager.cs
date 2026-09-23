using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace AbpAdmin.Account;

/// <summary>
/// T2.7 外部登录每租户配置的读取核心（Domain 层，Application.Tests 可直接覆盖租户隔离）。
/// 设置命名约定（定死）：AbpAdmin.Account.ExternalLogin.{Scheme}.Enabled / .ClientId / .ClientSecret。
/// ClientSecret 的 SettingDefinition 标记了 isEncrypted，ISettingProvider 读出的是解密后的明文。
/// </summary>
public class ExternalLoginSettingsManager : DomainService
{
    /// <summary>外部登录设置键前缀（键名拼接统一走 BuildSettingName，不再三处内插）。</summary>
    public const string SettingNamePrefix = "AbpAdmin.Account.ExternalLogin.";

    private readonly ISettingProvider _settingProvider;

    public ExternalLoginSettingsManager(ISettingProvider settingProvider)
    {
        _settingProvider = settingProvider;
    }

    /// <summary>
    /// 按当前租户读取指定认证方案（GitHub / Microsoft）的外部登录配置。
    /// </summary>
    public virtual async Task<ExternalLoginProviderSettings> GetProviderSettingsAsync(string scheme)
    {
        var enabled = await _settingProvider.GetOrNullAsync(BuildSettingName(scheme, "Enabled"));
        var clientId = await _settingProvider.GetOrNullAsync(BuildSettingName(scheme, "ClientId"));
        var clientSecret = await _settingProvider.GetOrNullAsync(BuildSettingName(scheme, "ClientSecret"));

        return new ExternalLoginProviderSettings(
            enabled?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            clientId,
            clientSecret);
    }

    /// <summary>构造外部登录设置键：AbpAdmin.Account.ExternalLogin.{scheme}.{suffix}。</summary>
    public static string BuildSettingName(string scheme, string suffix)
    {
        return $"{SettingNamePrefix}{scheme}.{suffix}";
    }
}

/// <summary>
/// 一个外部登录方案在当前租户下的有效配置。
/// </summary>
public class ExternalLoginProviderSettings
{
    public bool Enabled { get; }

    public string? ClientId { get; }

    public string? ClientSecret { get; }

    public ExternalLoginProviderSettings(bool enabled, string? clientId, string? clientSecret)
    {
        Enabled = enabled;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }
}
