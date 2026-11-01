using System;
using System.Reflection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DynamicProxy;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流拦截器注册器。
/// </summary>
public static class OperationRateLimitingInterceptorRegistrar
{
    public static void RegisterIfNeeded(IOnServiceRegistredContext context)
    {
        if (ShouldIntercept(context.ImplementationType))
        {
            context.Interceptors.TryAdd<OperationRateLimitingInterceptor>();
        }
    }

    private static bool ShouldIntercept(Type type)
    {
        if (DynamicProxyIgnoreTypes.Contains(type))
        {
            return false;
        }

        // 类上标了特性
        if (type.GetCustomAttribute<OperationRateLimitingAttribute>(inherit: true) != null)
        {
            return true;
        }

        // 任何方法上标了特性
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<OperationRateLimitingAttribute>(inherit: true) != null)
            {
                return true;
            }
        }

        return false;
    }
}
