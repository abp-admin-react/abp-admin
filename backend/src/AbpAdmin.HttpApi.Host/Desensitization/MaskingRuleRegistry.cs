using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 第三方 DTO 的脱敏规则注册表。无法给源码外的 DTO（如 ABP 的 IdentityUserDto）加
/// <see cref="MaskedAttribute"/>，改在这里以 (DTO 类型, 属性名) 注册等效规则，
/// 由 MaskingJsonSetup 在序列化元数据构建时一并挂上 MaskedStringConverter。
/// 规则在 Host 模块启动时静态注册（单例），不提供运行时增删。
/// </summary>
public sealed class MaskingRuleRegistry
{
    private readonly List<MaskingRule> _rules = new();

    public MaskingRuleRegistry Add<TDto>(Expression<Func<TDto, string?>> propertySelector, MaskedAttribute spec)
    {
        var propertyName = propertySelector.Body switch
        {
            MemberExpression { Member: PropertyInfo property } => property.Name,
            UnaryExpression { Operand: MemberExpression { Member: PropertyInfo property } } => property.Name,
            _ => throw new ArgumentException("属性选择器必须是成员表达式，如 x => x.PhoneNumber", nameof(propertySelector)),
        };

        _rules.Add(new MaskingRule(typeof(TDto), propertyName, spec));
        return this;
    }

    internal MaskedAttribute? Find(Type serializedType, string propertyName)
    {
        foreach (var rule in _rules)
        {
            // 精确类型或其子类（派生 DTO 继承基类规则）
            if (rule.DtoType.IsAssignableFrom(serializedType)
                && string.Equals(rule.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return rule.Spec;
            }
        }

        return null;
    }
}

internal sealed record MaskingRule(Type DtoType, string PropertyName, MaskedAttribute Spec);
