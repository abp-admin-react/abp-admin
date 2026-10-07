using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.PermissionManagement;

/* 权限定义运行时管理测试。
 * 核心验收：运行时写入的定义要能被 IPermissionDefinitionManager 解析到
 * （证明 stamp 失效 → DynamicPermissionDefinitionStore 重读这条链路真实打通），
 * 以及静态遮蔽/父子约束等防护语义。测试库 collection 内共享，用唯一名隔离。
 */
public abstract class PermissionDefinitionManagementAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IPermissionDefinitionManagementAppService _appService;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;

    protected PermissionDefinitionManagementAppServiceTests()
    {
        _appService = GetRequiredService<IPermissionDefinitionManagementAppService>();
        _permissionDefinitionManager = GetRequiredService<IPermissionDefinitionManager>();
    }

    [Fact]
    public async Task CreateGroup_And_Definition_Should_Be_Visible_To_Runtime_Store()
    {
        var groupName = $"TestPermGroup_{Guid.NewGuid():N}";
        var permName = $"TestPerm.{groupName}.Approve";

        var group = await _appService.CreateGroupAsync(new CreatePermissionGroupInput
        {
            Name = groupName,
            DisplayName = "Test Permission Group",
        });

        var definition = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = groupName,
            Name = permName,
            DisplayName = "Approve orders",
        });

        // 运行时定义存储（stamp 失效后重读）能解析到新权限
        var resolved = await _permissionDefinitionManager.GetOrNullAsync(permName);
        resolved.ShouldNotBeNull();

        // 列表可见
        var groups = await _appService.GetGroupsAsync(new GetPermissionGroupListInput { Filter = groupName });
        groups.TotalCount.ShouldBe(1);
        groups.Items[0].Name.ShouldBe(groupName);

        var definitions = await _appService.GetDefinitionsAsync(new GetPermissionDefinitionListInput { GroupName = groupName });
        definitions.TotalCount.ShouldBe(1);
        definitions.Items[0].Name.ShouldBe(permName);
        definitions.Items[0].IsEnabled.ShouldBeTrue();

        // 清理（也验证删除链路）
        await _appService.DeleteDefinitionAsync(definition.Id);
        (await _permissionDefinitionManager.GetOrNullAsync(permName)).ShouldBeNull();
        await _appService.DeleteGroupAsync(group.Id);
    }

    [Fact]
    public async Task CreateGroup_With_Duplicate_Name_Should_Be_Rejected()
    {
        var groupName = $"TestPermGroup_{Guid.NewGuid():N}";
        await _appService.CreateGroupAsync(new CreatePermissionGroupInput
        {
            Name = groupName,
            DisplayName = "First",
        });

        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _appService.CreateGroupAsync(new CreatePermissionGroupInput
            {
                Name = groupName,
                DisplayName = "Second",
            }));

        exception.Message.ShouldContain(groupName);
    }

    [Fact]
    public async Task CreateDefinition_Shadowing_Static_Name_Should_Be_Rejected()
    {
        // AbpAdmin.Sessions 是代码内定义的静态权限
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
            {
                GroupName = "AbpAdmin",
                Name = "AbpAdmin.Sessions",
                DisplayName = "shadow",
            }));

        exception.Message.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateDefinition_Under_Static_Group_Should_Be_Allowed()
    {
        var permName = $"TestPerm.StaticGroupAttach.{Guid.NewGuid():N}";
        // 组名用静态定义里存在的组（AbpAdmin），定义挂到静态组下
        var definition = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = "AbpAdmin",
            Name = permName,
            DisplayName = "Attached to static group",
        });

        definition.GroupName.ShouldBe("AbpAdmin");
        await _appService.DeleteDefinitionAsync(definition.Id);
    }

    [Fact]
    public async Task DeleteGroup_With_Definitions_Should_Be_Rejected()
    {
        var groupName = $"TestPermGroup_{Guid.NewGuid():N}";
        var group = await _appService.CreateGroupAsync(new CreatePermissionGroupInput
        {
            Name = groupName,
            DisplayName = "G",
        });
        var definition = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = groupName,
            Name = $"TestPerm.{groupName}.Do",
            DisplayName = "Do",
        });

        await Should.ThrowAsync<UserFriendlyException>(() => _appService.DeleteGroupAsync(group.Id));

        await _appService.DeleteDefinitionAsync(definition.Id);
        await _appService.DeleteGroupAsync(group.Id);
    }

    [Fact]
    public async Task DeleteDefinition_With_Children_Should_Be_Rejected()
    {
        var groupName = $"TestPermGroup_{Guid.NewGuid():N}";
        var group = await _appService.CreateGroupAsync(new CreatePermissionGroupInput
        {
            Name = groupName,
            DisplayName = "G",
        });
        var parent = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = groupName,
            Name = $"TestPerm.{groupName}.Parent",
            DisplayName = "Parent",
        });
        var child = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = groupName,
            Name = $"TestPerm.{groupName}.Parent.Child",
            ParentName = $"TestPerm.{groupName}.Parent",
            DisplayName = "Child",
        });

        await Should.ThrowAsync<UserFriendlyException>(() => _appService.DeleteDefinitionAsync(parent.Id));

        // 先删子、再删父（顺序约束的另一半）
        await _appService.DeleteDefinitionAsync(child.Id);
        await _appService.DeleteDefinitionAsync(parent.Id);
        await _appService.DeleteGroupAsync(group.Id);
    }

    [Fact]
    public async Task UpdateDefinition_Should_Persist_And_Resolve_DisplayName()
    {
        var groupName = $"TestPermGroup_{Guid.NewGuid():N}";
        var permName = $"TestPerm.{groupName}.Edit";
        await _appService.CreateGroupAsync(new CreatePermissionGroupInput { Name = groupName, DisplayName = "G" });
        var definition = await _appService.CreateDefinitionAsync(new CreatePermissionDefinitionInput
        {
            GroupName = groupName,
            Name = permName,
            DisplayName = "Before",
        });

        var updated = await _appService.UpdateDefinitionAsync(definition.Id, new UpdatePermissionDefinitionInput
        {
            DisplayName = "After",
            IsEnabled = false,
        });

        updated.DisplayNameLocalized.ShouldBe("After");
        updated.IsEnabled.ShouldBeFalse();

        var resolved = await _permissionDefinitionManager.GetOrNullAsync(permName);
        resolved.ShouldNotBeNull();
        resolved.IsEnabled.ShouldBeFalse();

        await _appService.DeleteDefinitionAsync(definition.Id);
        await DeleteGroupByNameAsync(groupName);
    }

    private async Task DeleteGroupByNameAsync(string groupName)
    {
        var groups = await _appService.GetGroupsAsync(new GetPermissionGroupListInput { Filter = groupName });
        foreach (var g in groups.Items.Where(x => x.Name == groupName))
        {
            await _appService.DeleteGroupAsync(g.Id);
        }
    }
}
