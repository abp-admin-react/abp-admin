using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Account;

/// <summary>
/// Magic Link 登录 URL 构造器。前端地址取 App:SpaUrl（{0} 为租户名占位符，
/// 与 AbpAdminHttpApiHostModule.ConfigureUrls 映射进 AppUrlOptions 的值同源），缺失时回落 App:SelfUrl。
/// host 邮件没有租户子域名，去掉 "{0}." 段得到无子域名地址；租户邮件替换 {0} 并追加 tenant 参数，
/// 登录页据此自解析租户上下文（链接自包含，不依赖打开邮件时的任何本地状态）。
/// </summary>
public class PasswordlessMagicLinkUrlBuilder : ITransientDependency
{
    private readonly IConfiguration _configuration;

    public PasswordlessMagicLinkUrlBuilder(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>构造落在 SPA 登录页的绝对 Magic Link 地址。</summary>
    public virtual string BuildLoginUrl(string magicLinkToken, string email, string? tenantName)
    {
        var spaUrl = _configuration["App:SpaUrl"];
        if (spaUrl.IsNullOrWhiteSpace())
        {
            spaUrl = _configuration["App:SelfUrl"];
        }

        if (spaUrl.IsNullOrWhiteSpace())
        {
            throw new AbpException("App:SpaUrl / App:SelfUrl 均未配置，无法构造 Magic Link 登录地址");
        }

        return Format(spaUrl, magicLinkToken, email, tenantName);
    }

    /// <summary>纯字符串组装，单元测试直接覆盖，不依赖 IConfiguration。</summary>
    public static string Format(string spaUrlTemplate, string magicLinkToken, string email, string? tenantName)
    {
        // "{0}.localhost:8000" 的 host 形态：占位符与其后的 "." 一并移除；
        // 不含占位符的固定域名保持原样
        var baseUrl = tenantName.IsNullOrWhiteSpace()
            ? Regex.Replace(spaUrlTemplate, @"\{0\}\.?", string.Empty)
            : spaUrlTemplate.Replace("{0}", Uri.EscapeDataString(tenantName));

        return $"{baseUrl.TrimEnd('/')}/user/login" +
               $"?magicLinkToken={Uri.EscapeDataString(magicLinkToken)}" +
               $"&email={Uri.EscapeDataString(email)}" +
               (tenantName.IsNullOrWhiteSpace() ? string.Empty : $"&tenant={Uri.EscapeDataString(tenantName)}");
    }
}
