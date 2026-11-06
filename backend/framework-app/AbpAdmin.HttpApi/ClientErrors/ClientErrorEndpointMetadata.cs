using System.Collections.Generic;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 挂到 MVC 端点元数据上的受众登记，供运行时投影读取。
/// Convention 写入 Action.Filters 与 Selector.EndpointMetadata，避免只写 ActionModel 被路由丢掉。
/// </summary>
public sealed class ClientErrorEndpointMetadata : IFilterMetadata
{
    /// <summary>出口稳定 Id：DeclaringType.MethodName(参数签名)。</summary>
    public required string ExitId { get; init; }

    /// <summary>控制器 CLR 全名。</summary>
    public required string DeclaringType { get; init; }

    /// <summary>动作方法名。</summary>
    public required string MethodName { get; init; }

    /// <summary>参数类型短名签名，如 (Guid, String)。</summary>
    public required string ParameterSignature { get; init; }

    /// <summary>HTTP 方法；无法从约束解析时为 null。</summary>
    public string? HttpMethod { get; init; }

    /// <summary>动作路由模板。</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>声明或推断的错误受众。不按登录身份放宽。</summary>
    public required ClientErrorAudience Audience { get; init; }

    /// <summary>该出口的错误协议。生产路径由登记表选择适配器。</summary>
    public required ErrorProtocol Protocol { get; init; }

    /// <summary>动作上的授权策略名。空集合表示无管理权限门槛。</summary>
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>转成 Domain 登记结构，供投影核心与资格解析使用。</summary>
    public AdminErrorEndpointRegistration ToRegistration()
    {
        return new AdminErrorEndpointRegistration
        {
            ExitId = ExitId,
            DeclaringType = DeclaringType,
            MethodName = MethodName,
            ParameterSignature = ParameterSignature,
            HttpMethod = HttpMethod,
            RouteTemplate = RouteTemplate,
            Audience = Audience,
            Protocol = Protocol,
            RequiredPermissions = RequiredPermissions,
        };
    }
}
