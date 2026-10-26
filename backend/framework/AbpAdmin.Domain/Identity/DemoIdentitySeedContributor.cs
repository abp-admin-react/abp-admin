using System;
using System.Linq;
using System.Security.Claims;
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
using AbpAdmin.Posts;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

/// <summary>
/// 演示身份种子（净库开箱可测）：demo 租户 + 财务角色（fin-accountant/fin-boss）+ 演示账号（密码
/// 1q2w3E*）。幂等：按租户名/角色名/用户名判重。门控三态：App:SeedDemoData 显式 true 强制开 /
/// 显式 false 强制关 / 缺省随 IsDevelopment（与 Ledger 种子的 Finance:SeedDemoData 同款口径；
/// IAbpHostEnvironment 是 ABP 自带抽象，Domain 层零额外依赖）。必须在 Domain 层：DbMigrator 只挂
/// ApplicationContracts，不加载 Application——放 Application 的贡献者迁移时根本不会执行（这正是
/// 本文件曾在 AbpAdmin.Application/Seed 而租户种不出来的原因）。
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
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly OrganizationUnitManager _organizationUnitManager;
    private readonly IPostRepository _postRepository;
    private readonly IRepository<IdentityUserPost> _userPostRepository;
    private readonly Volo.Abp.Uow.IUnitOfWorkManager _unitOfWorkManager;

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
        IAbpHostEnvironment environment,
        IOrganizationUnitRepository organizationUnitRepository,
        OrganizationUnitManager organizationUnitManager,
        IPostRepository postRepository,
        IRepository<IdentityUserPost> userPostRepository,
        Volo.Abp.Uow.IUnitOfWorkManager unitOfWorkManager)
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
        _organizationUnitRepository = organizationUnitRepository;
        _organizationUnitManager = organizationUnitManager;
        _postRepository = postRepository;
        _userPostRepository = userPostRepository;
        _unitOfWorkManager = unitOfWorkManager;
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

        // 门控三态（与 LedgerDataSeedContributor 的 Finance:SeedDemoData 同款，优先级从高到低）：
        // App:SeedDemoData 显式 true 强制开（生产演示库）、显式 false 强制关（测试库/特殊部署
        // 形态可在不依赖环境判定的前提下关死）、缺省随 IsDevelopment()（本地净库 dotnet run
        // 自动带演示账号）。
        var configured = _configuration["App:SeedDemoData"];
        if (string.Equals(configured, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var enabled = _environment.IsDevelopment()
            || string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase);
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

        // 组织×岗位×角色×用户组合演示（部门树 + 职务 + 权限来源对照，见方法头注释）
        await SeedOrgComboDemoAsync();

        // demo 租户 + 租户内 admin + 财务角色/账号
        // FindByNameAsync 按 NormalizedName 匹配（默认大写），传原始名会漏判已存在 → CreateAsync 里
        // 校验器按归一化名查重直接抛「租户名已存在」。必须先 NormalizeName 再查。
        var tenant = await _tenants.FindByNameAsync(_tenantNormalizer.NormalizeName("demo"));
        if (tenant is null)
        {
            // ABP 的 TenantManager.CreateAsync 一直是「只构造实体并做校验（重名/归一化）」，
            // 不落库——持久化是调用方的活（官方种子示例 abp#8800 即 CreateAsync + InsertAsync
            // 两步；TenantAppService 同构）。漏掉 InsertAsync 会种出「用户带租户 ID 而
            // AbpTenants 为空」的孤儿数据，且建租户事件（字典种子）不触发。
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

    /// <summary>
    /// 组织×岗位×角色×用户组合演示（四个页面各自看着抽象，组合才是用法——净库给一套有故事线的样例）：
    /// 部门树（组织单元）管「人在哪」，岗位管「什么职务」（不携带权限），角色管「能干什么」（权限集合），
    /// 用户把三者组合起来；权限来源两条路——直接给角色，或把角色绑到部门（进部门即得）。
    /// <para>演示人物（口令同 DemoPassword，display 即页面展示名）：</para>
    /// <list type="bullet">
    /// <item>张智 zhang.cto：技术中心+研发部（一人多部门），岗位 CTO，直接角色「部门经理」</item>
    /// <item>李工 li.dev：研发部，岗位 开发工程师+运维工程师（一人兼岗），<b>无直接角色——
    /// 权限来自「研发部」绑定的「普通员工」角色</b>（组织绑角色演示）</item>
    /// <item>王财 wang.fm：财务中心，岗位 财务经理，直接角色 fin-accountant（复用财务域角色库）</item>
    /// <item>赵营销 zhao.mkt：市场部，岗位 市场专员，直接角色「普通员工」（与李工同权限、来源不同的对照组）</item>
    /// </list>
    /// 全部幂等：OU 按 DisplayName、岗位按 Code、角色/用户按归一化名、OU 角色与用户岗位绑定按存在性判重。
    /// </summary>
    internal async Task SeedOrgComboDemoAsync()
    {
        // 自带环境 UoW：migrator 侧外层已有大 UoW 时嵌套加入；测试/裸调时兜底供 DbContext。
        // OU 创建逐个 requiresNew 单独提交——OU 父子校验按 parentId 从库里 FindAsync，
        // 同批未提交的插入对它不可见（实测 010010），逐个落库让父节点在子节点创建前可查。
        using (var ambient = _unitOfWorkManager.Begin())
        {
            var ouList = await _organizationUnitRepository.GetListAsync();

            // 自洽：王财复用 fin-accountant，先确保财务角色库在位（幂等；migrator 主流程
            // 已先种过一遍，这里双调无害——也让本方法可独立调用/测试）
            await SeedHostFinanceRolesAsync();

            async Task<OrganizationUnit> EnsureOu(string displayName, string? parentDisplayName)
            {
                var existing = ouList.FirstOrDefault(x => x.DisplayName == displayName);
                if (existing is { } ou)
                {
                    return ou;
                }

                var parent = parentDisplayName == null
                    ? null
                    : ouList.FirstOrDefault(x => x.DisplayName == parentDisplayName)
                      ?? throw new InvalidOperationException($"演示 OU 父节点缺失：{parentDisplayName}");
                // Code（层级编码，非空列）由 OrganizationUnitManager 生成——裸 InsertAsync 会 23502
                var created = new OrganizationUnit(_guids.Create(), displayName, parent?.Id, _currentTenant.Id);
                using (var ouUow = _unitOfWorkManager.Begin(requiresNew: true))
                {
                    await _organizationUnitManager.CreateAsync(created);
                    await ouUow.CompleteAsync();
                }
                ouList.Add(created);
                return created;
            }

            var group = await EnsureOu("演示集团", null);
            var board = await EnsureOu("董事会", "演示集团");
            var techCenter = await EnsureOu("技术中心", "演示集团");
            var rd = await EnsureOu("研发部", "技术中心");
            await EnsureOu("运维部", "技术中心");
            var financeCenter = await EnsureOu("财务中心", "演示集团");
            var marketing = await EnsureOu("市场部", "演示集团");
            _ = board;

            // —— 岗位（职务维度，不带权限）：QA 故意停用，演示状态列 ——
            async Task<Post> EnsurePostAsync(string code, string name, int sort, string remark, bool enabled = true)
            {
                var post = await _postRepository.FindByCodeAsync(code);
                if (post is { })
                {
                    return post;
                }

                post = new Post(_guids.Create(), name, code, sort,
                    enabled ? PostStatusEnum.Enabled : PostStatusEnum.Disabled, remark, _currentTenant.Id);
                return await _postRepository.InsertAsync(post, autoSave: true);
            }

            var postCto = await EnsurePostAsync("CTO", "技术负责人", 1, "演示：技术中心负责人");
            var postDev = await EnsurePostAsync("DEV", "开发工程师", 2, "演示：研发部");
            var postSre = await EnsurePostAsync("SRE", "运维工程师", 3, "演示：运维部");
            var postFm = await EnsurePostAsync("FM", "财务经理", 4, "演示：财务中心");
            var postMkt = await EnsurePostAsync("MKT", "市场专员", 5, "演示：市场部");
            await EnsurePostAsync("QA", "测试工程师", 6, "演示：停用态——不出现在用户表单的岗位下拉", enabled: false);

            // —— 演示角色（权限集合）：部门经理（组织+用户查看+财务只读）/ 普通员工（财务只读）——
            await EnsureRoleWithPermissionsAsync("dept-manager", new[]
            {
                "AbpAdmin.OrganizationUnits", "AbpIdentity.Users", "Finance.Ledger.View",
            });
            await EnsureRoleWithPermissionsAsync("employee", new[] { "Finance.Ledger.View" });
            var employeeRole = await _roles.FindByNormalizedNameAsync("EMPLOYEE");

            // —— 组织绑角色：研发部 → 普通员工（该部门成员自动获得，李工的权限来源）——
            // OrganizationUnitRole 是复合键实体，判重走仓储的「该 OU 已绑角色」查询
            if (employeeRole is { })
            {
                var rdRoles = await _organizationUnitRepository.GetRolesAsync(rd);
                if (rdRoles.All(x => x.Id != employeeRole.Id))
                {
                    await _organizationUnitManager.AddRoleToOrganizationUnitAsync(employeeRole.Id, rd.Id);
                }
            }

            // —— 演示人物（组合对照）——
            async Task<IdentityUser> DemoUserAsync(string userName, string email, string display)
                => await EnsureUserAsync(null, userName, email, display, null);

            async Task PutInOuAsync(IdentityUser user, OrganizationUnit ou)
            {
                if (!await _userManager.IsInOrganizationUnitAsync(user.Id, ou.Id))
                {
                    await _userManager.AddToOrganizationUnitAsync(user.Id, ou.Id);
                }
            }

            async Task PutOnPostAsync(IdentityUser user, Post post)
            {
                if (!await _userPostRepository.AnyAsync(x => x.UserId == user.Id && x.PostId == post.Id))
                {
                    await _userPostRepository.InsertAsync(
                        new IdentityUserPost(user.Id, post.Id, _currentTenant.Id), autoSave: true);
                }
            }

            var zhang = await DemoUserAsync("zhang.cto", "zhang.cto@demo.local", "张智（演示：CTO·跨部门）");
            await PutInOuAsync(zhang, techCenter);
            await PutInOuAsync(zhang, rd); // 一人多部门
            await PutOnPostAsync(zhang, postCto);
            if (!await _userManager.IsInRoleAsync(zhang, "dept-manager"))
            {
                (await _userManager.AddToRoleAsync(zhang, "dept-manager")).CheckErrors();
            }

            // 李工：无直接角色（权限来自研发部 OU 的角色绑定）+ 一人双岗位（兼运维）
            var li = await DemoUserAsync("li.dev", "li.dev@demo.local", "李工（演示：兼岗·权限来自部门）");
            await PutInOuAsync(li, rd);
            await PutOnPostAsync(li, postDev);
            await PutOnPostAsync(li, postSre);

            var wang = await DemoUserAsync("wang.fm", "wang.fm@demo.local", "王财（演示：财务经理·复用财务角色）");
            await PutInOuAsync(wang, financeCenter);
            await PutOnPostAsync(wang, postFm);
            if (!await _userManager.IsInRoleAsync(wang, "fin-accountant"))
            {
                (await _userManager.AddToRoleAsync(wang, "fin-accountant")).CheckErrors();
            }

            var zhao = await DemoUserAsync("zhao.mkt", "zhao.mkt@demo.local", "赵营销（演示：直接给角色对照）");
            await PutInOuAsync(zhao, marketing);
            await PutOnPostAsync(zhao, postMkt);
            if (!await _userManager.IsInRoleAsync(zhao, "employee"))
            {
                (await _userManager.AddToRoleAsync(zhao, "employee")).CheckErrors();
            }

            _ = group;

            await ambient.CompleteAsync();
        }
    }

    /// <summary>
    /// host 侧财务角色的权限面（宿主运营自己用，非 demo 租户）：fin-accountant = 全操作
    /// （过账/红冲/调账全生命周期/科目管理/对账/期间查看），fin-boss = 只读 + 审批——
    /// 审批与操作分权（operator 提交、boss 放行），不给 boss 操作位。
    /// 授权行走 ("R", 角色名)、host 作用域（见 EnsureRoleWithPermissionsAsync）。
    /// </summary>
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
        // FindByNormalizedNameAsync 按归一化名匹配，Identity 默认归一器是大写——传原始名永远查不到
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
            // 同上：用户查重按归一化名（大写）
            var existing = await _users.FindByNormalizedUserNameAsync(userName.ToUpperInvariant());
            if (existing is not null)
            {
                return existing;
            }

            var user = new IdentityUser(_guids.Create(), userName, email, tenantId);
            (await _userManager.CreateAsync(user, DemoPassword)).CheckErrors();
            // display claim：演示账号的中文展示名（角色/用户列表与审批单展示申请人时读取）
            (await _userManager.AddClaimAsync(user, new Claim("display", display))).CheckErrors();
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
