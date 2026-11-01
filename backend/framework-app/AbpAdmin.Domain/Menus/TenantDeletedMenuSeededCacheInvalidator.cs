using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Menus;

/// <summary>
/// 租户删除时清理 <see cref="MenuManager"/> 的「已播种」进程内短缓存条目
///（事件处理风格参考 Editions 的 EditionChangedTenantCacheInvalidator）。
/// 不清理的问题：条目最多残留一个缓存窗口（1 分钟），期间若同 Id 租户被重建
///（数据恢复/迁移场景），其首访会被残留条目短路跳过懒拷贝，菜单为空且无报错；
/// 同时是纯内存泄漏兜底——租户只删不建时条目永不释放。
/// 说明：本地事件只在本实例生效，多实例部署下其它实例仍靠 1 分钟短过期收敛（可接受）。
/// </summary>
public class TenantDeletedMenuSeededCacheInvalidator :
    ILocalEventHandler<EntityDeletedEventData<Tenant>>,
    ITransientDependency
{
    public virtual Task HandleEventAsync(EntityDeletedEventData<Tenant> eventData)
    {
        MenuManager.ClearSeededCheckCache(eventData.Entity.Id);
        return Task.CompletedTask;
    }
}
