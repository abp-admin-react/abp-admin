using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.ClientErrors;
using AbpAdmin.Biz.Template.Entities;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 仓储查询的唯一共享点。调用方传入数据范围（ownerUserId），此处不 Disable 任何过滤器。
/// </summary>
public class BizProjectAccessService : IBizProjectAccessService, ITransientDependency
{
    private readonly IRepository<BizProject, Guid> _bizProjectRepository;

    public BizProjectAccessService(IRepository<BizProject, Guid> bizProjectRepository)
    {
        _bizProjectRepository = bizProjectRepository;
    }

    public async Task<BizProject> GetRequiredAsync(Guid id)
    {
        var project = await FindAsync(id);
        if (project is null)
        {
            throw new EntityNotFoundException(typeof(BizProject), id);
        }

        return project;
    }

    public async Task<BizProject> GetOwnedActiveAsync(Guid id, Guid ownerUserId)
    {
        var project = await FindAsync(id);
        if (project is null || project.CreatorId != ownerUserId || !project.IsActive)
        {
            throw CreateUnavailable();
        }

        return project;
    }

    public async Task<List<BizProject>> GetOwnedListAsync(Guid ownerUserId)
    {
        var items = await _bizProjectRepository.GetListAsync(
            project => project.CreatorId == ownerUserId && project.IsActive);
        return items
            .OrderByDescending(project => project.CreationTime)
            .ToList();
    }

    private Task<BizProject?> FindAsync(Guid id)
    {
        return _bizProjectRepository.FindAsync(id);
    }

    private static BusinessException CreateUnavailable()
    {
        return new BusinessException(BizTemplateClientErrorCodes.ProjectUnavailable);
    }
}
