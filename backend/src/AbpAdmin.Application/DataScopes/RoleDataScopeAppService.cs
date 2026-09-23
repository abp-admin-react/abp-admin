using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.DataScopes;

[Authorize(AbpAdminPermissions.DataScopes.Manage)]
public class RoleDataScopeAppService : ApplicationService, IRoleDataScopeAppService
{
    private readonly IRepository<RoleDataScope, Guid> _repository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<OrganizationUnit, Guid> _ouQueryRepository;

    public RoleDataScopeAppService(
        IRepository<RoleDataScope, Guid> repository,
        IIdentityRoleRepository roleRepository,
        IRepository<OrganizationUnit, Guid> ouQueryRepository)
    {
        _repository = repository;
        _roleRepository = roleRepository;
        _ouQueryRepository = ouQueryRepository;
    }

    public async Task<PagedResultDto<RoleDataScopeDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var queryable = await _repository.WithDetailsAsync(x => x.CustomOrganizationUnits);

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        var items = await AsyncExecuter.ToListAsync(
            queryable
                .OrderBy(input.Sorting ?? nameof(RoleDataScope.RoleName))
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount)
        );

        return new PagedResultDto<RoleDataScopeDto>(
            totalCount,
            items.Select(MapToDto).ToList()
        );
    }

    public async Task<RoleDataScopeDto> GetAsync(Guid id)
    {
        var queryable = await _repository.WithDetailsAsync(x => x.CustomOrganizationUnits);
        var entity = await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.Id == id));
        if (entity == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeNotFound)
                .WithData("Id", id);
        }
        return MapToDto(entity);
    }

    public async Task<RoleDataScopeDto> GetByRoleNameAsync(string roleName)
    {
        var queryable = await _repository.WithDetailsAsync(x => x.CustomOrganizationUnits);
        var entity = await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.RoleName == roleName));
        if (entity == null)
        {
            // 未配置过数据范围的角色视为「全部数据」，返回 404 让前端走默认逻辑
            throw new EntityNotFoundException(typeof(RoleDataScope), roleName);
        }
        return MapToDto(entity);
    }

    public async Task<RoleDataScopeDto> CreateAsync(CreateUpdateRoleDataScopeDto input)
    {
        // 先取规范角色名再用它查重：落库存的是规范 Name，若用客户端原样大小写查重，
        // 「Admin」在库中只有「admin」记录时可绕过应用层检查、落库成「admin」直撞
        // 唯一索引变成原始 500（见代码审查 round1）。
        var canonicalRoleName = await GetCanonicalRoleNameAsync(input.RoleName);

        var existing = await _repository.FirstOrDefaultAsync(x => x.RoleName == canonicalRoleName);
        if (existing != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeAlreadyExists)
                .WithData("RoleName", canonicalRoleName);
        }

        // 角色名必须是当前租户真实存在的角色，防止「拼错即失效」的脏配置；
        // 落库用角色的规范 Name（而非客户端原样大小写）——与 MenuAppService.UpdateRoleGrantsAsync
        // 同一口径：读取侧 CurrentDataScopeProvider 用角色 claim 的原始 Name 做精确匹配，
        // 原样存入会在大小写不一致时永远匹配不上（fail-closed 零行可见）
        var entity = new RoleDataScope(
            GuidGenerator.Create(),
            canonicalRoleName,
            input.ScopeType
        );

        await ValidateOrganizationUnitsAsync(input);
        ApplyCustomOrganizationUnits(entity, input);

        await _repository.InsertAsync(entity);
        return MapToDto(entity);
    }

    public async Task<RoleDataScopeDto> UpdateAsync(Guid id, CreateUpdateRoleDataScopeDto input)
    {
        var queryable = await _repository.WithDetailsAsync(x => x.CustomOrganizationUnits);
        var entity = await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.Id == id));
        if (entity == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeNotFound)
                .WithData("Id", id);
        }

        // RoleName 是配置的键（一条记录一个角色）：更新时校验输入角色存在，
        // 且规范名必须与记录一致——改挂到别的角色名下应去编辑那条角色自己的记录
        var canonicalRoleName = await GetCanonicalRoleNameAsync(input.RoleName);
        if (canonicalRoleName != entity.RoleName)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeAlreadyExists)
                .WithData("RoleName", canonicalRoleName);
        }

        entity.SetScopeType(input.ScopeType);

        await ValidateOrganizationUnitsAsync(input);
        // 清空旧自定义组织，重新设置
        ApplyCustomOrganizationUnits(entity, input);

        await _repository.UpdateAsync(entity);
        return MapToDto(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    /// <summary>校验角色存在并返回规范 Name（FindByNormalizedNameAsync 大小写归一查询）。</summary>
    private async Task<string> GetCanonicalRoleNameAsync(string roleName)
    {
        var role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        if (role == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeRoleNotFound)
                .WithData("RoleName", roleName);
        }

        return role.Name!;
    }

    /// <summary>
    /// Custom 范围的组织单元必须真实存在：存入幽灵 Id 后解析侧会静默收敛为空集合
    /// （fail-closed 零行可见）且管理端无任何提示，故在写入前批量校验。
    /// </summary>
    private async Task ValidateOrganizationUnitsAsync(CreateUpdateRoleDataScopeDto input)
    {
        if (input.ScopeType != DataScopeTypeEnum.Custom || input.CustomOrganizationUnitIds.Count == 0)
        {
            return;
        }

        var distinctIds = input.CustomOrganizationUnitIds.Distinct().ToList();
        var existingCount = await _ouQueryRepository.CountAsync(x => distinctIds.Contains(x.Id));
        if (existingCount != distinctIds.Count)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeOrganizationUnitNotFound)
                .WithData("Count", distinctIds.Count - existingCount);
        }
    }

    /// <summary>Create/Update 共用：把输入的自定义组织单元应用到实体（去重、全量替换）。</summary>
    private static void ApplyCustomOrganizationUnits(RoleDataScope entity, CreateUpdateRoleDataScopeDto input)
    {
        entity.CustomOrganizationUnits.Clear();
        if (input.ScopeType != DataScopeTypeEnum.Custom)
        {
            return;
        }

        foreach (var ouId in input.CustomOrganizationUnitIds.Distinct())
        {
            entity.CustomOrganizationUnits.Add(new RoleDataScopeOrganizationUnit(entity.Id, ouId));
        }
    }

    private RoleDataScopeDto MapToDto(RoleDataScope entity)
    {
        return ObjectMapper.Map<RoleDataScope, RoleDataScopeDto>(entity);
    }
}
