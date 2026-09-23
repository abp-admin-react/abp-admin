using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单种子：仅在 Host 上下文播种全局模板（DbMigrator 会对 host 和每个租户各跑一遍种子，
/// 租户上下文跳过——租户菜单走 MenuManager.EnsureTenantMenusAsync 懒拷贝）。
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

        return Task.CompletedTask;
    }
}
