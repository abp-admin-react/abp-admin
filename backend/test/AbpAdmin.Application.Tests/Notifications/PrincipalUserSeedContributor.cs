using System;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Shouldly;

namespace AbpAdmin.Notifications;

/// <summary>
/// 为 FakeCurrentPrincipalAccessor 的固定主体 id 补种真实用户行（host 侧）。
/// 定向通知在 sectest b07 修复后按「当前租户内存在的用户」过滤目标 UserIds——
/// 旧夹具只伪造 principal 声明、库里无此用户行，依赖 AdminUserId 的既有测试会
/// 全体命中 EmptyUserIds。补种后固定 id 与真实用户一致，主体/库内双向成立。
/// 本 contributor 位于 Application.Tests 程序集：EFCore 测试模块依赖本模块，
/// 种子随 DataSeeder 一同执行（幂等：固定 id 已存在则跳过）。
/// </summary>
public class PrincipalUserSeedContributor : IDataSeedContributor, ITransientDependency
{
    /// <summary>与 FakeCurrentPrincipalAccessor 的测试主体声明同源（勿改一边）。</summary>
    public static readonly Guid PrincipalUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly ICurrentTenant _currentTenant;
    private readonly IdentityUserManager _userManager;

    public PrincipalUserSeedContributor(ICurrentTenant currentTenant, IdentityUserManager userManager)
    {
        _currentTenant = currentTenant;
        _userManager = userManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context?.TenantId))
        {
            if (_currentTenant.Id == null &&
                await _userManager.FindByIdAsync(PrincipalUserId.ToString()) == null)
            {
                var user = new IdentityUser(PrincipalUserId, "ut.principal", "ut.principal@sectest.local",
                    _currentTenant.Id);
                (await _userManager.CreateAsync(user, "Sectest!1234")).Succeeded.ShouldBe(true);
            }
        }
    }
}
