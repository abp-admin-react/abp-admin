using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Controllers;
using AbpAdmin.Grids;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using MiniExcelLibs;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Identity.Settings;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Identity;

/* 用户导出与周期改密集成测试。
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)。
 */
public abstract class UserImportExportTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIdentityUserAdminAppService _identityUserAdminAppService;
    private readonly IdentityUserManager _userManager;
    private readonly ISettingManager _settingManager;

    protected UserImportExportTests()
    {
        _identityUserAdminAppService = GetRequiredService<IIdentityUserAdminAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    /// <summary>
    /// 创建测试用户并返回用户 ID。
    /// </summary>
    private async Task<Guid> CreateTestUserAsync(string userName, string email, string password)
    {
        var user = new IdentityUser(
            Guid.NewGuid(),
            userName,
            email);

        var result = await _userManager.CreateAsync(user, password);
        result.Succeeded.ShouldBeTrue(
            string.Join("; ", result.Errors.Select(e => e.Description)));

        return user.Id;
    }

    /// <summary>
    /// 清理测试用户。
    /// </summary>
    private async Task CleanupTestUserAsync(Guid userId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        });
    }

    #region 导出测试

    [Fact]
    public async Task EnqueueExportAsync_Should_Return_Queued_Status()
    {
        // Act
        var result = await _identityUserAdminAppService.EnqueueExportAsync(null);

        // Assert
        result.ShouldNotBeNull();
        result.IsQueued.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrEmpty();
        result.Message.ShouldContain("已排队");
    }

    [Fact]
    public async Task GridExport_Should_Keep_Filtered_Order_Across_Pages_And_Mask_Contact_Details()
    {
        var prefix = "grid-export-" + Guid.NewGuid().ToString("N")[..8];
        var usernames = new[] { prefix + "-a", prefix + "-b" };
        var ids = new List<Guid>();
        try
        {
            foreach (var name in usernames)
                ids.Add(await CreateTestUserAsync(name, name + "@example.org", "Test@123456"));

            var query = new GridQueryInput
            {
                SkipCount = 1, MaxResultCount = 1, Keyword = prefix.ToUpperInvariant(),
                Filters = [new() { Field = "isActive", Conditions = [new() { Op = "eq", Value = "true" }] }],
                Sort = [new() { Field = "userName", Direction = "desc" }]
            };
            var builder = GetRequiredService<IUserExcelBuilder>();
            var asOf = DateTimeOffset.UtcNow;
            (await builder.CountGridAsync(query, true, asOf)).ShouldBe(2);
            using var stream = new MemoryStream(await builder.BuildGridAsync(query, 1000, true, asOf));
            var rows = stream.Query(useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
            rows.Select(r => r[UserExportColumnNames.UserName]?.ToString()).ShouldBe(usernames.Reverse());
            foreach (var row in rows)
                row[UserExportColumnNames.Email].ToString().ShouldNotBeNull().ShouldContain("*");
        }
        finally
        {
            foreach (var id in ids)
                await CleanupTestUserAsync(id);
        }
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public async Task GridExport_Should_Choose_Download_Or_Queue_Using_The_Filtered_Count(int total, bool queued)
    {
        var query = new GridQueryInput { Keyword = "test" };
        var builder = Substitute.For<IUserExcelBuilder>();
        var jobs = Substitute.For<IBackgroundJobManager>();
        var permissions = Substitute.For<IPermissionChecker>();
        builder.CountGridAsync(query, true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((long)total);
        builder.BuildGridAsync(query, total, true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1 });
        var controller = new UserExportController(GetRequiredService<IIdentityUserRepository>(), builder,
            GetRequiredService<IRepository<UserExcelFile, Guid>>(), GetRequiredService<IBlobContainer<UserExportBlobContainer>>(), permissions)
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.ExportQueryAsync(query, jobs);
        if (queued)
        {
            result.ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<UserExportResultDto>().IsQueued.ShouldBeTrue();
            await jobs.Received(1).EnqueueAsync(Arg.Is<UserExportJobArgs>(a =>
                a.GridQuery == query && a.MaskSensitive && a.StatusAsOf != default && a.UserId != Guid.Empty));
        }
        else
        {
            result.ShouldBeOfType<FileContentResult>().FileContents.ShouldBe(new byte[] { 1 });
            await jobs.DidNotReceiveWithAnyArgs().EnqueueAsync<UserExportJobArgs>(default!);
        }
    }

    /// <summary>
    /// OU 列回归（bug 修复）：用户查询改 includeDetails:false 后，
    /// OU 列由 IdentityUserOrganizationUnit 连接表批量查询提供——导出必须仍带出 OU 显示名。
    /// </summary>
    [Fact]
    public async Task BuildAsync_Should_Export_OrganizationUnit_DisplayNames()
    {
        var userName = $"export-ou-{Guid.NewGuid().ToString("N")[..8]}";
        var userId = await CreateTestUserAsync(userName, $"{userName}@test.com", "Test@123456");

        // UserManager 的写操作走 EF 仓储（含集合加载），必须显式开 UoW
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ou = new OrganizationUnit(Guid.NewGuid(), $"ExportOu-{Guid.NewGuid().ToString("N")[..6]}", parentId: null);
        await WithUnitOfWorkAsync(async () =>
        {
            await ouManager.CreateAsync(ou);
            var user = (await _userManager.FindByIdAsync(userId.ToString()))!;
            await _userManager.AddToOrganizationUnitAsync(user, ou);
        });

        try
        {
            var builder = GetRequiredService<IUserExcelBuilder>();
            // maskSensitive:false 保持原有明文断言语义；脱敏输出另有专测
            var bytes = await builder.BuildAsync(userName, maxResultCount: 10, maskSensitive: false);

            using var stream = new MemoryStream(bytes);
            var rows = stream.Query(useHeaderRow: true)
                .Cast<IDictionary<string, object>>()
                .ToList();
            rows.ShouldNotBeEmpty();

            var row = rows.Single(x => x[UserExportColumnNames.UserName]?.ToString() == userName);
            row[UserExportColumnNames.Email]?.ToString().ShouldBe($"{userName}@test.com");
            row[UserExportColumnNames.OrganizationUnits]!.ToString()!.ShouldContain(ou.DisplayName);
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user != null)
                {
                    await _userManager.RemoveFromOrganizationUnitAsync(user, ou);
                }
                await ouManager.DeleteAsync(ou.Id);
            });
            await CleanupTestUserAsync(userId);
        }
    }

    [Fact]
    public async Task BuildAsync_Should_Export_Direct_Roles_Only()
    {
        var userName = $"export-role-{Guid.NewGuid().ToString("N")[..8]}";
        var directRoleName = "export-direct-" + Guid.NewGuid().ToString("N")[..6];
        var ouRoleName = "export-ou-role-" + Guid.NewGuid().ToString("N")[..6];
        var userId = await CreateTestUserAsync(userName, $"{userName}@test.com", "Test@123456");
        var roleManager = GetRequiredService<IdentityRoleManager>();
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ouRepository = GetRequiredService<IOrganizationUnitRepository>();
        Guid? ouId = null;
        Guid? directRoleId = null;
        Guid? ouRoleId = null;

        await WithUnitOfWorkAsync(async () =>
        {
            var directRole = new IdentityRole(Guid.NewGuid(), directRoleName);
            var ouRole = new IdentityRole(Guid.NewGuid(), ouRoleName);
            (await roleManager.CreateAsync(directRole)).Succeeded.ShouldBeTrue();
            (await roleManager.CreateAsync(ouRole)).Succeeded.ShouldBeTrue();
            directRoleId = directRole.Id;
            ouRoleId = ouRole.Id;
            var ou = new OrganizationUnit(Guid.NewGuid(), $"ExportRoleOu-{Guid.NewGuid().ToString("N")[..6]}");
            await ouManager.CreateAsync(ou);
            ouId = ou.Id;
        });
        await WithUnitOfWorkAsync(async () =>
        {
            await ouManager.AddRoleToOrganizationUnitAsync(ouRoleId!.Value, ouId!.Value);
            var user = (await _userManager.FindByIdAsync(userId.ToString()))!;
            (await _userManager.AddToRoleAsync(user, directRoleName)).Succeeded.ShouldBeTrue();
            await _userManager.AddToOrganizationUnitAsync(user, await ouRepository.GetAsync(ouId.Value));
        });

        try
        {
            var bytes = await GetRequiredService<IUserExcelBuilder>()
                .BuildAsync(userName, maxResultCount: 10, maskSensitive: false);
            using var stream = new MemoryStream(bytes);
            var row = stream.Query(useHeaderRow: true)
                .Cast<IDictionary<string, object>>()
                .Single(x => x[UserExportColumnNames.UserName]?.ToString() == userName);
            var roles = row[UserExportColumnNames.RoleNames]?.ToString() ?? "";
            roles.ShouldContain(directRoleName);
            roles.ShouldNotContain(ouRoleName);
        }
        finally
        {
            await CleanupTestUserAsync(userId);
            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var id in new[] { directRoleId, ouRoleId })
                {
                    if (id is not Guid roleId)
                    {
                        continue;
                    }

                    var role = await roleManager.FindByIdAsync(roleId.ToString());
                    if (role != null)
                    {
                        await roleManager.DeleteAsync(role);
                    }
                }
            });
            await WithUnitOfWorkAsync(async () =>
            {
                if (ouId is Guid id && await ouRepository.FindAsync(id) != null)
                {
                    await ouManager.DeleteAsync(id);
                }
            });
        }
    }

    #endregion

    #region 周期改密测试（ShouldPeriodicallyChangePasswordAsync，框架方法直测）

    /// <summary>
    /// 创建用户并设置密码最后修改时间（内存态即可，该方法只读实体字段与设置项）。
    /// </summary>
    private async Task<IdentityUser> CreateUserWithLastPasswordChangeAsync(
        string userName, DateTimeOffset? lastPasswordChangeTime, bool isExternal = false)
    {
        var user = new IdentityUser(Guid.NewGuid(), userName, $"{userName}@test.com");
        if (isExternal)
        {
            user.IsExternal = true;
            (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        }
        else
        {
            (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();
        }
        user.SetLastPasswordChangeTime(lastPasswordChangeTime);
        return user;
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Feature_Disabled()
    {
        // Arrange - 默认设置（未开启强制周期改密），即使密码很旧也不应要求改密
        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-off-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-365));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Period_Is_Zero()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "0");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-zero-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-365));
        try
        {
            // 0 值 = 不启用周期判定
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_True_When_Password_Expired()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-exp-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-40));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeTrue();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Password_Not_Expired()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-ok-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-10));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_For_External_User()
    {
        // 外部用户（IsExternal = true，无本地密码哈希）不受密码有效期影响——
        // 框架方法对无密码哈希的用户直接返回 false。
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-ext-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-40), isExternal: true);
        try
        {
            user.PasswordHash.ShouldBeNull();
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    #endregion

}
