using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.Localization;
using AbpAdmin.Menus;
using AbpAdmin.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Local;
using Volo.Abp.MultiTenancy;
using Volo.Abp.ObjectExtending;
using Volo.Abp.Settings;
using Volo.Abp.TenantManagement;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Tenants;

/// <summary>
/// 替换开源 TenantManagement 的 <see cref="Volo.Abp.TenantManagement.TenantAppService"/>（T2.8 SaaS 缺口）。
/// 走 ABP 的服务替换机制，不改 ABP 源码：
/// - 开源 HttpApi 层的显式 TenantController（/api/multi-tenancy/tenants）注入 ITenantAppService，
///   会解析到本类，因此原有路由的行为一并变为加密存储/掩码返回；
/// - 本类同时经约定控制器暴露在 /api/app/tenant，承载新增的连接串管理端点。
///
/// 「创建租户时忽略传入的连接串」说明：开源 TenantCreateDto 契约不含任何连接串字段
/// （连接串不是扩展属性，MapExtraPropertiesTo 不会映射它），创建路径没有连接串通道，
/// 该行为天然成立，无需在 CreateAsync 里做剥离。
/// </summary>
[Authorize(TenantManagementPermissions.Tenants.Default)]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ITenantAppService), typeof(Volo.Abp.TenantManagement.TenantAppService), IncludeSelf = true)]
public class TenantAppService : Volo.Abp.TenantManagement.TenantAppService
{
    private readonly TenantConnectionStringProtector _connectionStringProtector;
    private readonly AbpDbConnectionOptions _dbConnectionOptions;
    private readonly IConfiguration _configuration;
    private readonly TenantPackageManager _tenantPackageManager;
    private readonly MenuManager _menuManager;
    // 本类继承 ABP TenantManagementAppServiceBase，L 走 AbpTenantManagementResource；
    // 自研文案键注册在 AbpAdminResource，须经专用 localizer 取（否则客户端拿到裸 key）
    private readonly IStringLocalizer<AbpAdminResource> _abpAdminLocalizer;

    public TenantAppService(
        ITenantRepository tenantRepository,
        ITenantManager tenantManager,
        IDataSeeder dataSeeder,
        IDistributedEventBus distributedEventBus,
        ILocalEventBus localEventBus,
        TenantConnectionStringProtector connectionStringProtector,
        IOptions<AbpDbConnectionOptions> dbConnectionOptions,
        IConfiguration configuration,
        TenantPackageManager tenantPackageManager,
        MenuManager menuManager,
        IStringLocalizer<AbpAdminResource> abpAdminLocalizer)
        : base(tenantRepository, tenantManager, dataSeeder, distributedEventBus, localEventBus)
    {
        _connectionStringProtector = connectionStringProtector;
        _dbConnectionOptions = dbConnectionOptions.Value;
        _configuration = configuration;
        _tenantPackageManager = tenantPackageManager;
        _menuManager = menuManager;
        _abpAdminLocalizer = abpAdminLocalizer;
    }

