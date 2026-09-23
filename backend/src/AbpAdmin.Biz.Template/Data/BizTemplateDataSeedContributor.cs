using System;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 模块种子：随框架 IDataSeeder 自动执行，幂等（空表才插）。
/// </summary>
public class BizTemplateDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<BizProject, Guid> _bizProjectRepository;
    private readonly IGuidGenerator _guidGenerator;

    public BizTemplateDataSeedContributor(
        IRepository<BizProject, Guid> bizProjectRepository,
        IGuidGenerator guidGenerator)
    {
        _bizProjectRepository = bizProjectRepository;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
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
