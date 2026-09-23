using System;

namespace AbpAdmin.TextTemplates;

public class AbpAdminTextTemplateOptions
{
    public const string SectionName = "TextTemplating";

    /// <summary>
    /// 模板内容缓存滑动过期时间，默认 1 小时
    /// </summary>
    public TimeSpan ContentCacheSlidingExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 是否将静态模板定义同步到数据库（作为基线），默认 true
    /// </summary>
    public bool SaveStaticTemplatesToDatabase { get; set; } = true;
}
