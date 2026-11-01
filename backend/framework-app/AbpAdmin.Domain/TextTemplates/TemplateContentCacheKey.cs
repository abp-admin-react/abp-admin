using System;

namespace AbpAdmin.TextTemplates;

[Serializable]
public class TemplateContentCacheKey
{
    public Guid? TenantId { get; set; }

    public string Name { get; set; } = default!;

    public string? CultureName { get; set; }

    /// <summary>
    /// (TenantId, Name) 维度的版本值：内容缓存 key 拼进版本，保存/恢复时替换版本号，
    /// 该模板当前租户下所有文化的条目一次性作废（旧条目靠 TTL 消亡）。
    /// null 表示旧版无版本 key，仅兼容缓存迁移期，新代码不再产生。
    /// </summary>
    public string? Version { get; set; }

    public override string ToString()
    {
        return $"{TenantId?.ToString() ?? "host"}:{Name}:{CultureName ?? "-"}:v{Version ?? "legacy"}";
    }
}
