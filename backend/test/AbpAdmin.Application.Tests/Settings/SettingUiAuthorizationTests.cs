using System.Linq;
using AbpAdmin.Permissions;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace AbpAdmin.Settings;

/// <summary>
/// 回归守卫：上游 EasyAbp.SettingUi（含 2.10.0）的 SettingUiAppService / SettingUiController
/// 都没有 [Authorize]，/api/setting-ui/* 曾实际匿名可达（前端 access.ts 不是防线）。
/// 门禁收口在替换服务 AbpAdminSettingUiAppService 的类级 Authorize 上（EasyAbp 控制器注入
/// 的就是本类代理，授权拦截器在方法调用前生效）。这里用反射钉住特性——
/// 误删时测试套件立即失败，而不是等安全巡检再发现匿名端点。
/// "替换服务真的被 DI 解析"这一环由 SettingUiMaskingTests 的
/// SettingUiAppService_Should_Be_Replaced_By_Masked_Implementation 守（解代理验类型）；
/// 宿主层面的"任何端点默认不匿名可达"由 AnonymousEndpointSweepTests 全量审计。
/// </summary>
public class SettingUiAuthorizationTests
{
    [Fact]
    public void AbpAdminSettingUiAppService_Should_Require_ShowSettingPage_At_Class_Level()
    {
        var attributes = typeof(AbpAdminSettingUiAppService)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        attributes.ShouldNotBeEmpty();
        attributes.ShouldContain(a => a.Policy == SettingUiPermissions.ShowSettingPage);
    }

    /// <summary>
    /// 读写分离（模块五收口）：写值/重置两个入口必须叠加 AbpAdmin.SettingUi.Update。
    /// ABP 授权拦截器把类级与方法级 Authorize 组合为"都须满足"，
    /// 删掉方法级特性即退回单读权限就能改设置——这里钉住。
    /// </summary>
    [Theory]
    [InlineData(nameof(AbpAdminSettingUiAppService.SetSettingValuesAsync))]
    [InlineData(nameof(AbpAdminSettingUiAppService.ResetSettingValuesAsync))]
    public void Write_Entries_Should_Require_SettingUi_Update_At_Method_Level(string methodName)
    {
        // 覆写被整体删除时给出自解释的失败信息，而不是 Single() 的裸异常
        var method = typeof(AbpAdminSettingUiAppService)
            .GetMethods()
            .SingleOrDefault(m => m.Name == methodName
                         && m.DeclaringType == typeof(AbpAdminSettingUiAppService));

        method.ShouldNotBeNull(
            $"AbpAdminSettingUiAppService 不再覆写 {methodName}——覆写是写权限门禁(method-level Authorize)" +
            $"和操作日志(OnCompleted)的挂载点,删除即丢失两者。");

        var attributes = method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .ToList();

        attributes.ShouldNotBeEmpty($"写入口 {methodName} 缺方法级授权");
        attributes.ShouldContain(a => a.Policy == AbpAdminPermissions.SettingUi.Update);
    }
}
