using System;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 登录会话设备类型解析。取值即 ABP <see cref="IdentitySessionDevices"/> 常量（Web / Mobile）：
/// 客户端在登录/换 token 请求上带可选 device 参数（如 Mobile 端带 device=Mobile），
/// 不传、传空或传未知值时一律回退 Web（保守默认，不因脏参数改变互踢面）。
///
/// 这是三档防并发登录中「同类型设备互踢」的区分轴——此前所有会话都硬编码 Web，
/// LogoutFromSameTypeDevices 与 LogoutFromAllDevices 等效（六透镜审查遗留项）。
/// 客户端接入约定：token 请求附加 device=Mobile 即可让该端按独立设备类型参与互踢。
///
/// 信任模型（设计边界，非漏洞）：device 是客户端自报提示，不构成安全控制——
/// 恶意客户端谎报最多只能在 SameType 档维持每类型一个会话（与 Disabled 档收益相同）；
/// LogoutFromAllDevices 与按用户吊销均不看 device，事件响应请用这两者。
/// </summary>
public static class IdentitySessionDeviceResolver
{
    public const string DeviceParameterName = "device";

    public static string Resolve(string? deviceParameter)
    {
        if (string.IsNullOrWhiteSpace(deviceParameter))
        {
            return IdentitySessionDevices.Web;
        }

        return deviceParameter.Trim().Equals(IdentitySessionDevices.Mobile, StringComparison.OrdinalIgnoreCase)
            ? IdentitySessionDevices.Mobile
            : IdentitySessionDevices.Web;
    }
}
