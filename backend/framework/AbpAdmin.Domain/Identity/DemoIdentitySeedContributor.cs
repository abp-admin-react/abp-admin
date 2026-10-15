using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Roles;
using Volo.Abp.TenantManagement;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 演示身份种子（净库开箱可测）：demo 租户 + 财务角色（fin-accountant/fin-boss）+ 演示账号（密码
/// 1q2w3E*）。幂等：按租户名/角色名/用户名判重。门控：仅 Development 或 App:SeedDemoData=true
/// （与 LedgerDataSeedContributor 的 Finance:SeedDemoData 同款口径，IAbpHostEnvironment 是 ABP
/// 自带抽象，Domain 层零额外依赖）。必须在 Domain 层：DbMigrator 只挂 ApplicationContracts，
/// 不加载 Application——放 Application 的贡献者迁移时根本不会执行（这正是本文件曾在
/// AbpAdmin.Application/Seed 而租户种不出来的原因）。
/// 运维口径（幂等的边界）：本贡献者只保证 DB 侧幂等。宿主运行期有权限判定缓存
/// （PermissionChecker 结果进分布式缓存，键形如 c:Volo.Abp.PermissionManagement.PermissionGrant,...）——
/// 对带流量的宿主重跑本种子修正授权后，需清权限缓存或重启宿主，否则已缓存的 false 判定
/// 不随库失效（docker 全量实测踩过：修行后仍 403，FLUSHALL 恢复）。
/// </summary>
public class DemoIdentitySeedContributor : IDataSeedContributor, ITransientDependency
{
    public ILogger<DemoIdentitySeedContributor> Logger { get; set; } =
        NullLogger<DemoIdentitySeedContributor>.Instance;

    private static readonly string DemoPassword = "1q2w3E*";

    private readonly ITenantRepository _tenants;
    private readonly ITenantManager _tenantManager;
    private readonly ITenantNormalizer _tenantNormalizer;
    private readonly IIdentityRoleRepository _roles;
    private readonly IIdentityUserRepository _users;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionDataSeeder _permissionSeeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly IConfiguration _configuration;
    private readonly IAbpHostEnvironment _environment;

