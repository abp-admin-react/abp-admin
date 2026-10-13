using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 声明式操作限流特性。标注在应用服务/领域服务的方法或类上，指定限流策略名。
/// 方法级优先于类级。
///
/// 限制（必须满足，否则特性静默失效）：
/// 1. 服务必须从 DI 容器解析（new 出来的对象没有代理）。
/// 2. 被拦截的方法必须是 virtual（ABP 的动态代理基于继承）。
///    若服务通过接口注入，则接口方法不受 virtual 限制。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class OperationRateLimitingAttribute : Attribute
{
    public string PolicyName { get; }

    public OperationRateLimitingAttribute(string policyName)
    {
        PolicyName = policyName;
    }
}
