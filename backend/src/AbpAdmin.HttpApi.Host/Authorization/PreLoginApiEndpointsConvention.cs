using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AbpAdmin.Authorization;

/// <summary>
/// 给登录链路必需的 ABP 框架 API 控制器补匿名元数据（<see cref="EndpointAllowAnonymousMarker"/>）。
/// 问题：ABP 10.6.1 的 <c>AbpApplicationConfigurationController</c>（/api/abp/application-configuration）
/// 与 <c>AbpApplicationLocalizationController</c>（/api/abp/application-configuration/localization）
/// 类上没有任何授权标注（包内元数据核实），宿主没开 FallbackPolicy 时它们"事实上匿名可达"，
/// 前端登录页依赖它们拉取基础配置与本地化——开了默认拒绝后必须显式豁免，
/// 否则 SPA 登录页初始化即 401（Chrome 实测抓到过）。
/// 与 OpenIddictConnectEndpointsConvention、PreLoginRazorPagesConvention 共同构成登录链路豁免集，
/// 由 AnonymousEndpointSweepTests 双向兜住（白名单登记 + 反向必达）。
/// </summary>
public class PreLoginApiEndpointsConvention : IControllerModelConvention
{
    private static readonly string[] AnonymousControllerTypeNames =
    [
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationController",
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationLocalizationController",
    ];

    public void Apply(ControllerModel controller)
    {
        if (Array.IndexOf(AnonymousControllerTypeNames, controller.ControllerType.FullName) < 0)
        {
            return;
        }

        controller.Filters.Add(new EndpointAllowAnonymousMarker());
    }
}
