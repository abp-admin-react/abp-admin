using System;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 标记客户/共享/管理兼容错误受众（方案 4.1）。
/// 控制器或 Action 声明后，HTTP 投影按此选择公开目录，而不是按登录身份放宽。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientErrorAudienceAttribute : Attribute
{
    public ClientErrorAudienceAttribute(ClientErrorAudience audience)
    {
        Audience = audience;
    }

    /// <summary>该控制器或动作的错误受众。方法级覆盖类级。</summary>
    public ClientErrorAudience Audience { get; }
}
