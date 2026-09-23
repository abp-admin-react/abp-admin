using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言定义实体。全局资产（host 维护），所有租户共用。
/// 不实现 IMultiTenant，不有 TenantId 列。
/// </summary>
public class Language : AuditedAggregateRoot<Guid>
{
    /// <summary>
    /// 文化名称（如 zh-Hans、en-US）。创建后不可修改。
    /// </summary>
    public virtual string CultureName { get; protected set; }

    /// <summary>
    /// UI 文化名称。创建后不可修改。
    /// </summary>
    public virtual string UiCultureName { get; protected set; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public virtual string DisplayName { get; set; }

    /// <summary>
    /// 旗标图标。
    /// </summary>
    public virtual string? FlagIcon { get; set; }

    /// <summary>
    /// 是否启用。
    /// </summary>
    public virtual bool IsEnabled { get; set; }

    /// <summary>
    /// 是否默认语言。冗余字段，以 LocalizationSettingNames.DefaultLanguage 设置为准。
    /// </summary>
    public virtual bool IsDefault { get; set; }

    protected Language()
    {
        CultureName = string.Empty;
        UiCultureName = string.Empty;
        DisplayName = string.Empty;
    }

    public Language(
        Guid id,
        string cultureName,
        string uiCultureName,
        string displayName,
        string? flagIcon = null,
        bool isEnabled = true,
        bool isDefault = false)
        : base(id)
    {
        CultureName = cultureName;
        UiCultureName = uiCultureName;
        DisplayName = displayName;
        FlagIcon = flagIcon;
        IsEnabled = isEnabled;
        IsDefault = isDefault;
    }
}
