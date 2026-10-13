using System.Threading.Tasks;
using AbpAdmin.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;

namespace AbpAdmin.Editions;

/// <summary>
/// 版本删除时清理挂在它名下的功能值（T2.8 SaaS Pro 缺口）：
/// AbpFeatureValues 中 ProviderName="E"、ProviderKey=版本 Id 的行若不清理，
/// 版本删除后成为孤儿数据。删除与缓存失效策略收敛在 <see cref="FeatureValueCleanupService"/>。
///
/// 本地实体事件在删除方的 UoW 内发布：清理与版本删除同一事务提交（PostgreSQL 原子），
/// 事务回滚则清理一并回滚。缓存失效 considerUow 同样推迟到提交后。
/// </summary>
public class EditionDeletedFeatureValueCleanupHandler :
    ILocalEventHandler<EntityDeletedEventData<Edition>>,
    ITransientDependency
{
    private readonly FeatureValueCleanupService _featureValueCleanupService;

    public EditionDeletedFeatureValueCleanupHandler(
        FeatureValueCleanupService featureValueCleanupService)
    {
        _featureValueCleanupService = featureValueCleanupService;
    }

    public virtual async Task HandleEventAsync(EntityDeletedEventData<Edition> eventData)
    {
        await _featureValueCleanupService.DeleteAllAsync(
            Volo.Abp.Features.EditionFeatureValueProvider.ProviderName,
            eventData.Entity.Id.ToString());
    }
}
