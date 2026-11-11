using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

public class RemoveUnusedAdminFeaturesMigrationTests
{
    [Fact]
    public async Task Upgrade_Should_Retire_Exact_Legacy_Entries_And_Preserve_Custom_Permissions()
    {
        var schema = "admin_features_retirement_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(AbpAdminTestPg.ContainerConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(AbpAdminTestPg.ContainerConnectionString)
            {
                SearchPath = schema,
                Pooling = false,
            }.ConnectionString;
            await using var context = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(connectionString).Options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261110115501_RemovePosts");
            await using (var seedConnection = new NpgsqlConnection(connectionString))
            {
                await seedConnection.OpenAsync();
                await using var seed = new NpgsqlCommand(SeedSql, seedConnection);
                await seed.ExecuteNonQueryAsync();
            }

            var targetMigration = context.Database.GetMigrations()
                .Single(x => x.EndsWith("_RemoveUnusedAdminFeatures", StringComparison.Ordinal));
            await migrator.MigrateAsync(targetMigration);

            (await context.Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('"AppMenuGrants"') IS NULL AND to_regclass('"AppDataScopeDemos"') IS NULL AS "Value" """)
                .SingleAsync()).ShouldBeTrue();

            var menuPaths = await context.Database.SqlQueryRaw<string>(
                """SELECT "Path" AS "Value" FROM "AppMenus" ORDER BY "Path" """).ToArrayAsync();
            menuPaths.ShouldBe(new[]
            {
                "/administration", "/biz-template/data-scope", "/custom-kept", "/custom-permission-binding",
                "/legacy-role-only", "/legacy-role-only-child", "/tenant-legacy-role-only", "/tenant-public"
            });
            var hiddenMenuPaths = await context.Database.SqlQueryRaw<string>(
                """SELECT "Path" AS "Value" FROM "AppMenus" WHERE "IsHide" ORDER BY "Path" """)
                .ToArrayAsync();
            // 原来仅靠角色勾选受限的菜单隐藏。绑定功能权限、无角色授权、非 R 授权
            // 以及其他租户的孤立授权都不能改变菜单现有的隐藏设置。
            hiddenMenuPaths.ShouldBe(new[] { "/legacy-role-only", "/tenant-legacy-role-only" });
            (await context.Database.SqlQueryRaw<string>(
                """
                SELECT parent."Path" AS "Value" FROM "AppMenus" child
                JOIN "AppMenus" parent ON parent."Id" = child."ParentId"
                WHERE child."Path" = '/legacy-role-only-child'
                """).SingleAsync()).ShouldBe("/legacy-role-only");
            (await context.Database.SqlQueryRaw<string>(
                """
                SELECT parent."Path" AS "Value" FROM "AppMenus" child
                JOIN "AppMenus" parent ON parent."Id" = child."ParentId"
                WHERE child."Path" = '/custom-kept'
                """).SingleAsync()).ShouldBe("/administration");
            (await context.Database.SqlQueryRaw<string>(
                """SELECT "PermissionName" AS "Value" FROM "AppMenus" WHERE "Path" = '/custom-permission-binding' """)
                .SingleAsync()).ShouldBe("AbpAdmin.Menus.AssignRoles");

            var expectedPermissions = new[]
            {
                "AbpAdmin.BackgroundJobs", "AbpAdmin.Menus", "AbpAdmin.PermissionDefinitions.Custom",
                "AbpAdmin.PermissionDefinitions.Custom.View", "BizTemplate.DataScopeDemo"
            };
            (await context.Database.SqlQueryRaw<string>(
                """SELECT "Name" AS "Value" FROM "AbpPermissions" ORDER BY "Name" """)
                .ToArrayAsync()).ShouldBe(expectedPermissions);
            (await context.Database.SqlQueryRaw<string>(
                """SELECT "Name" AS "Value" FROM "AbpPermissionGrants" ORDER BY "Name" """)
                .ToArrayAsync()).ShouldBe(expectedPermissions);
            (await context.Database.SqlQueryRaw<bool>(
                """
                SELECT "ParentName" IS NULL AS "Value" FROM "AbpPermissions"
                WHERE "Name" = 'AbpAdmin.PermissionDefinitions.Custom'
                """).SingleAsync()).ShouldBeTrue();
            (await context.Database.SqlQueryRaw<string>(
                """
                SELECT "ParentName" AS "Value" FROM "AbpPermissions"
                WHERE "Name" = 'AbpAdmin.PermissionDefinitions.Custom.View'
                """).SingleAsync()).ShouldBe("AbpAdmin.PermissionDefinitions.Custom");
            (await context.Database.SqlQueryRaw<string>(
                """
                SELECT menu."Path" AS "Value" FROM "AppTenantPackageMenus" selection
                JOIN "AppMenus" menu ON menu."Id" = selection."TemplateMenuId"
                ORDER BY menu."Path"
                """).ToArrayAsync()).ShouldBe(new[] { "/biz-template/data-scope", "/custom-kept" });
            (await context.Database.SqlQueryRaw<int>(
                """SELECT count(*)::int AS "Value" FROM "AppTenantPackageMenus" """).SingleAsync()).ShouldBe(2);
            (await context.Database.SqlQueryRaw<string>(
                """SELECT "Name" AS "Value" FROM "AppTenantPackages" """).SingleAsync()).ShouldBe("保留套餐");
            (await context.Database.SqlQueryRaw<string>(
                """SELECT "RoleName" AS "Value" FROM "AppRoleDataScopes" """).SingleAsync()).ShouldBe("custom-scope-role");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private const string SeedSql = """
        INSERT INTO "AppMenus" ("Id", "ParentId", "Path", "PermissionName", "TenantId", "Type",
            "Title", "OrderNo", "IsHide", "IsEnabled", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        SELECT md5(path || COALESCE(tenant, 'host'))::uuid,
            md5(parent_path || COALESCE(tenant, 'host'))::uuid,
            path, permission, tenant::uuid, 1, path, 0, FALSE, TRUE, '{}', 'test', NOW(), FALSE
        FROM (VALUES
            ('/administration', NULL, NULL, NULL),
            ('/data-scope-demo', '/administration', 'AbpAdmin.DataScopes.Demo', NULL),
            ('/data-scope-demo', NULL, NULL, '30000000-0000-0000-0000-000000000001'),
            ('/current-session', '/data-scope-demo', NULL, NULL),
            ('/custom-kept', '/current-session', 'AbpAdmin.Menus', NULL),
            ('/administration/permission-definitions', '/administration', 'AbpAdmin.PermissionDefinitions', NULL),
            ('/biz-template/data-scope', '/administration', 'BizTemplate.DataScopeDemo', NULL),
            ('/custom-permission-binding', '/administration', 'AbpAdmin.Menus.AssignRoles', NULL),
            ('/legacy-role-only', '/administration', NULL, NULL),
            ('/legacy-role-only-child', '/legacy-role-only', NULL, NULL),
            ('/tenant-legacy-role-only', NULL, NULL, '30000000-0000-0000-0000-000000000001'),
            ('/tenant-public', NULL, NULL, '30000000-0000-0000-0000-000000000001')
        ) AS seed(path, parent_path, permission, tenant);

        INSERT INTO "AppMenuGrants" ("Id", "MenuId", "ProviderName", "ProviderKey", "TenantId")
        SELECT md5('old-menu-grant-' || "Id")::uuid, "Id",
            CASE WHEN "Path" = '/administration' THEN 'U' ELSE 'R' END, 'employee',
            CASE WHEN "Path" = '/tenant-public' THEN NULL ELSE "TenantId" END
        FROM "AppMenus"
        WHERE "Path" IN (
            '/current-session', '/custom-kept', '/administration', '/legacy-role-only',
            '/tenant-legacy-role-only', '/tenant-public');
        INSERT INTO "AppDataScopeDemos" ("Id", "Name", "ExtraProperties", "ConcurrencyStamp", "CreationTime")
        VALUES ('20000000-0000-0000-0000-000000000001', '旧框架示例', '{}', 'test', NOW());
        INSERT INTO "AppRoleDataScopes" ("Id", "RoleName", "ScopeType", "ExtraProperties", "ConcurrencyStamp", "CreationTime")
        VALUES ('20000000-0000-0000-0000-000000000002', 'custom-scope-role', 4, '{}', 'test', NOW());

        INSERT INTO "AppTenantPackages" ("Id", "Name", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        VALUES ('40000000-0000-0000-0000-000000000001', '保留套餐', '{}', 'test', NOW(), FALSE);
        INSERT INTO "AppTenantPackageMenus" ("Id", "PackageId", "TemplateMenuId")
        SELECT md5('package-' || "Id")::uuid, '40000000-0000-0000-0000-000000000001', "Id"
        FROM "AppMenus" WHERE "Path" IN ('/current-session', '/custom-kept', '/biz-template/data-scope');

        INSERT INTO "AbpPermissions" ("Id", "GroupName", "Name", "DisplayName", "ParentName", "IsEnabled", "MultiTenancySide")
        SELECT md5(name)::uuid, 'AbpAdmin', name, name,
            CASE name
                WHEN 'AbpAdmin.PermissionDefinitions.Custom' THEN 'AbpAdmin.PermissionDefinitions'
                WHEN 'AbpAdmin.PermissionDefinitions.Custom.View' THEN 'AbpAdmin.PermissionDefinitions.Custom'
            END,
            TRUE, 3
        FROM unnest(ARRAY[
            'AbpAdmin.BackgroundJobs.Enqueue', 'AbpAdmin.Menus.AssignRoles', 'AbpAdmin.DataScopes.Demo', 'AbpAdmin.PermissionDefinitions',
            'AbpAdmin.PermissionDefinitions.Create', 'AbpAdmin.PermissionDefinitions.Update', 'AbpAdmin.PermissionDefinitions.Delete',
            'AbpAdmin.BackgroundJobs', 'AbpAdmin.Menus', 'AbpAdmin.PermissionDefinitions.Custom',
            'AbpAdmin.PermissionDefinitions.Custom.View', 'BizTemplate.DataScopeDemo'
        ]) AS seed(name);
        INSERT INTO "AbpPermissionGrants" ("Id", "Name", "ProviderName", "ProviderKey")
        SELECT md5('permission-' || "Name")::uuid, "Name", 'R', 'employee' FROM "AbpPermissions";
        """;
}
