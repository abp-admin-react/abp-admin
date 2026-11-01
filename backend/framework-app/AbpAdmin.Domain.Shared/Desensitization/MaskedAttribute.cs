using System;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 字段脱敏标记（对标 ruoyi-vue-pro 的 @MobileDesensitize 等注解族）。
/// 打在 DTO 的 string 属性上，响应序列化时由 MaskedStringConverter 执行脱敏；
/// <see cref="PlaintextPermission"/> 非空时，持有该权限的用户可看到明文（字段权限）。
/// 无法改动源码的第三方 DTO（如 ABP 的 IdentityUserDto）用 MaskingRuleRegistry 注册等效规则。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaskedAttribute : Attribute
{
    public MaskedAttribute(MaskKindEnum kind = MaskKindEnum.Custom)
    {
        Kind = kind;
    }

    /// <summary>脱敏规则类型；Custom 时由滑块/正则参数决定。</summary>
    public MaskKindEnum Kind { get; }

    /// <summary>Custom 滑块：前缀明文长度。</summary>
    public int PrefixKeep { get; init; }

    /// <summary>Custom 滑块：后缀明文长度。</summary>
    public int SuffixKeep { get; init; }

    /// <summary>Custom 滑块：替换符，默认 *。长度与被遮蔽区间等长。</summary>
    public string Replacer { get; init; } = "*";

    /// <summary>Custom 正则：匹配表达式（与滑块参数互斥，设置后优先）。</summary>
    public string? Pattern { get; init; }

    /// <summary>Custom 正则：替换串，默认 ******。</summary>
    public string RegexReplacer { get; init; } = "******";

    /// <summary>
    /// 字段权限：持有该权限的用户跳过脱敏返回明文；null/空表示一律脱敏。
    /// 权限判断在 HTTP 请求上下文内进行，无上下文（后台序列化）时按无权限处理（fail-closed）。
    /// </summary>
    public string? PlaintextPermission { get; init; }
}
