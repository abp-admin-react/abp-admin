using System;
using System.Threading.Tasks;
using AbpAdmin.Editions;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Volo.Abp.TenantManagement;
using Xunit;
using TenantAppService = AbpAdmin.Tenants.TenantAppService;

namespace AbpAdmin.Saas;

/// <summary>
/// 版本分配扩展属性（EditionConsts.TenantEditionPropertyName = "EditionId"）契约：
/// set-tenant-edition 写入实体 ExtraProperties 后，租户 DTO 必须把它读回 extraProperties
/// （租户列表"版本"列与 TenantEditionForm 回显的唯一数据源）。
/// 缺陷史：AbpAdminModuleExtensionConfigurator 未注册该扩展属性时，ABP DTO 序列化剥掉此键，
/// 管理员在列表与编辑回显中永远看不到版本分配（SAAS 测试计划 A-TN-04 实测缺陷）。
/// </summary>
public abstract class TenantEditionExtensionPropertyTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IEditionAppService _editionAppService;
    private readonly TenantAppService _tenantAppService;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    protected TenantEditionExtensionPropertyTests()
    {
        _editionAppService = GetRequiredService<IEditionAppService>();
        _tenantAppService = GetRequiredService<TenantAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<ITenantManager>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task SetTenantEdition_Should_Roundtrip_Via_TenantDto_ExtraProperties()
    {
        var edition = await _editionAppService.CreateAsync(
            new CreateEditionDto { DisplayName = "ext-prop-edition" });

        Guid tenantId;
        using (var uow = _unitOfWorkManager.Begin())
        {
            var tenant = await _tenantRepository.InsertAsync(
                await _tenantManager.CreateAsync("ext-prop-tenant"), autoSave: true);
            tenantId = tenant.Id;
            await uow.CompleteAsync();
        }

        await _editionAppService.SetTenantEditionAsync(tenantId, edition.Id);

        var dto = await _tenantAppService.GetAsync(tenantId);
        dto.ExtraProperties.ShouldContainKey(EditionConsts.TenantEditionPropertyName);
        dto.ExtraProperties[EditionConsts.TenantEditionPropertyName]!
            .ToString()!.ToLowerInvariant()
            .ShouldBe(edition.Id.ToString().ToLowerInvariant());
    }
}
