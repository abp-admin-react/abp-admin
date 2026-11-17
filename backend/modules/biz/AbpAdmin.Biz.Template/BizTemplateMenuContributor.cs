using System.Collections.Generic;
using AbpAdmin.Menus;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template;

[ExposeServices(typeof(IMenuTemplateContributor))]
public sealed class BizTemplateMenuContributor : IMenuTemplateContributor, ITransientDependency
{
    public IEnumerable<MenuDefinition> GetMenus() =>
    [
        new("biz-template-data-scope", "demos", MenuTypeEnum.Menu, "数据权限示例", "biz-template-data-scope",
            "/biz-template/data-scope", "apartment", 252, "BizTemplate.DataScopeDemo", IsHide: true)
    ];
}
