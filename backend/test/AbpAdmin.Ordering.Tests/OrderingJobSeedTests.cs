using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 作业种子与 Host 专属守卫（计划第 2、8 节）：本模块的作业种子在 context.TenantId != null 时
/// 直接返回——只给 Host 插行，默认停用；测试断言租户上下文不会产生这些行
///（关多租户过滤器直查，避免过滤器把租户行藏起来造成假绿），
/// 且 JobType 已加入 HostOnlyScheduledJobTypes.All（ScheduledJobAppService 四处守卫依赖它）。
/// </summary>
public class OrderingJobSeedTests : OrderingTestBase
{
    [Fact]
    public async Task Host_Seed_Inserts_Job_Row_Disabled()
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDataSeeder>()
                .SeedAsync(new DataSeedContext(tenantId: null));
        }

        var job = await GetJobRowAsync(null, ScheduledJobConsts.OrderStatusSyncJobType);
        job.ShouldNotBeNull();
        job.IsEnabled.ShouldBeFalse(); // 默认停用：上线时由人打开，不在种子里打开
        job.TenantId.ShouldBeNull();
    }

    [Fact]
    public async Task Tenant_Seed_Produces_No_Job_Row()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using (var scope = ServiceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDataSeeder>()
                .SeedAsync(new DataSeedContext(tenantId: tenantId));
        }

        // 关过滤器直查：租户上下文不产生任何行（含 Host 行）
        (await GetJobRowAsync(tenantId, ScheduledJobConsts.OrderStatusSyncJobType)).ShouldBeNull();
    }

    [Fact]
    public void Job_Type_Is_Registered_In_HostOnly_Guard_Set()
    {
        HostOnlyScheduledJobTypes.All.ShouldContain(ScheduledJobConsts.OrderStatusSyncJobType);
    }

    private async Task<AbpAdmin.ScheduledJobs.ScheduledJob?> GetJobRowAsync(Guid? tenantId, string jobType)
    {
        using var uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin();
        var repository = ServiceProvider.GetRequiredService<IRepository<AbpAdmin.ScheduledJobs.ScheduledJob, Guid>>();
        using (ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
        {
            var rows = await repository.GetListAsync(j => j.JobType == jobType);
            await uow.CompleteAsync();
            return rows.FirstOrDefault(j => j.TenantId == tenantId);
        }
    }
}
