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

        // —— /Abp/*Script 三个脚本端点（ABP 10.7.0 均无授权标注，程序集字符串核实过全名）——
        // 登录页（LeptonXLite SSR）以 <script> 引用它们；默认拒绝下 302 到 /Account/Login，
        // 浏览器按 MIME 拒执行 HTML——GUI 全量测试实锤（2026-10）。三者数据面与已豁免的
        // JSON 端点同源：ConfigurationScript 就是 application-configuration JSON 的 $.extend 包装
        // （匿名访客拿到的是匿名用户视角：currentUser=null、默认设置）；LocalizationScript 只含
        // 公开本地化文案；ServiceProxyScript 暴露的 API 面与仓库随附的 web/swagger.json 同构，
        // 无边际泄露。漏配的失配形态由 AnonymousEndpointSweepTests 反向断言兜住。
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationScriptController",
        "Volo.Abp.AspNetCore.Mvc.Localization.AbpApplicationLocalizationScriptController",
        "Volo.Abp.AspNetCore.Mvc.ProxyScripting.AbpServiceProxyScriptController",
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
