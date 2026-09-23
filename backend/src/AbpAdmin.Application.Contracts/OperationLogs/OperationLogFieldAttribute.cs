using System;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 标注在 DTO/实体属性上，为 <c>{{_diff(...)}}</c> 差异输出提供中文显示名
/// （对标 mzt-biz-log 的 @DiffLogField(name=...)）。未标注时回退为属性名。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OperationLogFieldAttribute : Attribute
{
    public OperationLogFieldAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}
