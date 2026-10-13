using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 把脱敏规则接入 MVC 响应序列化（System.Text.Json）。
/// 用自定义 IJsonTypeInfoResolver 包装既有 resolver（.NET 10 无 WithModifier 便捷扩展，
/// 且既有链可能是 null / Default / 组合 resolver，统一走包装最稳）：在类型元数据构建期为
/// 命中规则的 string 属性挂 MaskedStringConverter——等价于 ruoyi 基于 Jackson
/// ContextualSerializer 的零侵入方案，且保留 ABP 已有的 resolver 链（日期格式等不受影响）。
/// 只影响 HTTP 响应；IJsonSerializer（缓存/后台作业参数等）不接入，避免污染存储值。
/// </summary>
public static class MaskingJsonSetup
{
    public static void Configure(JsonSerializerOptions options, MaskingRuleRegistry registry)
    {
        var inner = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = new MaskingResolver(inner, registry);
    }

    private static void Modify(JsonTypeInfo typeInfo, MaskingRuleRegistry registry)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            var spec = property.AttributeProvider?
                           .GetCustomAttributes(typeof(MaskedAttribute), inherit: false)?
                           .OfType<MaskedAttribute>()
                           .FirstOrDefault()
                       ?? registry.Find(typeInfo.Type, property.Name);
            if (spec == null)
            {
                continue;
            }

            property.CustomConverter = new MaskedStringConverter(spec);
        }
    }

    /// <summary>包装内层 resolver，构建出的元数据统一过一遍脱敏修饰。</summary>
    private sealed class MaskingResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver _inner;
        private readonly MaskingRuleRegistry _registry;

        public MaskingResolver(IJsonTypeInfoResolver inner, MaskingRuleRegistry registry)
        {
            _inner = inner;
            _registry = registry;
        }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = _inner.GetTypeInfo(type, options);
            if (typeInfo != null)
            {
                Modify(typeInfo, _registry);
            }

            return typeInfo;
        }
    }
}
