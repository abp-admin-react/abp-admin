using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.DataScopes;
using AbpAdmin.Menus;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Identity;

/* 角色管理自研扩展的应用服务级测试：
 * 1. 删除保护（AbpAdminRoleAppService 替换 IdentityRoleAppService）：角色下仍有用户时拒绝删除；
 * 2. 角色重命名级联（RoleRenamedCascadeHandler 订阅 IdentityRoleNameChangedEto）：
 *    MenuGrant.ProviderKey 与 RoleDataScope.RoleName 跟随新名，旧行不残留；
 * 3. 删除级联（RoleDeletedCascadeHandler）：无人使用的角色删除后两张自研表同步清理；
 * 4. 角色下用户只读查询（RoleUserAdminAppService）：分页/总数/非成员排除。
 */
public abstract class RoleAdminTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIdentityRoleAppService _roleAppService;
    private readonly IRoleUserAdminAppService _roleUserAppService;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;

    protected RoleAdminTests()
    {
        _roleAppService = GetRequiredService<IIdentityRoleAppService>();
        _roleUserAppService = GetRequiredService<IRoleUserAdminAppService>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
        _menuGrantRepository = GetRequiredService<IRepository<MenuGrant, Guid>>();
        _roleDataScopeRepository = GetRequiredService<IRepository<RoleDataScope, Guid>>();
    }

    private async Task<Volo.Abp.Identity.IdentityRole> CreateRoleAsync(string roleName)
    {
        var role = new Volo.Abp.Identity.IdentityRole(Guid.NewGuid(), roleName);
        await WithUnitOfWorkAsync(() => _roleRepository.InsertAsync(role, autoSave: true));
        return role;
    }

    private async Task<MenuGrant> CreateMenuGrantAsync(string roleName, string suffix)
    {
        var menu = new Menu(Guid.NewGuid(), null, null, MenuTypeEnum.Menu,
            $"重命名级联-{suffix}", name: $"rn-cascade-{suffix}", path: $"/rn-cascade/{suffix}");
        var grant = new MenuGrant(Guid.NewGuid(), menu.Id, null, MenuConsts.RoleProviderName, roleName);
        await WithUnitOfWorkAsync(async () =>
        {
            await _menuRepository.InsertAsync(menu, autoSave: true);
            await _menuGrantRepository.InsertAsync(grant, autoSave: true);
        });
        return grant;
    }

    private async Task CreateUsersInRoleAsync(string roleName, string suffix, int count)
    {
        var users = Enumerable.Range(1, count)
            .Select(i => new Volo.Abp.Identity.IdentityUser(
                Guid.NewGuid(), $"ru-m{i}-{suffix}", $"m{i}-{suffix}@test.example"))
            .ToList();
        users.Add(new Volo.Abp.Identity.IdentityUser(
            Guid.NewGuid(), $"ru-out-{suffix}", $"out-{suffix}@test.example"));

        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var user in users)
            {
                (await _userManager.CreateAsync(user)).CheckErrors();
            }

            foreach (var member in users.Take(count))
            {
                (await _userManager.AddToRoleAsync(member, roleName)).CheckErrors();
            }
        });
    }

    [Fact]
    public async Task DeleteAsync_Should_Reject_When_Role_Has_Users()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = await CreateRoleAsync($"del-guard-{suffix}");
        await CreateUsersInRoleAsync(role.Name!, suffix, 1);

        // 修复前：ABP 原生删除静默清掉用户-角色关联，用户权限无感丢失
        var ex = await Should.ThrowAsync<BusinessException>(() => _roleAppService.DeleteAsync(role.Id));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Identity.RoleHasUsers);
        ex.Data["RoleName"]!.ToString().ShouldBe(role.Name);
        ex.Data["Count"]!.ToString().ShouldBe("1");

        // 角色仍在（删除被拒绝，不是删了再报错）
        (await WithUnitOfWorkAsync(() => _roleRepository.FindAsync(role.Id))).ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_Should_Succeed_And_Cascade_When_No_Users()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = await CreateRoleAsync($"del-ok-{suffix}");
        var grant = await CreateMenuGrantAsync(role.Name!, suffix);
        await WithUnitOfWorkAsync(() => _roleDataScopeRepository.InsertAsync(
            new RoleDataScope(Guid.NewGuid(), role.Name!, DataScopeTypeEnum.SelfOnly), autoSave: true));

        await _roleAppService.DeleteAsync(role.Id);

        await WithUnitOfWorkAsync(async () =>
        {
            (await _roleRepository.FindAsync(role.Id)).ShouldBeNull();
            (await _menuGrantRepository.FindAsync(grant.Id)).ShouldBeNull();
            (await _roleDataScopeRepository.FirstOrDefaultAsync(x => x.RoleName == role.Name!)).ShouldBeNull();
        });
    }

    [Fact]
    public async Task UpdateAsync_Rename_Should_Cascade_MenuGrant_And_RoleDataScope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var oldName = $"rn-old-{suffix}";
        var newName = $"rn-new-{suffix}";
        var role = await CreateRoleAsync(oldName);
        var grant = await CreateMenuGrantAsync(oldName, suffix);
        await WithUnitOfWorkAsync(() => _roleDataScopeRepository.InsertAsync(
            new RoleDataScope(Guid.NewGuid(), oldName, DataScopeTypeEnum.SelfOnly), autoSave: true));

        // 走真实应用服务入口重命名（SetRoleNameAsync → ChangeName → IdentityRoleNameChangedEto）
        await _roleAppService.UpdateAsync(role.Id, new IdentityRoleUpdateDto
        {
            Name = newName,
            IsDefault = false,
            IsPublic = false,
            ConcurrencyStamp = role.ConcurrencyStamp
        });

        // 两张以角色名为键的自研表都跟随新名，旧行不残留
        await WithUnitOfWorkAsync(async () =>
        {
            var renamedGrant = await _menuGrantRepository.FindAsync(grant.Id);
            renamedGrant.ShouldNotBeNull();
            renamedGrant.ProviderKey.ShouldBe(newName);
            (await _menuGrantRepository.GetListAsync(x => x.ProviderKey == oldName)).Count.ShouldBe(0);

            var renamedScope = await _roleDataScopeRepository.FirstOrDefaultAsync(x => x.RoleName == newName);
            renamedScope.ShouldNotBeNull();
            (await _roleDataScopeRepository.GetListAsync(x => x.RoleName == oldName)).Count.ShouldBe(0);
        });
    }

    [Fact]
    public async Task GetRoleUsersAsync_Should_Return_Only_Members_Paged()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = await CreateRoleAsync($"ru-role-{suffix}");
        await CreateUsersInRoleAsync(role.Name!, suffix, 3);

        var page1 = await _roleUserAppService.GetListAsync(role.Id,
            new Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto
            {
                MaxResultCount = 2,
                SkipCount = 0,
                Sorting = "UserName"
            });

        page1.TotalCount.ShouldBe(3);
        page1.Items.Count.ShouldBe(2);
        page1.Items.Select(x => x.UserName).ShouldAllBe(x => x.StartsWith("ru-m"));

        var page2 = await _roleUserAppService.GetListAsync(role.Id,
            new Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto
            {
                MaxResultCount = 2,
                SkipCount = 2,
                Sorting = "UserName"
            });

        page2.Items.Count.ShouldBe(1);

        // 角色不存在返回 404（EntityNotFoundException），不静默给空列表
        await Should.ThrowAsync<EntityNotFoundException>(() => _roleUserAppService.GetListAsync(
            Guid.NewGuid(), new Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto()));
    }
}
