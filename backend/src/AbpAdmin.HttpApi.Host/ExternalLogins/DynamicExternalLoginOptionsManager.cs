using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using AspNet.Security.OAuth.GitHub;
using AspNet.Security.OAuth.Weixin;
using Microsoft.Extensions.Options;
using Volo.Abp.Options;

namespace AbpAdmin.ExternalLogins;

/// <summary>
/// T2.7 / T4.5 外部登录每租户动态配置：按当前租户从 Setting 读取 ClientId / ClientSecret。
/// </summary>
public class DynamicExternalLoginOptionsManager<TOptions> : AbpDynamicOptionsManager<TOptions>
    where TOptions : OAuthOptions, new()
{
    private readonly Account.ExternalLoginSettingsManager _externalLoginSettingsManager;

    public DynamicExternalLoginOptionsManager(
        IOptionsFactory<TOptions> factory,
        Account.ExternalLoginSettingsManager externalLoginSettingsManager)
        : base(factory)
    {
        _externalLoginSettingsManager = externalLoginSettingsManager;
    }

    protected override async Task OverrideOptionsAsync(string name, TOptions options)
    {
        var scheme = ResolveScheme();
        var settings = await _externalLoginSettingsManager.GetProviderSettingsAsync(scheme);

        if (!settings.Enabled)
        {
            options.ClientId = string.Empty;
            options.ClientSecret = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.ClientId))
        {
            options.ClientId = settings.ClientId;
        }

        if (!string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            options.ClientSecret = settings.ClientSecret;
        }
    }

    private static string ResolveScheme()
    {
        if (typeof(TOptions) == typeof(GitHubAuthenticationOptions))
        {
            return "GitHub";
        }

        if (typeof(TOptions) == typeof(MicrosoftAccountOptions))
        {
            return "Microsoft";
        }

        if (typeof(TOptions) == typeof(WeixinAuthenticationOptions))
        {
            return "Weixin";
        }

        if (typeof(TOptions) == typeof(GoogleOptions))
        {
            return "Google";
        }

        return typeof(TOptions).Name.Replace("AuthenticationOptions", string.Empty)
            .Replace("Options", string.Empty);
    }
}
