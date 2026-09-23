using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>TryAcquireAsync 恒返回 null——模拟分布式锁持续超时（持锁方拷贝异常缓慢 / 锁竞争激烈）。</summary>
public class AlwaysTimeoutDistributedLock : IAbpDistributedLock
{
    public Task<IAbpDistributedLockHandle?> TryAcquireAsync(
        string name,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IAbpDistributedLockHandle?>(null);
    }
}

/// <summary>
/// 把 IAbpDistributedLock 换成恒超时桩：覆盖 EnsureTenantMenusAsync 的「锁超时裸拷贝」分支。
/// 默认进程内 LocalAbpDistributedLock 恒可获取，常规菜单测试永远走不到该分支——
/// 若删掉裸拷贝代码（或锁用法改坏），本模块下的测试会失败而常规测试仍然全绿。
/// </summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class MenuLazyCopyLockTimeoutTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(
            ServiceDescriptor.Singleton<IAbpDistributedLock, AlwaysTimeoutDistributedLock>());
    }
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreMenuLazyCopyLockTimeoutTests : AbpAdminApplicationTestBase<MenuLazyCopyLockTimeoutTestModule>
{
    /// <summary>
    /// 锁超时裸拷贝：拷贝仍须完整完成（「宁可 500 不可脏数据」以拷贝功能本身可用为前提），
    /// 且重复调用幂等（count > 0 短路，不重复拷贝）。数据来源是 Host 模板，先播种保证非空。
    /// </summary>
    [Fact]
    public async Task EnsureTenantMenusAsync_With_Lock_Timeout_Should_Bare_Copy_Idempotently()
    {
        var menuManager = GetRequiredService<MenuManager>();
        var menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
        var currentTenant = GetRequiredService<ICurrentTenant>();

        await WithUnitOfWorkAsync(() => menuManager.SeedHostTemplateAsync());

        var tenantId = Guid.NewGuid();
        using (currentTenant.Change(tenantId))
        {
            await WithUnitOfWorkAsync(() => menuManager.EnsureTenantMenusAsync(tenantId));
            var count = await WithUnitOfWorkAsync(() => menuRepository.GetCountAsync());
            count.ShouldBeGreaterThan(0);

            // 二次调用：计数短路，幂等不重复拷贝
            await WithUnitOfWorkAsync(() => menuManager.EnsureTenantMenusAsync(tenantId));
            (await WithUnitOfWorkAsync(() => menuRepository.GetCountAsync())).ShouldBe(count);
        }
    }
}
