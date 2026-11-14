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

public class RemoveEasyAbpPaymentServiceMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_Install_And_Upgrade_Should_Retire_Old_Payment_Schema(bool upgrade)
    {
        var schema = "payment_retirement_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(AbpAdminTestPg.ContainerConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
        await create.ExecuteNonQueryAsync();

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
            var targetMigration = context.Database.GetMigrations()
                .Single(id => id.EndsWith("_RemoveEasyAbpPaymentService", StringComparison.Ordinal));

            if (upgrade)
            {
                await migrator.MigrateAsync("20261111022011_RemoveUnusedAdminFeatures");
                (await CountLegacyTablesAsync(context)).ShouldBe(10);

                await using var seedConnection = new NpgsqlConnection(connectionString);
                await seedConnection.OpenAsync();
                await using var seed = new NpgsqlCommand(SeedSql, seedConnection);
                await seed.ExecuteNonQueryAsync();
            }

            await migrator.MigrateAsync(targetMigration);

            (await CountLegacyTablesAsync(context)).ShouldBe(0);
            (await context.Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('"AbpUsers"') IS NOT NULL AS "Value" """)
                .SingleAsync()).ShouldBeTrue();

            if (upgrade)
            {
                (await context.Database.SqlQueryRaw<string>(
                    """SELECT "PermissionName" AS "Value" FROM "AppMenus" WHERE "Path" = '/administration/payments' """)
                    .SingleAsync()).ShouldBe("AbpAdmin.Collection.Payments.View");
                (await context.Database.SqlQueryRaw<string>(
                    """SELECT "Name" AS "Value" FROM "AbpPermissions" """)
                    .ToArrayAsync()).ShouldBe(new[] { "AbpAdmin.PaymentsArchive" });
                (await context.Database.SqlQueryRaw<string>(
                    """SELECT "Name" AS "Value" FROM "AbpPermissionGrants" """)
                    .ToArrayAsync()).ShouldBe(new[] { "AbpAdmin.PaymentsArchive" });
                (await context.Database.SqlQueryRaw<string>(
                    """SELECT "Name" AS "Value" FROM "AbpPermissionGroups" """)
                    .ToArrayAsync()).ShouldBe(new[] { "AbpAdmin" });
                (await context.Database.SqlQueryRaw<string>(
                    """SELECT "Name" AS "Value" FROM "AbpSettings" """)
                    .ToArrayAsync()).ShouldBe(new[] { "Finance.Ledger.Enabled" });
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static Task<int> CountLegacyTablesAsync(AbpAdminDbContext context) =>
        context.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::int AS "Value"
            FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name LIKE 'EasyAbpPaymentService%'
            """).SingleAsync();

    private const string SeedSql = """
        INSERT INTO "AppMenus" ("Id", "Path", "PermissionName", "Type", "Title", "OrderNo", "IsHide",
            "IsEnabled", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        VALUES ('10000000-0000-0000-0000-000000000001', '/administration/payments', 'AbpAdmin.Payments',
            1, '支付记录', 0, FALSE, TRUE, '{}', 'test', NOW(), FALSE);

        INSERT INTO "AbpPermissionGroups" ("Id", "Name", "DisplayName")
        SELECT md5(name)::uuid, name, name
        FROM unnest(ARRAY['AbpAdmin', 'EasyAbp.PaymentService', 'EasyAbp.PaymentService.Prepayment',
            'EasyAbp.PaymentService.WeChatPay']) AS name;
        INSERT INTO "AbpPermissions" ("Id", "GroupName", "Name", "DisplayName", "IsEnabled", "MultiTenancySide")
        SELECT md5(name)::uuid, group_name, name, name, TRUE, 3
        FROM (VALUES ('AbpAdmin', 'AbpAdmin.Payments'), ('AbpAdmin', 'AbpAdmin.Payments.Refund'),
            ('AbpAdmin', 'AbpAdmin.PaymentsArchive'), ('EasyAbp.PaymentService', 'EasyAbp.PaymentService.Payment'),
            ('EasyAbp.PaymentService.Prepayment', 'EasyAbp.PaymentService.Prepayment.Account'))
            AS seed(group_name, name);
        INSERT INTO "AbpPermissionGrants" ("Id", "Name", "ProviderName", "ProviderKey")
        SELECT md5('grant-' || "Name")::uuid, "Name", 'R', 'admin' FROM "AbpPermissions";

        INSERT INTO "AbpSettings" ("Id", "Name", "Value", "ProviderName")
        SELECT md5(name)::uuid, name, 'false', 'G'
        FROM unnest(ARRAY['Finance.Ledger.Enabled', 'Finance.Prepayment.FreezeWrites',
            'AbpAdmin.Payment.WeChatPay.MchId', 'EasyAbp.PaymentService.Free.DefaultPayeeAccount']) AS name;
        """;
}
