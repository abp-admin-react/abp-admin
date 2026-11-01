using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 权限定义契约（随模块复制）。测试基座 AddAlwaysAllowAuthorization 让 [Authorize] 拒绝路径
/// 在本工程不可达，但权限"定义完整性"可静态断言：组、父权限、三个子权限挂接正确——
/// 复制改名时权限常量打错字、子权限漏定义，在此变红而不是派生项目运行时按钮失灵。
/// </summary>
public class BizTemplatePermissionDefinitionTests : BizTemplateTestBase
{
    [Fact]
    public async Task BizTemplate_Group_Should_Define_Projects_With_Children()
    {
        var manager = ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var groups = await manager.GetGroupsAsync();

        var group = groups.SingleOrDefault(g => g.Name == BizTemplatePermissions.GroupName);
        Assert.NotNull(group);

        var projects = group!.Permissions.SingleOrDefault(p => p.Name == BizTemplatePermissions.Projects.Default);
        Assert.NotNull(projects);

        var children = projects!.Children.Select(c => c.Name).ToList();
        Assert.Equal(
            new[]
            {
                BizTemplatePermissions.Projects.Create,
                BizTemplatePermissions.Projects.Update,
                BizTemplatePermissions.Projects.Delete
            },
            children);
    }
}
