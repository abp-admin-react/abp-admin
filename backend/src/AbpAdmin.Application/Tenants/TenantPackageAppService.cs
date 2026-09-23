using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户套餐管理（Host 专属，借鉴芋道"租户套餐"）。套餐 = 全局菜单模板的勾选子集；
/// 应用到租户时按套餐过滤拷贝模板（见 <see cref="MenuManager.EnsureTenantMenusAsync"/>）。
/// </summary>
[Authorize(AbpAdminPermissions.TenantPackages.Default)]
public class TenantPackageAppService : AbpAdminAppService, ITenantPackageAppService
{
    private readonly IRepository<TenantPackage, Guid> _packageRepository;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IDataFilter _dataFilter;

    public TenantPackageAppService(
        IRepository<TenantPackage, Guid> packageRepository,
        IRepository<Menu, Guid> menuRepository,
        IDataFilter dataFilter)
    {
        _packageRepository = packageRepository;
        _menuRepository = menuRepository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<TenantPackageDto>> GetListAsync(TenantPackageListInput input)
    {
        EnsureHostSide();

        // 带 Menus 子集合：MenuCount 才有值（默认查询不加载聚合子集合）
        var query = await _packageRepository.WithDetailsAsync(x => x.Menus);
        var filter = input.Filter?.Trim();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x => x.Name.Contains(filter));
        }

        var total = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.OrderBy(x => x.Name).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TenantPackageDto>(
            total,
            items.Select(x => ToDto(x)).ToList());
    }

    public virtual async Task<TenantPackageDto> GetAsync(Guid id)
    {
        EnsureHostSide();
        return ToDto(await GetPackageAsync(id));
    }

    [Authorize(AbpAdminPermissions.TenantPackages.Create)]
    public virtual async Task<TenantPackageDto> CreateAsync(TenantPackageCreateDto input)
    {
        EnsureHostSide();
        await EnsureNameUniqueAsync(input.Name);

        var package = new TenantPackage(GuidGenerator.Create(), input.Name, input.Remark);
        await _packageRepository.InsertAsync(package, autoSave: true);
        return ToDto(package);
    }

    [Authorize(AbpAdminPermissions.TenantPackages.Update)]
    public virtual async Task<TenantPackageDto> UpdateAsync(Guid id, TenantPackageUpdateDto input)
    {
        EnsureHostSide();
        var package = await GetPackageAsync(id);
        if (package.Name != input.Name)
        {
            await EnsureNameUniqueAsync(input.Name, excludeId: id);
            package.SetName(input.Name);
        }

        package.Remark = input.Remark;
        await _packageRepository.UpdateAsync(package, autoSave: true);
        return ToDto(package);
    }

    [Authorize(AbpAdminPermissions.TenantPackages.Delete)]
    [OperationLog("租户套餐", "删除套餐", BizNo = "{{id}}", Success = "删除了租户套餐「{{tenantPackage(id)}}」")]
    public virtual async Task DeleteAsync(Guid id)
    {
        EnsureHostSide();
        var package = await GetPackageAsync(id);
        await _packageRepository.DeleteAsync(package);
    }

    public virtual async Task<TenantPackageMenuSelectionDto> GetMenuSelectionAsync(Guid id)
    {
        EnsureHostSide();
        var package = await GetPackageAsync(id);

        var templateMenus = await GetHostTemplateMenusAsync();

        // 复用 MenuTreeBuilder：与菜单管理页（MenuAppService.GetTreeAsync）同一套树构建
        // 与排序/悬空节点上浮语义，DTO 多出的 TenantId/Remark/GrantedRoles 字段对勾选树无影响
        var roots = MenuTreeBuilder.Build(templateMenus);

        return new TenantPackageMenuSelectionDto
        {
            Tree = roots,
            CheckedMenuIds = package.Menus.Select(x => x.TemplateMenuId).ToList()
        };
    }

    [Authorize(AbpAdminPermissions.TenantPackages.Update)]
    [OperationLog("租户套餐", "调整菜单勾选", BizNo = "{{id}}", Success = "调整了租户套餐「{{tenantPackage(id)}}」的菜单勾选")]
    public virtual async Task UpdateMenuSelectionAsync(Guid id, UpdateTenantPackageMenusDto input)
    {
        EnsureHostSide();
        var package = await GetPackageAsync(id);

        // 勾选的必须是 Host 模板节点
        var templateMenus = await GetHostTemplateMenusAsync();

        var templateIds = templateMenus.Select(x => x.Id).ToHashSet();
        var invalid = input.MenuIds.Where(x => !templateIds.Contains(x)).ToList();
        if (invalid.Count > 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.TenantPackages.InvalidTemplateMenu)
                .WithData("Count", invalid.Count);
        }

        package.SetMenus(input.MenuIds, GuidGenerator);
        await _packageRepository.UpdateAsync(package, autoSave: true);
    }

    /// <summary>禁用多租户过滤读取 Host 模板菜单（套餐勾选的两个入口共用）。</summary>
    private async Task<List<Menu>> GetHostTemplateMenusAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await _menuRepository.GetListAsync(x => x.TenantId == null);
        }
    }

    private async Task<TenantPackage> GetPackageAsync(Guid id)
    {
        // 显式 Include 子集合：默认 FindAsync 不保证加载 Menus，
        // 未加载的集合改动了 EF 也检测不到（Backing field 快照缺失）
        var query = await _packageRepository.WithDetailsAsync(x => x.Menus);
        var package = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == id));
        if (package == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.TenantPackages.TenantPackageNotFound)
                .WithData("Id", id);
        }

        return package;
    }

    private async Task EnsureNameUniqueAsync(string name, Guid? excludeId = null)
    {
        var existing = await _packageRepository.FindAsync(x => x.Name == name && x.Id != excludeId);
        if (existing != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.TenantPackages.TenantPackageNameDuplicate)
                .WithData("Name", name);
        }
    }

    private static TenantPackageDto ToDto(TenantPackage package) => new()
    {
        Id = package.Id,
        Name = package.Name,
        Remark = package.Remark,
        MenuCount = package.Menus.Count
    };
}
