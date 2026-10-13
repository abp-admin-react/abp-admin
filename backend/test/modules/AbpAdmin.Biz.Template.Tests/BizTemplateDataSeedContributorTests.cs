using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 种子开关（第②层 SeedSampleData）行为测试：直接构造贡献者注入两种选项快照。
/// 每步动作各用独立 UoW——与生产一致：种子在独立工作单元里跑，提交时才落库，
/// 幂等守卫（空表才插）跨的是"运行"而不是同一 UoW 内的未落库状态。
/// 测试启动链默认选项为 true（JSON 基线），启动种子已写入一行——先清空再触发。
/// </summary>
public class BizTemplateDataSeedContributorTests : BizTemplateTestBase
{
    [Fact]
    public async Task Seed_Should_Skip_When_SampleData_Disabled()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>()
                .DeleteAsync(x => true, autoSave: true);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var repository = ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>();
            var contributor = new BizTemplateDataSeedContributor(
                repository,
                ServiceProvider.GetRequiredService<IGuidGenerator>(),
                Options.Create(new Configuration.BizTemplateOptions { SeedSampleData = false }));

            await contributor.SeedAsync(new DataSeedContext(null));
        });

        await WithUnitOfWorkAsync(async () =>
        {
            (await ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>()
                .GetCountAsync()).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Seed_Should_Be_Idempotent_When_Enabled()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>()
                .DeleteAsync(x => true, autoSave: true);
        });

        // 两轮种子（两个 UoW = 两次"运行"）：第二轮命中空表守卫，不产生重复行
        for (var round = 0; round < 2; round++)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var contributor = new BizTemplateDataSeedContributor(
                    ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>(),
                    ServiceProvider.GetRequiredService<IGuidGenerator>(),
                    Options.Create(new Configuration.BizTemplateOptions { SeedSampleData = true }));

                await contributor.SeedAsync(new DataSeedContext(null));
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            (await ServiceProvider.GetRequiredService<IRepository<Entities.BizProject, Guid>>()
                .GetCountAsync()).ShouldBe(1);
        });
    }
}