    public DemoIdentitySeedContributor(
        ITenantRepository tenants,
        ITenantManager tenantManager,
        ITenantNormalizer tenantNormalizer,
        IIdentityRoleRepository roles,
        IIdentityUserRepository users,
        IdentityUserManager userManager,
        IdentityRoleManager roleManager,
        IPermissionDataSeeder permissionSeeder,
        ICurrentTenant currentTenant,
        IGuidGenerator guids,
        IConfiguration configuration,
        IAbpHostEnvironment environment)
    {
        _tenants = tenants;
        _tenantManager = tenantManager;
        _tenantNormalizer = tenantNormalizer;
        _roles = roles;
        _users = users;
        _userManager = userManager;
        _roleManager = roleManager;
        _permissionSeeder = permissionSeeder;
        _currentTenant = currentTenant;
        _guids = guids;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // 仅在 host 上下文跑一次：AbpAdminDbMigrationService 先种 host、再逐租户重跑种子循环，
        // 本贡献者自己用 Change(tenantId) 管理租户侧写入，若不拦会在租户循环里把 host 角色重复
        // 种成租户作用域角色。
        if (context.TenantId is not null)
        {
            return;
        }

        var enabled = _environment.IsDevelopment()
            || string.Equals(_configuration["App:SeedDemoData"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return;
        }

        if (!_environment.IsDevelopment())
        {
            // 与 AbpAdminDbMigrationService 对默认 admin 口令的告警同姿态：生产/预发显式打开
            // 演示种子，会落地口令为源码公开常量的可登录账号集，必须留痕。
            Logger.LogWarning(
                "App:SeedDemoData=true 在非 Development 环境生效：即将创建演示账号（口令为源码公开常量 1q2w3E*：宿主 fin-viewer 与 demo 租户 admin/fin-ops/fin-boss）——仅限演示/预发，生产误配请关闭并停用这些账号");
        }

        // Host 侧财务角色 + 只读查看账号
        await SeedHostFinanceRolesAsync();
        await EnsureUserAsync(null, "fin-viewer", "fin-viewer@demo.local", "财务查看（只读）",
            new[] { "Finance.Ledger.View", "Finance.Voucher.View", "Finance.BizDoc.View", "Finance.Balance.View" });

        // demo 租户 + 租户内 admin + 财务角色/账号
        // FindByNameAsync 按 NormalizedName 匹配（默认大写），传原始名会漏判已存在 → CreateAsync 里
        // 校验器按归一化名查重直接抛「租户名已存在」。必须先 NormalizeName 再查。
        var tenant = await _tenants.FindByNameAsync(_tenantNormalizer.NormalizeName("demo"));
        if (tenant is null)
        {
            // ABP 10.x 的 TenantManager.CreateAsync 只构造实体并做校验（重名/归一化），
            // 不落库——持久化是调用方的活（同 TenantAppService）。漏掉 InsertAsync 会种出
            // 「用户带租户 ID 而 AbpTenants 为空」的孤儿数据，且建租户事件（字典种子）不触发。
            tenant = await _tenantManager.CreateAsync("demo");
            await _tenants.InsertAsync(tenant, autoSave: true);
        }

        using (var _ = _currentTenant.Change(tenant.Id))
        {
            // 补租户 admin 角色：框架 IdentityDataSeeder 见 admin 用户已存在会早退、不建角色，
            // 而框架 PermissionDataSeedContributor 已把全部权限授给 ("R","admin")——补上角色并挂
            // 用户，租户 admin 即全权（IsStatic 与框架种子一致）。角色名取 AbpRoleConsts 常量、
            // 归一化查重按其大写归一，不硬编码字面量。
            var adminRoleName = AbpRoleConsts.AdminRoleName;
            var adminRole = await _roles.FindByNormalizedNameAsync(adminRoleName.ToUpperInvariant());
            if (adminRole is null)
            {
                adminRole = new IdentityRole(_guids.Create(), adminRoleName, tenant.Id)
                {
                    IsStatic = true,
                    IsPublic = true,
                };
                (await _roleManager.CreateAsync(adminRole)).CheckErrors();
            }

            var admin = await SeedTenantAdminAsync(tenant.Id);
            if (!await _userManager.IsInRoleAsync(admin, adminRoleName))
            {
                (await _userManager.AddToRoleAsync(admin, adminRoleName)).CheckErrors();
            }

            // 返回值无消费方，不绑定（与上方 fin-viewer 裸调用风格一致）
            await EnsureUserAsync(
                tenant.Id, "fin-ops", "fin-ops@demo.local", "演示：财务操作员",
                new[] { "Finance.Ledger.View", "Finance.Voucher.View", "Finance.Adjust.Create", "Finance.Adjust.Submit" });
            await EnsureUserAsync(
                tenant.Id, "fin-boss", "fin-boss@demo.local", "演示：财务审批人",
                new[] { "Finance.Ledger.View", "Finance.Voucher.View", "Finance.Adjust.Approve", "Finance.Adjust.Reject" });
        }
    }

    private async Task SeedHostFinanceRolesAsync()
    {
        await EnsureRoleWithPermissionsAsync("fin-accountant", new[]
        {
            "Finance.Ledger.View", "Finance.Voucher.View", "Finance.Voucher.Reverse",
            "Finance.Adjust.Create", "Finance.Adjust.Submit", "Finance.Adjust.Approve",
            "Finance.Adjust.Reject", "Finance.Adjust.Reverse", "Finance.Account.Manage",
            "Finance.Reconcile.Run", "Finance.Period.View",
        });
        await EnsureRoleWithPermissionsAsync("fin-boss", new[]
        {
            "Finance.Ledger.View", "Finance.Voucher.View", "Finance.Adjust.Approve", "Finance.Adjust.Reject",
        });
    }

    private async Task EnsureRoleWithPermissionsAsync(string roleName, string[] permissions)
    {
        var existingRole = await _roles.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        if (existingRole is not { } role)
        {
            role = new IdentityRole(_guids.Create(), roleName, _currentTenant.Id);
            (await _roleManager.CreateAsync(role)).CheckErrors();
        }

        // "R" 的 ProviderKey 是角色名（RolePermissionValueProvider 按用户角色声明匹配），
        // 传 roleId.ToString() 是永远命不中的死数据——框架自己种的也是 ("R","admin")。
        // tenantId 必须显式传：PermissionDataSeeder 授权行的租户取自参数而非 ICurrentTenant
        // （框架种子同理显式传），漏传时 demo 租户的授权落 host 作用域，租户内检查永远命不中。
        await _permissionSeeder.SeedAsync(
            "R", roleName, permissions, _currentTenant.Id);
    }

    private async Task<IdentityUser> SeedTenantAdminAsync(Guid tenantId)
    {
        var admin = await EnsureUserAsync(tenantId, "admin", "admin@demo.local", "demo 租户管理员", null);
        // "U"（用户级直接授予）：租户 admin 角色已由调用方补建并把本用户挂入（框架
        // PermissionDataSeedContributor 预先授给 ("R","admin") 的全量权限随之生效）；此处再按
        // 用户钉一层财务权限 + Scope.All，演示账号能力不随角色调整漂移。
        // 注意 ProviderKey 语义："U" 接用户 Id、"R" 接角色名——把用户 Id 塞进 "R" 是
        // 永远命不中的死数据（权限检查器按 R×角色名 匹配）。tenantId 必须显式传（见
        // EnsureRoleWithPermissionsAsync 处注释）：漏传 = 授权落 host 作用域，租户内 403。
        await _permissionSeeder.SeedAsync(
            "U", admin.Id.ToString(),
            new[] { "Finance.Ledger.View", "Finance.Voucher.View", "Finance.Adjust.Create", "Finance.Adjust.Submit", "Finance.Scope.All" },
            tenantId);
        return admin;
    }

    private async Task<IdentityUser> EnsureUserAsync(
        Guid? tenantId, string userName, string email, string display, string[]? permissions)
    {
        using (var _ = _currentTenant.Change(tenantId))
        {
            var existing = await _users.FindByNormalizedUserNameAsync(userName.ToUpperInvariant());
            if (existing is not null)
            {
                return existing;
            }

            var user = new IdentityUser(_guids.Create(), userName, email, tenantId);
            (await _userManager.CreateAsync(user, DemoPassword)).CheckErrors();
            await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("display", display));
            if (permissions is { Length: > 0 })
            {
                // tenantId 显式传（与 _currentTenant.Change 的上下文一致）——见
                // EnsureRoleWithPermissionsAsync 处注释：授权行租户取自参数而非 ICurrentTenant。
                await _permissionSeeder.SeedAsync(
                    "U", user.Id.ToString(), permissions, tenantId);
            }
            return user;
        }
    }
}
