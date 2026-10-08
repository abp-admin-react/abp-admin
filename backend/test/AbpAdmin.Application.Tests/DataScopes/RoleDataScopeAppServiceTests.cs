using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.DataScopes;

/* 角色数据范围配置（RoleDataScopeAppService）写入侧测试。
 * 重点回归（代码审查 round1）：Create 的唯一性检查必须用规范角色名——
 * 混合大小写输入不得绕过应用层查重、落库成规范名后直撞唯一索引变成原始 500。
 *
 * 防提权（借鉴 Admin.NET GrantDataScope）：持有 Manage 权限但自身快照受限的操作者，
 * 不得授「全部数据」、不得把自定义 OU 授到自身范围之外——否则等于给自己的角色提权。
 */
public abstract class RoleDataScopeAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IRoleDataScopeAppService _appService;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<RoleDataScope, Guid> _scopeRepository;
    private readonly OrganizationUnitManager _ouManager;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    protected RoleDataScopeAppServiceTests()
    {
        _appService = GetRequiredService<IRoleDataScopeAppService>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _scopeRepository = GetRequiredService<IRepository<RoleDataScope, Guid>>();
        _ouManager = GetRequiredService<OrganizationUnitManager>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private async Task<string> EnsureRoleAsync(string roleName)
    {
        var role = new IdentityRole(Guid.NewGuid(), roleName);
        await WithUnitOfWorkAsync(() => _roleRepository.InsertAsync(role, autoSave: true));
        return role.Name!;
    }

    /// <summary>构造 Custom 范围配置（实体构造器不带 OU 列表，子行单独加）。</summary>
    private static RoleDataScope CreateCustomScope(string roleName, params Guid[] ouIds)
    {
        var scope = new RoleDataScope(Guid.NewGuid(), roleName, DataScopeTypeEnum.Custom);
        foreach (var ouId in ouIds)
        {
            scope.CustomOrganizationUnits.Add(new RoleDataScopeOrganizationUnit(scope.Id, ouId));
        }

        return scope;
    }

    /// <summary>
    /// 以「已认证 + 指定角色」的操作者身份执行。authenticationType 必须显式给值，
    /// 否则 ClaimsIdentity.IsAuthenticated 恒为 false，防提权校验会按未认证上下文放行。
    /// </summary>
    private IDisposable AsOperator(params string[] roleNames)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new(AbpClaimTypes.UserName, $"op-{Guid.NewGuid():N}"[..16])
        };
        claims.AddRange(roleNames.Select(r => new Claim(AbpClaimTypes.Role, r)));

        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Testing")));
    }

    [Fact]
    public async Task CreateAsync_Should_Store_Canonical_RoleName()
    {
        // 角色真实名含混合大小写：客户端原样大小写输入时，落库必须是角色的规范 Name
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"Ds-Canonical-{suffix}");

        using (AsOperator("admin"))
        {
            var created = await _appService.CreateAsync(new CreateUpdateRoleDataScopeDto
            {
                RoleName = roleName.ToUpperInvariant(),
                ScopeType = DataScopeTypeEnum.All
            });

            created.RoleName.ShouldBe(roleName);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Mixed_Case_Duplicate_With_Canonical_Name()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"ds-dup-{suffix}");

        using (AsOperator("admin"))
        {
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

    [Fact]
    public async Task CreateAsync_Should_Reject_All_When_Operator_NotAll()
    {
        // 操作者已认证、非 admin、自身无任何数据范围（快照 fail-closed 空集）：
        // 授 All 等于给「自己可加入的角色」提权，必须拒绝
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"ds-esc-target-{suffix}");

        using (AsOperator($"ds-esc-op-{suffix}"))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(
                new CreateUpdateRoleDataScopeDto
                {
                    RoleName = roleName,
                    ScopeType = DataScopeTypeEnum.All
                }));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Allow_All_When_Operator_IsAdmin()
    {
        // admin 超管豁免：出厂不为 admin 配 RoleDataScope 行（快照为空），
        // 不豁免会锁死首次配置（自我锁死 paradox）
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"ds-admin-target-{suffix}");

        using (AsOperator("admin"))
        {
            var created = await _appService.CreateAsync(new CreateUpdateRoleDataScopeDto
            {
                RoleName = roleName,
                ScopeType = DataScopeTypeEnum.All
            });

            created.ScopeType.ShouldBe(DataScopeTypeEnum.All);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_CustomOu_Outside_Operator_Scope()
    {
        // 操作者自身 = Custom(OU-A)；目标想授 Custom(OU-A, OU-B)——OU-B 越界，拒绝并给出越界数
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var operatorRoleName = await EnsureRoleAsync($"ds-cu-op-{suffix}");
        var targetRoleName = await EnsureRoleAsync($"ds-cu-target-{suffix}");

        var ouA = new OrganizationUnit(Guid.NewGuid(), $"OU-A-{suffix}");
        var ouB = new OrganizationUnit(Guid.NewGuid(), $"OU-B-{suffix}");
        await WithUnitOfWorkAsync(async () =>
        {
            await _ouManager.CreateAsync(ouA);
            await _ouManager.CreateAsync(ouB);
            await _scopeRepository.InsertAsync(CreateCustomScope(operatorRoleName, ouA.Id), autoSave: true);
        });

        using (AsOperator(operatorRoleName))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(
                new CreateUpdateRoleDataScopeDto
                {
                    RoleName = targetRoleName,
                    ScopeType = DataScopeTypeEnum.Custom,
                    CustomOrganizationUnitIds = new List<Guid> { ouA.Id, ouB.Id }
                }));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeCustomOuOutOfScope);
            ex.Data["Count"]!.ToString().ShouldBe("1");
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Allow_CustomOu_Within_Operator_Scope()
    {
        // 操作者自身 = Custom(OU-A)；授 Custom(OU-A) 是收敛不越权，放行
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var operatorRoleName = await EnsureRoleAsync($"ds-cu-ok-op-{suffix}");
        var targetRoleName = await EnsureRoleAsync($"ds-cu-ok-target-{suffix}");

        var ouA = new OrganizationUnit(Guid.NewGuid(), $"OU-A-{suffix}");
        await WithUnitOfWorkAsync(async () =>
        {
            await _ouManager.CreateAsync(ouA);
            await _scopeRepository.InsertAsync(CreateCustomScope(operatorRoleName, ouA.Id), autoSave: true);
        });

        using (AsOperator(operatorRoleName))
        {
            var created = await _appService.CreateAsync(new CreateUpdateRoleDataScopeDto
            {
                RoleName = targetRoleName,
                ScopeType = DataScopeTypeEnum.Custom,
                CustomOrganizationUnitIds = new List<Guid> { ouA.Id }
            });

            created.CustomOrganizationUnitIds.ShouldContain(ouA.Id);
        }
    }

    [Fact]
    public async Task UpdateAsync_Should_Reject_Escalation_To_All()
    {
        // 已有 SelfOnly 配置的角色改成 All 同样是提权路径，更新入口同口径拦截
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = await EnsureRoleAsync($"ds-esc-upd-{suffix}");

        var entityId = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => _scopeRepository.InsertAsync(
            new RoleDataScope(entityId, roleName, DataScopeTypeEnum.SelfOnly), autoSave: true));

        using (AsOperator($"ds-esc-upd-op-{suffix}"))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.UpdateAsync(
                entityId,
                new CreateUpdateRoleDataScopeDto
                {
                    RoleName = roleName,
                    ScopeType = DataScopeTypeEnum.All
                }));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation);
        }
    }
}
