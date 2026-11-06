using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizTemplateClientErrorSamplePermissionTests : BizTemplateTestBase
{
    [Fact]
    public async Task ClientErrorSample_Permission_Should_Be_Defined_Without_Children()
    {
        var manager = ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var groups = await manager.GetGroupsAsync();
        var group = groups.Single(item => item.Name == BizTemplatePermissions.GroupName);

        var permission = group.Permissions.SingleOrDefault(
            item => item.Name == BizTemplatePermissions.ClientErrorSample.Default);

        permission.ShouldNotBeNull();
        permission!.Children.ShouldBeEmpty();
    }
}
