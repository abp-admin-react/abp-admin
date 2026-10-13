using System;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Configuration;
using AbpAdmin.Biz.Template.Entities;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 模块种子：随框架 IDataSeeder 自动执行，幂等（空表才插）。
/// 样例数据写入受第②层部署期配置（BizTemplateOptions.SeedSampleData，模块自带 JSON +
/// 环境变量 BizTemplate__SeedSampleData 可覆盖）控制——生产环境置 false 即不产生演示数据。
/// </summary>
public class BizTemplateDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<BizProject, Guid> _bizProjectRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly BizTemplateOptions _bizTemplateOptions;

    public BizTemplateDataSeedContributor(
        IRepository<BizProject, Guid> bizProjectRepository,
        IGuidGenerator guidGenerator,
        IOptions<BizTemplateOptions> bizTemplateOptions)
    {
        _bizProjectRepository = bizProjectRepository;
        _guidGenerator = guidGenerator;
        _bizTemplateOptions = bizTemplateOptions.Value;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // 第②层（部署期配置）：生产/演示口径开关，改配置需重启——种子只在迁移时跑，正合适
        if (!_bizTemplateOptions.SeedSampleData)
        {
            return;
        }

        if (await _bizProjectRepository.GetCountAsync() > 0)
        {
            return;
        }

        await _bizProjectRepository.InsertAsync(
            new BizProject(
                _guidGenerator.Create(),
                "示例项目",
                "AbpAdmin.Biz.Template 模块种子数据：复制本模块改名后替换为真实业务。"));
    }
}
