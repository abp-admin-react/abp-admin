using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Localization;

/// <summary>
/// 本地化文本覆盖实体。租户级数据，实现 IMultiTenant。
/// 读取时先查当前租户覆盖行，未命中再查 host 行（TenantId == null）。
/// </summary>
public class LanguageText : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>
    /// 资源名称（如 AbpAdminResource）。
    /// </summary>
    public virtual string ResourceName { get; protected set; }

    /// <summary>
    /// 文化名称。
    /// </summary>
    public virtual string CultureName { get; protected set; }

    /// <summary>
    /// 本地化 key。
    /// </summary>
    public virtual string Name { get; protected set; }

    /// <summary>
    /// 本地化值。空字符串是合法状态（显式空串覆盖 = "未翻译"标记；
    /// 列表的 OnlyEmpty 过滤按合并后的生效值判断，本层的空串覆盖只是其中一种来源），
    /// 但必须经 <see cref="SetValue"/> 写入（与 TextTemplateContent.SetContent 风格一致）。
    /// </summary>
    public virtual string Value { get; protected set; }

    protected LanguageText()
    {
        ResourceName = string.Empty;
        CultureName = string.Empty;
        Name = string.Empty;
        Value = string.Empty;
    }

    public LanguageText(
        Guid id,
        Guid? tenantId,
        string resourceName,
        string cultureName,
        string name,
        string value)
        : base(id)
    {
        TenantId = tenantId;
        ResourceName = resourceName;
        CultureName = cultureName;
        Name = name;
        Value = value;
    }

    public virtual void SetValue(string value)
    {
        Value = Check.NotNull(value, nameof(value));
    }
}
