using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using EasyAbp.Abp.SettingUi.Authorization;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Xunit;

namespace AbpAdmin.Settings;

/// <summary>
/// 钉住 SettingUi 权限种子内容：admin 角色必须同时拿到读（SettingUi.ShowSettingPage）
/// 与写（AbpAdmin.SettingUi.Update）。这个 Contributor 存在的原因（类注释）正是
/// "后接入模块的权限在首次种子之后才被发现，admin 拿不到权限接口 403"——
/// 读写分离后漏种写权限会以同样的形状复发（能看设置页、保存/重置 403）。
/// </summary>
public class SettingUiPermissionDataSeedContributorTests
{
    private sealed class RecordingPermissionDataSeeder : IPermissionDataSeeder
    {
        public string? ProviderName { get; private set; }

        public string? ProviderKey { get; private set; }

        public IReadOnlyList<string>? GrantNames { get; private set; }

        public Task SeedAsync(
            string providerName,
            string providerKey,
            IEnumerable<string> grantNames,
            Guid? tenantId = null)
        {
            ProviderName = providerName;
            ProviderKey = providerKey;
            GrantNames = grantNames.ToList();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Seed_Should_Grant_Read_And_Write_To_Admin_Role()
    {
        var recorder = new RecordingPermissionDataSeeder();
        var contributor = new SettingUiPermissionDataSeedContributor(recorder);

        await contributor.SeedAsync(new DataSeedContext());

        recorder.ProviderName.ShouldBe(RolePermissionValueProvider.ProviderName);
        recorder.ProviderKey.ShouldBe(AbpRoleConsts.AdminRoleName);
        recorder.GrantNames.ShouldNotBeNull();
        recorder.GrantNames.ShouldContain(SettingUiPermissions.ShowSettingPage,
            "漏种读权限：设置页直接 403");
        recorder.GrantNames.ShouldContain(AbpAdminPermissions.SettingUi.Update,
            "漏种写权限：能看设置页但保存/重置 403（读写分离引入的新失败形状）");
    }
}
