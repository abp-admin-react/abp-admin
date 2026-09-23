using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Identity;

/* MoveAsync 调整（遗留项修复）：父子关系改走请求体（MoveOrganizationUnitInput），
 * ParentId=null＝移到根级；防环校验在服务端（OU Code 前缀判定），UI 不是控制。
 * 三个场景：移入自身子树被拒 / 移到根 / 移到另一棵树下（Code 前缀重算正确，含子树级联）。
 */
public abstract class OrganizationUnitMoveTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOrganizationUnitAppService _ouAppService;
    private readonly IOrganizationUnitRepository _ouRepository;
    private readonly OrganizationUnitManager _ouManager;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    protected OrganizationUnitMoveTests()
    {
        _ouAppService = GetRequiredService<IOrganizationUnitAppService>();
        _ouRepository = GetRequiredService<IOrganizationUnitRepository>();
        _ouManager = GetRequiredService<OrganizationUnitManager>();
        _localizer = GetRequiredService<IStringLocalizer<AbpAdminResource>>();
    }

    private async Task<OrganizationUnit> CreateOuAsync(string displayName, Guid? parentId)
    {
        var ou = new OrganizationUnit(Guid.NewGuid(), displayName, parentId);
        await WithUnitOfWorkAsync(async () => { await _ouManager.CreateAsync(ou); });
        return ou;
    }

    private async Task DeleteOuAsync(Guid id)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            if (await _ouRepository.FindAsync(id) != null)
            {
                await _ouManager.DeleteAsync(id);
            }
        });
    }

    [Fact]
    public async Task MoveAsync_Should_Reject_Move_Into_Own_Subtree()
    {
        var root = await CreateOuAsync($"移动根A-{Guid.NewGuid().ToString("N")[..6]}", null);
        var child = await CreateOuAsync($"移动子B-{Guid.NewGuid().ToString("N")[..6]}", root.Id);
        var grandchild = await CreateOuAsync($"移动孙C-{Guid.NewGuid().ToString("N")[..6]}", child.Id);

        try
        {
            // 直接把父移到自己的后代下（绕过前端的场景，UI 不是控制）
            var exception = await Should.ThrowAsync<UserFriendlyException>(async () =>
                await _ouAppService.MoveAsync(root.Id, new MoveOrganizationUnitInput { ParentId = grandchild.Id }));

            // 本地化文案等值断言（同 UserImport 系列惯例）：钉死走的是防环守卫而非其它失败来源
            exception.Message.ShouldBe(
                _localizer["OrganizationUnit:CannotMoveIntoOwnSubtree", root.DisplayName].Value);

            // 节点移到自己＝StartsWith 恒真，同样必须被守卫拦下（而非 ABP 同父早退静默返回）
            await Should.ThrowAsync<UserFriendlyException>(async () =>
                await _ouAppService.MoveAsync(child.Id, new MoveOrganizationUnitInput { ParentId = child.Id }));

            // 状态未被破坏：root 仍是根，child 的父与 Code 原样
            (await _ouRepository.GetAsync(root.Id)).ParentId.ShouldBeNull();
            var childAfter = await _ouRepository.GetAsync(child.Id);
            childAfter.ParentId.ShouldBe(root.Id);
            childAfter.Code.ShouldStartWith((await _ouRepository.GetAsync(root.Id)).Code + ".");
        }
        finally
        {
            await DeleteOuAsync(grandchild.Id);
            await DeleteOuAsync(child.Id);
            await DeleteOuAsync(root.Id);
        }
    }

    [Fact]
    public async Task MoveAsync_Should_Move_To_Root_When_ParentId_Null()
    {
        var root = await CreateOuAsync($"移动根R-{Guid.NewGuid().ToString("N")[..6]}", null);
        var child = await CreateOuAsync($"移动子C-{Guid.NewGuid().ToString("N")[..6]}", root.Id);
        var grandchild = await CreateOuAsync($"移动孙G-{Guid.NewGuid().ToString("N")[..6]}", child.Id);

        try
        {
            // 路由段表达不了的 null 语义：ParentId 空＝提升为根
            await _ouAppService.MoveAsync(child.Id, new MoveOrganizationUnitInput { ParentId = null });

            var moved = await _ouRepository.GetAsync(child.Id);
            moved.ParentId.ShouldBeNull();
            // ABP 根级 Code 恒为单个编码单元（5 位数字），且不再以原父 Code 为前缀
            moved.Code.Length.ShouldBe(5);
            moved.Code.ShouldNotStartWith((await _ouRepository.GetAsync(root.Id)).Code);

            // 子树级联重算：孙子的 Code 必须跟随新前缀，否则层级/数据范围全错且测试不红
            var grandchildAfter = await _ouRepository.GetAsync(grandchild.Id);
            grandchildAfter.Code.ShouldStartWith(moved.Code + ".");
        }
        finally
        {
            await DeleteOuAsync(grandchild.Id);
            await DeleteOuAsync(child.Id);
            await DeleteOuAsync(root.Id);
        }
    }

    [Fact]
    public async Task MoveAsync_Should_Reparent_Code_Prefix_When_Moved_Under_Another_Tree()
    {
        var rootA = await CreateOuAsync($"移动树A-{Guid.NewGuid().ToString("N")[..6]}", null);
        var childA = await CreateOuAsync($"A子-{Guid.NewGuid().ToString("N")[..6]}", rootA.Id);
        var grandchildA = await CreateOuAsync($"A孙-{Guid.NewGuid().ToString("N")[..6]}", childA.Id);
        var rootB = await CreateOuAsync($"移动树B-{Guid.NewGuid().ToString("N")[..6]}", null);

        try
        {
            await _ouAppService.MoveAsync(childA.Id, new MoveOrganizationUnitInput { ParentId = rootB.Id });

            var moved = await _ouRepository.GetAsync(childA.Id);
            moved.ParentId.ShouldBe(rootB.Id);
            var newParentCode = (await _ouRepository.GetAsync(rootB.Id)).Code;
            moved.Code.ShouldStartWith(newParentCode + ".");

            // 子树级联：孙子的 Code 前缀随移动节点一起换到新树
            (await _ouRepository.GetAsync(grandchildA.Id)).Code
                .ShouldStartWith(moved.Code + ".");
        }
        finally
        {
            await DeleteOuAsync(grandchildA.Id);
            await DeleteOuAsync(childA.Id);
            await DeleteOuAsync(rootA.Id);
            await DeleteOuAsync(rootB.Id);
        }
    }
}
