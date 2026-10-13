using System;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 模块配置选项。
/// 从 appsettings.json 的 "Gdpr" 节绑定。
/// </summary>
public class AbpAdminGdprOptions
{
    public const string SectionName = "Gdpr";

    /// <summary>
    /// 同一用户两次请求之间的最小间隔。默认 1 天。
    /// </summary>
    public TimeSpan RequestTimeInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// 从发起请求到数据准备就绪所需的等待时间。默认 60 分钟。
    /// </summary>
    public TimeSpan MinutesForDataPreparation { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// 导出数据与请求记录的保留天数（数据最小化，法域相关的合规参数）。默认 30 天。
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 下载令牌有效期（安全参数）。默认 60 分钟。
    /// </summary>
    public TimeSpan DownloadTokenExpiration { get; set; } = TimeSpan.FromMinutes(60);
}
