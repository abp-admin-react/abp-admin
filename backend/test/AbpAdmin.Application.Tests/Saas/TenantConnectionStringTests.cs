using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using AbpAdmin.Tenants;
using Shouldly;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// 租户连接字符串管理。
/// 断言要点：库里落密文（不是明文）、管理 API 返回掩码、留空保持原值、
/// 「使用共享数据库」删除记录、校验端点不写库、设置项禁用后更新被拒（业务异常）。
/// </summary>
public abstract class TenantConnectionStringTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string PlainConnectionString = "Data Source=:memory:;Cache=Shared";

    private readonly AbpAdmin.Tenants.TenantAppService _tenantAppService;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;
    private readonly TenantConnectionStringProtector _protector;
    private readonly IConnectionStringResolver _connectionStringResolver;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingManager _settingManager;
    private readonly IAuditingManager _auditingManager;

    protected TenantConnectionStringTests()
    {
        // ExposeServices 未 IncludeSelf：经基类解析，运行时实例就是我们的替换实现
        _tenantAppService = (AbpAdmin.Tenants.TenantAppService)GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _protector = GetRequiredService<TenantConnectionStringProtector>();
        _connectionStringResolver = GetRequiredService<IConnectionStringResolver>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _settingManager = GetRequiredService<ISettingManager>();
        _auditingManager = GetRequiredService<IAuditingManager>();
    }

    private async Task<Guid> CreateTenantAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync("t" + Guid.NewGuid().ToString("N")[..12]);
            await _tenantRepository.InsertAsync(tenant);
            return tenant.Id;
        });
    }

    [Fact]
    public void Should_Resolve_Our_TenantAppService()
    {
        // 验证 ABP 服务替换机制生效（基类 TenantAppService → 我们的实现）。
        // 注意 ITenantAppService 解析到的是 Castle 拦截代理，不能做类型断言；
        // 类类型解析返回的是继承本类的类代理，is 判断可以穿透。
        GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>()
            .ShouldBeAssignableTo<AbpAdmin.Tenants.TenantAppService>();
    }

    [Fact]
    public async Task Should_Store_Encrypted_And_Return_Mask()
    {
        var tenantId = await CreateTenantAsync();

        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items =
            {
                new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString }
            }
        });

        // 直接查库看到的是密文，且能解密回原文
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        var stored = tenant.FindDefaultConnectionString();
        stored.ShouldNotBeNull();
        stored.ShouldNotBe(PlainConnectionString);
        _protector.DecryptOrPlain(stored).ShouldBe(PlainConnectionString);

        // 管理 API 返回掩码，永不返回明文
        var management = await _tenantAppService.GetConnectionStringsAsync(tenantId);
        management.IsManagementEnabled.ShouldBeTrue();
        management.UseSharedDatabase.ShouldBeFalse();
        management.Items.Count.ShouldBe(1);
        management.Items[0].Name.ShouldBe("Default");
        management.Items[0].Value.ShouldBe(TenantConnectionStringProtector.Mask);

        var defaultValue = await _tenantAppService.GetDefaultConnectionStringAsync(tenantId);
        defaultValue.ShouldBe(TenantConnectionStringProtector.Mask);
    }

    [Fact]
    public async Task Should_Keep_Value_When_Mask_Submitted()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
        });

        // 回传掩码：保持原值
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = TenantConnectionStringProtector.Mask } }
        });

        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        var stored = tenant.FindDefaultConnectionString();
        stored.ShouldNotBeNull();
        stored.ShouldNotBe(TenantConnectionStringProtector.Mask);
        _protector.DecryptOrPlain(stored).ShouldBe(PlainConnectionString);
    }

    [Fact]
    public async Task Should_Decrypt_When_Resolving_For_Connection_Use()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
        });

        // 连接使用路径（MultiTenantConnectionStringResolver → TenantStore）拿到的是解密后的明文
        using (_currentTenant.Change(tenantId))
        {
            var resolved = await _connectionStringResolver.ResolveAsync();
            resolved.ShouldBe(PlainConnectionString);
        }
    }

    [Fact]
    public async Task Should_Remove_All_Records_When_UseSharedDatabase()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items =
            {
                new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString },
                new TenantConnectionStringItemInput { Name = "EasyAbpFileManagement", Value = PlainConnectionString }
            }
        });

        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            UseSharedDatabase = true
        });

        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ConnectionStrings.Count.ShouldBe(0);

        var management = await _tenantAppService.GetConnectionStringsAsync(tenantId);
        management.UseSharedDatabase.ShouldBeTrue();
        management.Items.Count.ShouldBe(0);

        // 回退到 host 的连接串
        using (_currentTenant.Change(tenantId))
        {
            var resolved = await _connectionStringResolver.ResolveAsync();
            resolved.ShouldNotBe(PlainConnectionString);
        }
    }

    [Fact]
    public async Task Should_Reject_Unknown_Database_Name()
    {
        var tenantId = await CreateTenantAsync();

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "NotARealDatabase", Value = PlainConnectionString } }
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringName);
    }

    [Fact]
    public async Task Should_List_Only_Databases_Used_By_Tenants()
    {
        var databases = await _tenantAppService.GetAvailableDatabasesAsync();

        // 项目只注册了 EasyAbpFileManagement 为 IsUsedByTenants；Default 由专门字段承载，不在列表里
        databases.ShouldContain("EasyAbpFileManagement");
        databases.ShouldNotContain("Default");
    }

    [RequiresHostDatabaseFact]
    public async Task Should_Check_Connection_String_Without_Persisting()
    {
        var tenantId = await CreateTenantAsync();

        // A-block 重构后建库/校验是 PostgreSQL-only（CreateConnection 用 Npgsql）。
        // 合法串 = 宿主连接串真实覆盖层（环境变量 → secrets；tracked 基座占位串不算配置）里的可连接 PG 串
        var ok = await _tenantAppService.CheckConnectionStringAsync(tenantId, new CheckTenantConnectionStringInput
        {
            ConnectionString = ResolveHostPgConnectionString()
        });
        ok.IsValid.ShouldBeTrue();
        ok.ErrorMessage.ShouldBeNull();

        // 不可达地址：打开失败（1s 超时快速失败）并返回本地化原因（驱动原文只进日志）
        var bad = await _tenantAppService.CheckConnectionStringAsync(tenantId, new CheckTenantConnectionStringInput
        {
            ConnectionString = "Host=10.255.255.1;Port=5432;Database=none;Timeout=1"
        });
        bad.IsValid.ShouldBeFalse();
        bad.ErrorMessage.ShouldNotBeNullOrWhiteSpace();

        // 纯校验，不写库：两次调用后租户仍无连接串记录
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ConnectionStrings.Count.ShouldBe(0);
    }

    /// <summary>
    /// 宿主连接串的真实覆盖：环境变量 ConnectionStrings__Default 优先，其后 HttpApi.Host 的
    /// appsettings.secrets.json——统一走 <see cref="AbpAdmin.RequiresHostDatabaseFactAttribute.TryResolveOverride"/>
    /// （tracked 基座 appsettings.json 的 PG 占位串设计为运行期 fail-fast，不算配置）。
    /// 连接串校验需要一台可连的 PG；本用例已由 <see cref="AbpAdmin.RequiresHostDatabaseFactAttribute"/>
    /// 门控，无覆盖时到不了这里，本抛出仅为直调兜底。
    /// </summary>
    private static string ResolveHostPgConnectionString()
    {
        var conn = AbpAdmin.RequiresHostDatabaseFactAttribute.TryResolveOverride();
        if (conn == null)
        {
            throw new System.InvalidOperationException(
                "连接串校验用例需要 PostgreSQL 连接串（宿主已 PG-only）。" +
                "请在 framework/AbpAdmin.HttpApi.Host/appsettings.secrets.json 配置 ConnectionStrings:Default。");
        }

        return conn;
    }

    [Fact]
    public async Task Should_Reject_Update_When_Management_Disabled()
    {
        var tenantId = await CreateTenantAsync();

        await _settingManager.SetGlobalAsync(
            AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement, "false");

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.TenantConnectionStringManagementDisabled);
    }

    [Fact]
    public async Task UpdateConnectionStrings_Plaintext_Should_Not_Enter_Audit_Action_Parameters()
    {
        // 回归锁：Items[].Value 是明文连接串，实体列走 protector 加密，但方法参数副本
        // 会被审计拦截器序列化进 AbpAuditLogActions.Parameters（90 天保留 = 凭据明文随审计
        // 裸奔）。修复与 SetWithdrawVaultAsync 同款：方法级 [DisableAuditing] 整体退出审计
        // 参数通道；操作留痕走 OperationLog 不受影响。断言审计作用域内任何动作参数都不含明文
        // （方法级关闭下该动作根本不落，未来若改成参数级打码此断言仍成立）。
        const string secret = "Host=db;Password=Audit!Leak#42";
        var tenantId = await CreateTenantAsync();

        await _settingManager.SetGlobalAsync(
            AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement, "true");

        using (_auditingManager.BeginScope())
        {
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = secret } }
            });

            var scope = _auditingManager.Current;
            scope.ShouldNotBeNull();
            scope.Log.Actions.Any(a => a.Parameters != null && a.Parameters.Contains(secret))
                .ShouldBeFalse("[DisableAuditing] 应挡住明文连接串进审计动作参数");
        }

        // 加密存储语义不受影响（既有 Should_Store_Encrypted_And_Return_Mask 的最小复核）
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        _protector.DecryptOrPlain(tenant.FindDefaultConnectionString()!).ShouldBe(secret);
    }

    [Fact]
    public async Task CheckConnectionString_Plaintext_Should_Not_Enter_Audit_Action_Parameters()
    {
        // 回归锁（校验端点）：探测目标串同样是明文入参，失败探测（不可达地址）也要留痕，
        // 但探测串本身不得进审计动作参数。
        const string secret = "Host=10.255.255.1;Port=5432;Password=Audit!Probe#42;Timeout=1";
        var tenantId = await CreateTenantAsync();

        await _settingManager.SetGlobalAsync(
            AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement, "true");

        using (_auditingManager.BeginScope())
        {
            await Should.NotThrowAsync(() => _tenantAppService.CheckConnectionStringAsync(
                tenantId, new CheckTenantConnectionStringInput { ConnectionString = secret }));

            var scope = _auditingManager.Current;
            scope.ShouldNotBeNull();
            scope.Log.Actions.Any(a => a.Parameters != null && a.Parameters.Contains(secret))
                .ShouldBeFalse();
        }
    }
}
