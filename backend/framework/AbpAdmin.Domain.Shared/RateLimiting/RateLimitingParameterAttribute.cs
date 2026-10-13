using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 标注在方法参数上，指定该参数的值作为限流分区参数。
/// 一个方法只能标一个，标了多个直接抛 AbpException（编码错误，非运行时超限）。
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class RateLimitingParameterAttribute : Attribute
{
}
