using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AbpAdmin.Menus;

/// <summary>
/// 播种宿主模板并升级已有租户快照。首次创建租户菜单仍由套餐复制流程处理。
/// </summary>
public class MenuDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly MenuManager _menuManager;

    public MenuDataSeedContributor(MenuManager menuManager)
    {
        _menuManager = menuManager;
    }

    [UnitOfWork]
    public virtual Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return _menuManager.SeedHostTemplateAsync();
        }

        return _menuManager.UpgradeTenantTemplateAsync(context.TenantId.Value);
    }
}
