using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Ledger.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;

namespace AbpAdmin.Ledger;

/// <summary>
/// Ledger 测试模块：程序集专属 Testcontainers PG 库，真迁移（框架 + Ledger），真事务（不禁 UoW 事务）。
/// 授权换回真 AbpAuthorizationService，权限判定由 <see cref="TestPermissionChecker"/> 按用户给（定稿 §5.2 角色表）。
/// </summary>
[DependsOn(typeof(AbpAdminLedgerModule), typeof(AbpAdminTestBaseModule))]
public class LedgerTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 测试应用的宿主环境显式钉 Production：不设时 ABP 按缺省解析，演示种子（LedgerDataSeed
        // Contributor 的 Finance:SeedDemoData 门控）可能随 IsDevelopment() 一起被打开，演示凭证
        // 与用例自建分录同科目过账，余额断言全部抬高（CI 首次全量实测：期望 100 实测 1350）。
        // 本模块没有再注入 Finance:SeedDemoData=false——环境钉死已足够；该键作为不依赖
        // 环境判定的强制关开关保留在种子门控里（部署形态需要时用）。
        context.Services.Replace(
            ServiceDescriptor.Singleton<Volo.Abp.IAbpHostEnvironment>(new Volo.Abp.AbpHostEnvironment
            {
                EnvironmentName = "Production",
            }));

        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        var database = AbpAdminTestPg.GetDatabase(typeof(LedgerTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var dbContext = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            dbContext.Database.Migrate();
        });
        database.MigrateOnce("LedgerDbContext", () =>
        {
            using var dbContext = new Data.LedgerDbContext(
                new DbContextOptionsBuilder<Data.LedgerDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(LedgerConsts.SchemaHistoryTable))
                    .Options);
            dbContext.Database.Migrate();
        });

        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = database.ConnectionString;
        });

        // 撤销 TestBase 的 AddAlwaysAllowAuthorization：权限测试要真判定
        context.Services.Replace(ServiceDescriptor.Transient<IAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IAbpAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IMethodInvocationAuthorizationService, MethodInvocationAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Singleton<IPermissionChecker, TestPermissionChecker>());
    }
}

/// <summary>测试用户与定稿 §5.2 角色权限。</summary>
public static class TestUsers
{
    /// <summary>TestBase FakeCurrentPrincipalAccessor 的默认用户：全权限（种子/工具场景）。</summary>
    public static readonly Guid Admin = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    public static readonly Guid AccountantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid AccountantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    public static readonly Guid Boss = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    public static readonly Guid Agent = Guid.Parse("d0000000-0000-0000-0000-00000000000d");
    public static readonly Guid Nobody = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    /// <summary>会计权限 + Finance.Adjust.ApproveOwn（老板在角色页勾上自批）。</summary>
    public static readonly Guid AccountantSelfApprover = Guid.Parse("f0000000-0000-0000-0000-00000000000f");
    /// <summary>老板权限 + Finance.Adjust.Create（老板给自己勾上建单）。</summary>
    public static readonly Guid BossWithCreate = Guid.Parse("c1000000-0000-0000-0000-0000000000c1");

    public static readonly string[] Accountant =
    {
        FinancePermissions.Ledger.View, FinancePermissions.Ledger.Export, FinancePermissions.Account.Manage,
        FinancePermissions.Rule.View, FinancePermissions.Voucher.View, FinancePermissions.Voucher.Reverse,
        FinancePermissions.Adjust.Create, FinancePermissions.Adjust.Submit, FinancePermissions.Adjust.Approve,
        FinancePermissions.Adjust.Reject, FinancePermissions.Adjust.Reverse, FinancePermissions.Reconcile.Run,
        FinancePermissions.Period.View, FinancePermissions.BizDoc.View, FinancePermissions.Balance.View,
    };

    public static readonly string[] BossPerms =
    {
        // 定稿 §5.2：老板 Approve+Reject，无 Create/Submit/Reverse/ApproveOwn
        FinancePermissions.Ledger.View, FinancePermissions.Ledger.Export, FinancePermissions.Rule.View,
        FinancePermissions.Voucher.View, FinancePermissions.Adjust.Approve, FinancePermissions.Adjust.Reject,
        FinancePermissions.Period.View,
        FinancePermissions.BizDoc.View, FinancePermissions.Balance.View,
    };

    public static readonly string[] AgentPerms =
    {
        FinancePermissions.BizDoc.View, FinancePermissions.Balance.View, FinancePermissions.Scope.Agent,
    };

    public static ClaimsPrincipal Principal(Guid id) =>
        new(new ClaimsIdentity(new[] { new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, id.ToString("N")[..8]) }, "Test"));
}

public class TestPermissionChecker : IPermissionChecker
{
    private static readonly Dictionary<Guid, HashSet<string>> Grants = new()
    {
        [TestUsers.AccountantA] = TestUsers.Accountant.ToHashSet(),
        [TestUsers.AccountantB] = TestUsers.Accountant.ToHashSet(),
        [TestUsers.Boss] = TestUsers.BossPerms.ToHashSet(),
        [TestUsers.Agent] = TestUsers.AgentPerms.ToHashSet(),
        [TestUsers.Nobody] = new HashSet<string>(),
        [TestUsers.AccountantSelfApprover] = TestUsers.Accountant.Append(FinancePermissions.Adjust.ApproveOwn).ToHashSet(),
        [TestUsers.BossWithCreate] = TestUsers.BossPerms.Append(FinancePermissions.Adjust.Create).ToHashSet(),
    };

    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public TestPermissionChecker(ICurrentPrincipalAccessor principalAccessor)
    {
        _principalAccessor = principalAccessor;
    }

    public Task<bool> IsGrantedAsync(string name) => IsGrantedAsync(_principalAccessor.Principal, name);

    public Task<bool> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string name)
    {
        var raw = claimsPrincipal?.FindFirst(AbpClaimTypes.UserId)?.Value;
        if (!Guid.TryParse(raw, out var id))
        {
            return Task.FromResult(false);
        }

        // Scope.Agent 是「收窄」权限：只给代理，管理员默认不带（否则会被强制按自己过滤）
        if (id == TestUsers.Admin)
        {
            return Task.FromResult(name != FinancePermissions.Scope.Agent && name != FinancePermissions.Adjust.ApproveOwn);
        }

        return Task.FromResult(Grants.TryGetValue(id, out var set) && set.Contains(name));
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names) => IsGrantedAsync(_principalAccessor.Principal, names);

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string[] names)
    {
        var result = new MultiplePermissionGrantResult();
        foreach (var n in names)
        {
            result.Result[n] = await IsGrantedAsync(claimsPrincipal, n) ? PermissionGrantResult.Granted : PermissionGrantResult.Prohibited;
        }

        return result;
    }
}

public abstract class LedgerTestBase : AbpAdminTestBase<LedgerTestModule>
{
    protected LedgerTestBase()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
    }

    protected IDisposable As(Guid userId) =>
        GetRequiredService<ICurrentPrincipalAccessor>().Change(TestUsers.Principal(userId));
}