    /// <summary>
    /// 与基类逻辑逐步一致，唯一差别：租户数据种子按贡献者分离 UoW、随做随提交
    /// （T3.4 在 AbpAdminDbMigrationService 的同款修法，运行时路径漏修导致建租户 500）。
    /// 基类在同一请求 UoW 里跑全部 IDataSeedContributor：框架 PermissionDataSeedContributor
    /// 把当前定义的全部权限授给 admin 角色（插入但本 UoW 内未提交），而 T1 自研的
    /// SettingUi/FileManagement/DataScope/NotificationService 权限种子走 IPermissionDataSeeder
    /// 「先查后插」——查的是数据库，看不到同 UoW 里未提交的行，去重失效 → 双方对同一
    /// (TenantId, Name, ProviderName, ProviderKey) 各插一条，请求末尾 AbpUowActionFilter
    /// SaveChanges 撞 AbpPermissionGrants 唯一索引（SQLite Error 19）→ 500。
    /// 分离 UoW 后每个贡献者立即提交，后面的贡献者查得到前面已提交的行，幂等去重生效。
    /// RequiresNew 必须为 true：否则子 UoW 并入环境 UoW，仍然攒到最后一次提交。
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.Create)]
    public override async Task<TenantDto> CreateAsync(TenantCreateDto input)
    {
        var tenant = await TenantManager.CreateAsync(input.Name);
        input.MapExtraPropertiesTo(tenant);
        await TenantRepository.InsertAsync(tenant);

        // 与基类一致：种子前先落库（触发 EntityCreatedEto&lt;TenantEto&gt; 字典种子处理器等）
        if (CurrentUnitOfWork != null)
        {
            await CurrentUnitOfWork.SaveChangesAsync();
        }

        await DistributedEventBus.PublishAsync(new TenantCreatedEto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Properties =
            {
                { "AdminEmail", input.AdminEmailAddress },
                { "AdminPassword", input.AdminPassword }
            }
        });

        using (CurrentTenant.Change(tenant.Id, tenant.Name))
        {
            await DataSeeder.SeedAsync(
                new DataSeedContext(tenant.Id)
                    .WithProperty("AdminEmail", input.AdminEmailAddress)
                    .WithProperty("AdminPassword", input.AdminPassword)
                    .WithProperty(DataSeederExtensions.SeedInSeparateUow, true)
                    .WithProperty(DataSeederExtensions.SeedInSeparateUowRequiresNew, true)
            );
        }

        return ObjectMapper.Map<Tenant, TenantDto>(tenant);
    }

    /// <summary>
    /// 应用套餐到租户：把租户菜单树重置为"套餐过滤后的全局模板拷贝"。
    /// 破坏性操作——会清掉该租户现有菜单与菜单角色勾选，调用方前端需二次确认。
    /// 套餐以 PackageId（强标识）持久化在租户 extra property 上（见 AbpAdminTenantConsts.PackageIdPropertyName），
    /// 懒拷贝据此过滤；套餐解析/守卫规则收敛在 <see cref="TenantPackageManager"/>。
    /// POST /api/app/tenant/{id}/apply-package
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.Update)]
    [OperationLog("租户管理", "应用套餐", BizNo = "{{id}}", Success = "为租户 {{tenant(id)}} 应用了套餐「{{tenantPackage(input.packageId)}}」")]
    public virtual async Task ApplyPackageAsync(Guid id, ApplyTenantPackageDto input)
    {
        var tenant = await TenantRepository.GetAsync(id);

        var allowedTemplateMenuIds = await _tenantPackageManager.ApplyAsync(tenant, input.PackageId);

        await TenantRepository.UpdateAsync(tenant);

        using (CurrentTenant.Change(tenant.Id, tenant.Name))
        {
            // 菜单仓储在该租户上下文里只看到租户自己的菜单
            await _menuManager.ResetTenantMenusAsync(tenant.Id, allowedTemplateMenuIds);
        }
    }

    /// <summary>
    /// 管理侧 API 永不返回明文：有值时返回固定掩码。
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    public override async Task<string?> GetDefaultConnectionStringAsync(Guid id)
    {
        var value = await base.GetDefaultConnectionStringAsync(id);
        return string.IsNullOrEmpty(value) ? value : TenantConnectionStringProtector.Mask;
    }

    /// <summary>
    /// 更新默认连接串：加密存储；掩码/空值表示保持原值；含掩码字面量的值拒绝（业务异常）。
    /// 提交了明文 Default 后发布 <see cref="TenantDatabaseMigrationNeededEto"/>（UoW 提交后投递）
    /// 触发独立租户库的运行期建库+schema 迁移——幂等，重复保存可作迁移失败的重试手段。
    /// 设置项禁用该功能时返回业务异常（不是静默忽略）。
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    public override async Task UpdateDefaultConnectionStringAsync(Guid id, string defaultConnectionString)
    {
        await EnsureConnectionStringManagementEnabledAsync();

        var tenant = await TenantRepository.GetAsync(id);
        if (TenantConnectionStringProtector.IsMaskOrEmpty(defaultConnectionString))
        {
            // 保持原值：原本有值则不动（掩码回传场景）；原本没有值则等价于不设
            return;
        }

        // 防御：管理 API 的值占位符是 "********"（原值回显为纯掩码）。
        // 若提交值只是"包含"掩码（如在掩码后追加输入的误操作），必须拒绝而不是当真值加密存储——
        // 这种值存进去后解密出来是掩码+连接串，任何数据库驱动都解析不了。
        EnsureNoMaskLiteral(defaultConnectionString);

        var encrypted = _connectionStringProtector.Encrypt(defaultConnectionString);
        // 比较明文（解密旧值）而非比较密文：与加密算法是否确定性解耦——
        // 换成带随机 IV 的实现后，同值也会密文不等 → 每次保存都多发一次缓存失效事件
        var stored = tenant.FindDefaultConnectionString();
        var changed = stored == null || _connectionStringProtector.DecryptOrPlain(stored) != defaultConnectionString;
        if (changed)
        {
            await LocalEventBus.PublishAsync(new TenantChangedEvent(tenant.Id, tenant.NormalizedName));
        }

        tenant.SetDefaultConnectionString(encrypted);
        await TenantRepository.UpdateAsync(tenant);

        // T2.8 SaaS Pro 缺口：配独立连接串后运行期建库+迁 schema（此前租户请求直接失败，
        // 只能离线重跑 DbMigrator）。分布式事件缓冲到 UoW 提交后投递，
        // 处理器（TenantDatabaseMigrationNeededHandler）读到的是已提交的新连接串。
        // 语义：提交了明文 Default（非掩码/空）即发布——不与 changed 挂钩，
        // 保证「迁移失败后重新保存同一连接串」能重试（幂等：库/schema 已就绪时无副作用）。
        await DistributedEventBus.PublishAsync(new TenantDatabaseMigrationNeededEto { TenantId = tenant.Id });
    }

    /// <summary>
    /// 连接字符串管理视图：当前记录（掩码）+ 功能开关 + 是否使用共享数据库。
    /// GET /api/app/tenant/{id}/connection-strings
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    public virtual async Task<TenantConnectionStringManagementDto> GetConnectionStringsAsync(Guid id)
    {
        var tenant = await TenantRepository.GetAsync(id);
        return new TenantConnectionStringManagementDto
        {
            IsManagementEnabled = await IsConnectionStringManagementEnabledAsync(),
            UseSharedDatabase = tenant.ConnectionStrings.Count == 0,
            Items = tenant.ConnectionStrings
                .Select(cs => new TenantConnectionStringItemDto
                {
                    Name = cs.Name,
                    Value = TenantConnectionStringProtector.Mask
                })
                .ToList()
        };
    }

    /// <summary>
    /// 可分配连接串的数据库清单：只列 IsUsedByTenants = true 的数据库。
    /// GET /api/app/tenant/available-databases
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    public virtual async Task<List<string>> GetAvailableDatabasesAsync()
    {
        // 与读/写/测连接串的兄弟端点一致：功能开关关闭时不暴露数据库清单
        await EnsureConnectionStringManagementEnabledAsync();

        return GetAvailableDatabaseNames().Where(name => name != "Default").ToList();
    }

    /// <summary>
    /// 全量同步租户连接串。设置项禁用时返回业务异常（不是静默忽略）。
    /// Items 语义：掩码/空 = 保持原值；含掩码字面量 = 拒绝；从提交里消失的既有记录 = 删除。
    /// 提交了明文 Default（非 UseSharedDatabase）后发布 <see cref="TenantDatabaseMigrationNeededEto"/>
    /// （UoW 提交后投递）触发运行期建库+迁移——与 UpdateDefaultConnectionStringAsync 同语义、幂等；
    /// 前端连接串抽屉把 Default 作为 Items 一项走本端点，这里是生产主路径。
    /// PUT /api/app/tenant/{id}/connection-strings
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    [OperationLog("租户管理", "更新连接串", BizNo = "{{id}}", Success = "更新了租户 {{tenant(id)}} 的数据库连接串（值不入日志）")]
    public virtual async Task UpdateConnectionStringsAsync(Guid id, UpdateTenantConnectionStringsInput input)
    {
        await EnsureConnectionStringManagementEnabledAsync();

        var tenant = await TenantRepository.GetAsync(id);
        var changed = false;

        if (input.UseSharedDatabase)
        {
            // 移除该租户的所有连接串记录，回退到 host 的连接串
            foreach (var name in tenant.ConnectionStrings.Select(x => x.Name).ToList())
            {
                tenant.RemoveConnectionString(name);
                changed = true;
            }
        }
        else
        {
            var availableNames = GetAvailableDatabaseNames();
            foreach (var item in input.Items)
            {
                if (!availableNames.Contains(item.Name))
                {
                    throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringName)
                        .WithData("Name", item.Name);
                }

                if (TenantConnectionStringProtector.IsMaskOrEmpty(item.Value))
                {
                    // 留空或回传掩码表示保持原值
                    continue;
                }

                EnsureNoMaskLiteral(item.Value);

                var encrypted = _connectionStringProtector.Encrypt(item.Value!);
                // 同 UpdateDefaultConnectionStringAsync：比较明文，与加密算法确定性解耦
                var storedItem = tenant.FindConnectionString(item.Name);
                if (storedItem == null || _connectionStringProtector.DecryptOrPlain(storedItem) != item.Value)
                {
                    tenant.SetConnectionString(item.Name, encrypted);
                    changed = true;
                }
            }

            // 全量同步：已存在但不在提交列表里的记录删除
            var submittedNames = input.Items.Select(x => x.Name).ToList();
            foreach (var existingName in tenant.ConnectionStrings.Select(x => x.Name).ToList())
            {
                if (!submittedNames.Contains(existingName))
                {
                    tenant.RemoveConnectionString(existingName);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            // 与开源实现一致：连接串变化发布 TenantChangedEvent 使 TenantConfiguration 缓存失效
            await LocalEventBus.PublishAsync(new TenantChangedEvent(tenant.Id, tenant.NormalizedName));
            await TenantRepository.UpdateAsync(tenant);
        }

        // Default 连接串的另一个通道：前端连接串抽屉把 Default 作为 Items 一项经本端点提交
        // （OSS 路由的 UpdateDefaultConnectionStringAsync 是独立端点）。语义与那个端点一致：
        // 提交了非掩码/空的明文 Default 即发布迁移事件——不与 changed 挂钩，
        // 「迁移失败后重新保存同一连接串」可重试（幂等：库/schema 已就绪时无副作用）。
        // 注意不校验 Default 值是否与库存相同：ensure 语义 + 幂等，重复执行的代价可接受。
        if (!input.UseSharedDatabase && input.Items.Any(x =>
                x.Name == "Default" && !TenantConnectionStringProtector.IsMaskOrEmpty(x.Value)))
        {
            await DistributedEventBus.PublishAsync(
                new TenantDatabaseMigrationNeededEto { TenantId = tenant.Id });
        }
    }

    /// <summary>
    /// 校验连接串：尝试建立一次数据库连接并立即关闭。纯校验，不写库。
    /// POST /api/app/tenant/{id}/check-connection-string
    /// </summary>
    [Authorize(TenantManagementPermissions.Tenants.ManageConnectionStrings)]
    public virtual async Task<CheckTenantConnectionStringResultDto> CheckConnectionStringAsync(
        Guid id,
        CheckTenantConnectionStringInput input)
    {
        await EnsureConnectionStringManagementEnabledAsync();

        // 与兄弟端点语义对齐：路径上的租户必须存在（EntityNotFoundException → 404）。
        // 不校验时本端点事实上是"以任意租户为前缀的服务端任意连接串探测"
        await TenantRepository.GetAsync(id);

        try
        {
            await using var connection = CreateConnection(input.ConnectionString);
            await connection.OpenAsync();
            await connection.CloseAsync();
            return new CheckTenantConnectionStringResultDto { IsValid = true };
        }
        catch (Exception ex)
        {
            // 底层驱动异常原文（Npgsql/SQLite 报错常含主机名、端口、账号甚至连接串片段）
            // 不得回传客户端；原文记入日志供排障（入参连接串本身不作为日志参数），
            // 客户端只拿本地化后的通用失败原因
            Logger.LogWarning(ex, "Tenant connection string check failed. TenantId:{TenantId}", id);
            return new CheckTenantConnectionStringResultDto
            {
                IsValid = false,
                ErrorMessage = _abpAdminLocalizer["TenantConnectionStringCheckFailed"]
            };
        }
    }

    /// <summary>
    /// 按应用当前配置的数据库提供程序建连接（租户库与 host 同 DBMS）。
    /// 提供程序判定与 AbpAdmin.EntityFrameworkCore 的 AbpAdminDatabaseProvider 同一配置键
    /// （Database:Provider，默认 Sqlite）；Application 层不引用 EFCore 项目，这里直接读配置。
    /// </summary>
    protected virtual DbConnection CreateConnection(string connectionString)
    {
        var provider = _configuration["Database:Provider"];
        if (!string.IsNullOrWhiteSpace(provider) &&
            provider.Trim().Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return new NpgsqlConnection(connectionString);
        }

        return new SqliteConnection(connectionString);
    }

    /// <summary>
    /// Default + AbpDbConnectionOptions.Databases 中 IsUsedByTenants = true 的数据库。
    /// </summary>
    protected virtual List<string> GetAvailableDatabaseNames()
    {
        var names = new List<string> { "Default" };
        names.AddRange(_dbConnectionOptions.Databases.Values
            .Where(db => db.IsUsedByTenants)
            .Select(db => db.DatabaseName));
        return names;
    }

    protected virtual async Task<bool> IsConnectionStringManagementEnabledAsync()
    {
        return await SettingProvider.IsTrueAsync(AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement);
    }

    protected virtual async Task EnsureConnectionStringManagementEnabledAsync()
    {
        if (!await IsConnectionStringManagementEnabledAsync())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.TenantConnectionStringManagementDisabled);
        }
    }

    /// <summary>
    /// 提交值里出现掩码字面量（非纯掩码回传）一律拒绝：掩码只是管理 API 的"原值"占位符，
    /// 当真值入库会导致解密结果无法被任何数据库驱动解析（见 InvalidTenantConnectionStringValue）。
    /// </summary>
    protected static void EnsureNoMaskLiteral(string? value)
    {
        if (!TenantConnectionStringProtector.IsMaskOrEmpty(value) &&
            value!.Contains(TenantConnectionStringProtector.Mask, StringComparison.Ordinal))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringValue);
        }
    }
}
