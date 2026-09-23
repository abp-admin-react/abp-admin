using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.DataScopes;
using AbpAdmin.Localization;
using AbpAdmin.Menus;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace AbpAdmin;

/*
 * You can add your own mappings here.
 * [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
 * public partial class AbpAdminApplicationMappers : MapperBase<BookDto, CreateUpdateBookDto>
 * {
 *    public override partial BookDto Map(BookDto source);
 *
 *    public override partial void Map(BookDto source, BookDto destination);
 * }
 */

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class LanguageMappers : MapperBase<Language, LanguageDto>
{
    public override partial LanguageDto Map(Language source);

    public override partial void Map(Language source, LanguageDto destination);
}

// LanguageText → LanguageTextDto 的映射不在这里做：DTO 的 IsOverridden/BaseValue 在实体上
// 没有源成员（Mapperly 对未映射目标只警告不报错），且覆盖行恒 IsOverridden=true 的语义只有
// 服务层知道，统一由 LanguageTextAppService.ToOverrideRowDto 手写映射。

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MenuMappers : MapperBase<Menu, MenuDto>
{
    public override partial MenuDto Map(Menu source);

    public override partial void Map(Menu source, MenuDto destination);
}

/// <summary>
/// RoleDataScope → RoleDataScopeDto：CustomOrganizationUnits（实体集合）→ CustomOrganizationUnitIds（Id 列表）。
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class RoleDataScopeMappers : MapperBase<RoleDataScope, RoleDataScopeDto>
{
    [MapProperty(nameof(RoleDataScope.CustomOrganizationUnits), nameof(RoleDataScopeDto.CustomOrganizationUnitIds))]
    public override partial RoleDataScopeDto Map(RoleDataScope source);

    [MapProperty(nameof(RoleDataScope.CustomOrganizationUnits), nameof(RoleDataScopeDto.CustomOrganizationUnitIds))]
    public override partial void Map(RoleDataScope source, RoleDataScopeDto destination);

    // 用户定义转换：Mapperly 在映射 CustomOrganizationUnits → CustomOrganizationUnitIds 时自动调用
    private List<Guid> ToOrganizationUnitIds(ICollection<RoleDataScopeOrganizationUnit> source)
        => source.Select(x => x.OrganizationUnitId).ToList();
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class DataScopeDemoMappers : MapperBase<DataScopeDemo, DataScopeDemoDto>
{
    public override partial DataScopeDemoDto Map(DataScopeDemo source);

    public override partial void Map(DataScopeDemo source, DataScopeDemoDto destination);
}
