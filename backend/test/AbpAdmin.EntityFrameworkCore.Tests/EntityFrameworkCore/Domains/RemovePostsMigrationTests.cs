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

/// <summary>在 Testcontainers 的独立 schema 中验证存量岗位数据的精确退役，不连接现有数据库。</summary>
public class RemovePostsMigrationTests
{
    [Fact]
    public async Task Upgrade_Should_Retire_Posts_And_Preserve_Unrelated_Menu_And_Permission_Data()
    {
        var schema = "posts_retirement_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(AbpAdminTestPg.ContainerConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var connection = new NpgsqlConnectionStringBuilder(AbpAdminTestPg.ContainerConnectionString)
            {
                SearchPath = schema,
                Pooling = false,
            }.ConnectionString;
            await using var context = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(connection).Options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261008092815_MenuGrant_ProviderKey256");
            await using (var seedConnection = new NpgsqlConnection(connection))
            {
                await seedConnection.OpenAsync();
                await using var seed = new NpgsqlCommand(SeedSql, seedConnection);
                await seed.ExecuteNonQueryAsync();
            }

            await migrator.MigrateAsync("20261110115501_RemovePosts");

            (await context.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('\"AppPosts\"') IS NULL AND to_regclass('\"AppIdentityUserPosts\"') IS NULL AS \"Value\"")
                .SingleAsync()).ShouldBeTrue();
            var paths = await context.Database.SqlQueryRaw<string>(
                "SELECT \"Path\" AS \"Value\" FROM \"AppMenus\" ORDER BY \"Path\"").ToArrayAsync();
            paths.ShouldBe(new[] { "/custom-kept", "/lookalike-kept", "/root" });
            // 两层退役祖先下的自定义子项挂回原存活目录，避免保留节点因断链被隐藏。
            (await context.Database.SqlQueryRaw<Guid>(
                "SELECT \"ParentId\" AS \"Value\" FROM \"AppMenus\" WHERE \"Path\" = '/custom-kept'")
                .SingleAsync()).ShouldBe(Guid.Parse("10000000-0000-0000-0000-000000000001"));
            (await context.Database.SqlQueryRaw<string>(
                "SELECT \"Name\" AS \"Value\" FROM \"AbpPermissions\"").ToArrayAsync())
                .ShouldBe(new[] { "AbpAdmin.PostsArchive" });
            (await context.Database.SqlQueryRaw<string>(
                "SELECT \"Name\" AS \"Value\" FROM \"AbpPermissionGrants\"").ToArrayAsync())
                .ShouldBe(new[] { "AbpAdmin.PostsArchive" });
            (await context.Database.SqlQueryRaw<Guid>(
                "SELECT \"MenuId\" AS \"Value\" FROM \"AppMenuGrants\"").ToArrayAsync())
                .ShouldBe(new[] { Guid.Parse("10000000-0000-0000-0000-000000000005") });
            (await context.Database.SqlQueryRaw<Guid>(
                "SELECT \"TemplateMenuId\" AS \"Value\" FROM \"AppTenantPackageMenus\"").ToArrayAsync())
                .ShouldBe(new[] { Guid.Parse("10000000-0000-0000-0000-000000000005") });
            (await context.Database.SqlQueryRaw<string>(
                "SELECT \"Name\" AS \"Value\" FROM \"AppTenantPackages\"").SingleAsync())
                .ShouldBe("保留套餐");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private const string SeedSql = """
        INSERT INTO "AppPosts" ("Id", "Name", "Code", "SortOrder", "Status",
            "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        VALUES ('20000000-0000-0000-0000-000000000001', '旧岗位', 'LEGACY', 0, 0, '{}', 'test', NOW(), FALSE);
        INSERT INTO "AppIdentityUserPosts" ("UserId", "PostId")
        VALUES ('20000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001');

        INSERT INTO "AppMenus" ("Id", "ParentId", "Path", "PermissionName", "TenantId", "Type",
            "Title", "OrderNo", "IsHide", "IsEnabled", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        SELECT seed.id, seed.parent, seed.path, seed.permission, seed.tenant, 1, seed.path, 0, FALSE, TRUE, '{}', 'test', NOW(), FALSE
        FROM (VALUES
            ('10000000-0000-0000-0000-000000000001'::uuid, NULL::uuid, '/root', NULL::text, NULL::uuid),
            ('10000000-0000-0000-0000-000000000002'::uuid, '10000000-0000-0000-0000-000000000001'::uuid,
                '/administration/identity/posts', 'AbpAdmin.Posts', NULL::uuid),
            ('10000000-0000-0000-0000-000000000003'::uuid, NULL::uuid,
                '/administration/identity/posts', NULL::text, '30000000-0000-0000-0000-000000000001'::uuid),
            ('10000000-0000-0000-0000-000000000004'::uuid, '10000000-0000-0000-0000-000000000002'::uuid,
                '/retired-child', 'AbpAdmin.Posts.Create', NULL::uuid),
            ('10000000-0000-0000-0000-000000000005'::uuid, '10000000-0000-0000-0000-000000000004'::uuid,
                '/custom-kept', 'AbpAdmin.Menus', NULL::uuid),
            ('10000000-0000-0000-0000-000000000006'::uuid, NULL::uuid,
                '/lookalike-kept', 'AbpAdmin.PostsArchive', NULL::uuid)
        ) AS seed(id, parent, path, permission, tenant);

        INSERT INTO "AppMenuGrants" ("Id", "MenuId", "ProviderName", "ProviderKey")
        SELECT md5('grant-' || "Id")::uuid, "Id", 'R', 'employee'
        FROM "AppMenus" WHERE "Path" IN ('/administration/identity/posts', '/retired-child', '/custom-kept');
        INSERT INTO "AppTenantPackages" ("Id", "Name", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        VALUES ('40000000-0000-0000-0000-000000000001', '保留套餐', '{}', 'test', NOW(), FALSE);
        INSERT INTO "AppTenantPackageMenus" ("Id", "PackageId", "TemplateMenuId")
        SELECT md5('package-' || "Id")::uuid, '40000000-0000-0000-0000-000000000001', "Id"
        FROM "AppMenus" WHERE "Path" IN ('/administration/identity/posts', '/custom-kept');

        INSERT INTO "AbpPermissions" ("Id", "GroupName", "Name", "DisplayName", "IsEnabled", "MultiTenancySide")
        SELECT md5(name)::uuid, 'AbpAdmin', name, name, TRUE, 3
        FROM unnest(ARRAY['AbpAdmin.Posts', 'AbpAdmin.Posts.Create', 'AbpAdmin.Posts.Update',
            'AbpAdmin.Posts.Delete', 'AbpAdmin.Posts.ManageMembers', 'AbpAdmin.PostsArchive']) AS name;
        INSERT INTO "AbpPermissionGrants" ("Id", "Name", "ProviderName", "ProviderKey")
        SELECT md5('permission-' || "Name")::uuid, "Name", 'R', 'employee' FROM "AbpPermissions";
        """;
}
