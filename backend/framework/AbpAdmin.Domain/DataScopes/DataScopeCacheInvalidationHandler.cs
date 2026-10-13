using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围缓存失效事件订阅。
/// 监听 OU 变更、RoleDataScope 变更、用户-OU 关联变更、用户-角色关联变更，
/// 任一变更发生时递增 generation，使所有用户的数据范围缓存同时失效。
/// </summary>
public class DataScopeCacheInvalidationHandler :
    ILocalEventHandler<EntityChangedEventData<OrganizationUnit>>,
    ILocalEventHandler<EntityChangedEventData<RoleDataScope>>,
    ILocalEventHandler<EntityChangedEventData<IdentityUserOrganizationUnit>>,
    ILocalEventHandler<EntityChangedEventData<IdentityUserRole>>,
    ITransientDependency
{
    private readonly IDataScopeCacheGeneration _generation;

    public DataScopeCacheInvalidationHandler(IDataScopeCacheGeneration generation)
    {
        _generation = generation;
    }

    public async Task HandleEventAsync(EntityChangedEventData<OrganizationUnit> eventData)
    {
        await _generation.IncrementAsync();
    }

    public async Task HandleEventAsync(EntityChangedEventData<RoleDataScope> eventData)
    {
        await _generation.IncrementAsync();
    }

    public async Task HandleEventAsync(EntityChangedEventData<IdentityUserOrganizationUnit> eventData)
    {
        await _generation.IncrementAsync();
    }

    public async Task HandleEventAsync(EntityChangedEventData<IdentityUserRole> eventData)
    {
        await _generation.IncrementAsync();
    }
}
