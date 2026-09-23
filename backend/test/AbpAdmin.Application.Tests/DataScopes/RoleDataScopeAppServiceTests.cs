using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.DataScopes;

/* 角色数据范围配置（RoleDataScopeAppService）写入侧测试。
 * 重点回归（代码审查 round1）：Create 的唯一性检查必须用规范角色名——
 * 混合大小写输入不得绕过应用层查重、落库成规范名后直撞唯一索引变成原始 500。
 */
public abstract class RoleDataScopeAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IRoleDataScopeAppService _appService;
    private readonly IIdentityRoleRepository _roleRepository;

    protected RoleDataScopeAppServiceTests()
    {
        _appService = GetRequiredService<IRoleDataScopeAppService>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
    }

    private async Task<string> EnsureRoleAsync(string roleName)
    {
        var role = new IdentityRole(Guid.NewGuid(), roleName);
        await _roleRepository.InsertAsync(role, autoSave: true);
        return role.Name!;
    }

    [Fact]
    public async Task CreateAsync_Should_Store_Canonical_RoleName()
    {
        // 角色真实名含混合大小写：客户端原样大小写输入时，落库必须是角色的规范 Name
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"Ds-Canonical-{suffix}");

        var created = await _appService.CreateAsync(new CreateUpdateRoleDataScopeDto
        {
            RoleName = roleName.ToUpperInvariant(),
            ScopeType = DataScopeTypeEnum.All
        });

        created.RoleName.ShouldBe(roleName);
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Mixed_Case_Duplicate_With_Canonical_Name()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"ds-dup-{suffix}");

        await _appService.CreateAsync(new CreateUpdateRoleDataScopeDto
        {
            RoleName = roleName,
            ScopeType = DataScopeTypeEnum.All
        });

        // 修复前：按客户端原样大写查重查不到既有小写记录 → 插入规范名直撞唯一索引变 500；
        // 修复后：先归一再查重，抛带错误码的 BusinessException
        var ex = await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(
            new CreateUpdateRoleDataScopeDto
            {
                RoleName = roleName.ToUpperInvariant(),
                ScopeType = DataScopeTypeEnum.SelfOnly
            }));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeAlreadyExists);
    }
}
