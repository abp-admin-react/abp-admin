using System;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 按路径前缀选择错误协议。未登记 /api 仍走 ABP 信封；非 API 返回 null。
/// </summary>
public class ControlledProtocolRegistry : IControlledProtocolRegistry, ISingletonDependency
{
    private static readonly ControlledProtocolRegistration Api = new()
    {
        ExitId = "controlled:/api",
        Protocol = ErrorProtocol.AbpEnvelope,
        RoutePrefix = ControlledProtocolPathPrefixes.Api,
    };

    private static readonly ControlledProtocolRegistration Connect = new()
    {
        ExitId = "controlled:/connect",
        Protocol = ErrorProtocol.OAuth,
        RoutePrefix = ControlledProtocolPathPrefixes.Connect,
    };

    private static readonly ControlledProtocolRegistration Gateway = new()
    {
        ExitId = "controlled:/v1",
        Protocol = ErrorProtocol.Gateway,
        RoutePrefix = ControlledProtocolPathPrefixes.TokenGatewayV1,
    };

    private static readonly ControlledProtocolRegistration SignalR = new()
    {
        ExitId = "controlled:/signalr-hubs",
        Protocol = ErrorProtocol.SignalR,
        RoutePrefix = ControlledProtocolPathPrefixes.SignalRHubs,
    };

    public IReadOnlyCollection<ControlledProtocolRegistration> GetAll() =>
        [Api, Connect, Gateway, SignalR];

    public ControlledProtocolRegistration? FindMatch(string? httpMethod, string path)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        if (HasPrefix(normalized, ControlledProtocolPathPrefixes.Connect))
        {
            return Connect;
        }

        if (HasPrefix(normalized, ControlledProtocolPathPrefixes.TokenGatewayV1))
        {
            return Gateway;
        }

        if (HasPrefix(normalized, ControlledProtocolPathPrefixes.SignalRHubs))
        {
            return SignalR;
        }

        if (HasPrefix(normalized, ControlledProtocolPathPrefixes.Api))
        {
            return Api;
        }

        return null;
    }

    private static bool HasPrefix(string path, string prefix)
    {
        return path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
