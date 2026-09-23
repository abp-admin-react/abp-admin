using System;
using System.Threading.Tasks;
using AbpAdmin.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// T2.8 SaaS Pro 缺口：租户激活状态校验（TenantActivationChecker）。
/// 覆盖三态与到期判定；异常错误码决定登录拒绝时的提示文案。
/// </summary>
public abstract class TenantActivationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly TenantActivationChecker _checker;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;

    protected TenantActivationTests()
    {
        _checker = GetRequiredService<TenantActivationChecker>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
    }

    private async Task<Tenant> CreateAndRefetchTenantAsync(Action<Tenant>? configure = null)
    {
        var tenantId = await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync("t" + Guid.NewGuid().ToString("N")[..12]);
            configure?.Invoke(tenant);
            await _tenantRepository.InsertAsync(tenant);
            return tenant.Id;
        });

        // 重新从库读取，走 EF 影子属性 → ExtraProperties 的真实填充路径
        return await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
    }

    [Fact]
    public async Task Should_Pass_When_Active_By_Default()
    {
        var tenant = await CreateAndRefetchTenantAsync();

        Should.NotThrow(() => _checker.Check(tenant));
    }

    [Fact]
    public async Task Should_Throw_When_Passive()
    {
        var tenant = await CreateAndRefetchTenantAsync(t =>
            t.SetProperty(AbpAdminTenantConsts.ActivationStatePropertyName, TenantActivationStateEnum.Passive));

        var exception = Should.Throw<BusinessException>(() => _checker.Check(tenant));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.TenantIsPassive);
    }

    [Fact]
    public async Task Should_Throw_When_Limited_And_Expired()
    {
        var tenant = await CreateAndRefetchTenantAsync(t =>
        {
            t.SetProperty(AbpAdminTenantConsts.ActivationStatePropertyName, TenantActivationStateEnum.ActiveWithLimitedTime);
            t.SetProperty(AbpAdminTenantConsts.ActivationEndDatePropertyName, DateTime.UtcNow.AddDays(-1));
        });

        var exception = Should.Throw<BusinessException>(() => _checker.Check(tenant));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.TenantActivationExpired);
    }

    [Fact]
    public async Task Should_Pass_When_Limited_And_Not_Expired()
    {
        var tenant = await CreateAndRefetchTenantAsync(t =>
        {
            t.SetProperty(AbpAdminTenantConsts.ActivationStatePropertyName, TenantActivationStateEnum.ActiveWithLimitedTime);
            t.SetProperty(AbpAdminTenantConsts.ActivationEndDatePropertyName, DateTime.UtcNow.AddDays(7));
        });

        Should.NotThrow(() => _checker.Check(tenant));
    }

    [Fact]
    public async Task Should_Pass_When_Limited_Without_EndDate()
    {
        var tenant = await CreateAndRefetchTenantAsync(t =>
            t.SetProperty(AbpAdminTenantConsts.ActivationStatePropertyName, TenantActivationStateEnum.ActiveWithLimitedTime));

        Should.NotThrow(() => _checker.Check(tenant));
    }
}
