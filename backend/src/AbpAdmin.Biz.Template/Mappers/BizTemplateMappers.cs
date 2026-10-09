using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Services.Dtos;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace AbpAdmin.Biz.Template.Mappers;

/// <summary>
/// 实体 → DTO（Mapperly 源生成，与框架 AbpAdminApplicationMappers 同一约定）。
/// DTO → 实体方向不生成映射：实体带审计字段做不了 Target 全映射，
/// 创建/更新的落值在 AppService 显式赋值，样板保持直白。
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BizProjectToDtoMapper : MapperBase<BizProject, BizProjectDto>
{
    public override partial BizProjectDto Map(BizProject source);

    public override partial void Map(BizProject source, BizProjectDto destination);
}

/// <summary>数据权限示例实体的 DTO 映射（同上约定）。</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BizDataScopeDemoToDtoMapper : MapperBase<BizDataScopeDemo, BizDataScopeDemoDto>
{
    public override partial BizDataScopeDemoDto Map(BizDataScopeDemo source);

    public override partial void Map(BizDataScopeDemo source, BizDataScopeDemoDto destination);
}
