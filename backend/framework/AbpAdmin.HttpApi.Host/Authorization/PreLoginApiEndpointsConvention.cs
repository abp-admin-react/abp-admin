using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AbpAdmin.Authorization;

/// <summary>
/// 给登录链路必需的 ABP 框架 API 控制器补匿名元数据（<see cref="EndpointAllowAnonymousMarker"/>）。
/// 问题：ABP 10.6.1 的 <c>AbpApplicationConfigurationController</c>（/api/abp/application-configuration）、
/// <c>AbpApplicationLocalizationController</c>（/api/abp/application-configuration/localization）与
/// <c>AbpTenantController</c>（/api/abp/multi-tenancy/tenants/by-name|by-id）
/// 类上没有任何授权标注（包内元数据核实；后者源码位于
/// framework/src/Volo.Abp.AspNetCore.Mvc.UI.MultiTenancy/Pages/Abp/MultiTenancy/，命名空间是
/// Pages.Abp.MultiTenancy 而非 Volo.*——全量实测时被反向测试逮住一次，勿"顺手改回 Volo."），
/// 宿主没开 FallbackPolicy 时它们"事实上匿名可达"，前端登录页依赖前两者拉取基础配置与本地化、
/// 依赖租户端点做子域名租户解析与登录页「切换租户」——开了默认拒绝后必须显式豁免，
/// 否则 SPA 登录页初始化即 401；租户端点被拒时宿主对非 AJAX 请求 302 到 /Error，
/// 前端把解析失败误判成「租户不存在」（demo 子域名全量实测抓到过）。
/// 与 OpenIddictConnectEndpointsConvention、PreLoginRazorPagesConvention 共同构成登录链路豁免集，
/// 由 AnonymousEndpointSweepTests 双向兜住（白名单登记 + 反向必达）。
/// </summary>
public class PreLoginApiEndpointsConvention : IControllerModelConvention
{
    private static readonly string[] AnonymousControllerTypeNames =
    [
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationController",
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationLocalizationController",
        "Pages.Abp.MultiTenancy.AbpTenantController",
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
