using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ledger.Data;

public class LedgerDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public LedgerDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<LedgerDbContext>();

        await EfCoreLegacySchemaBaseliner.StampIfNeededAsync(
            dbContext.Database.GetDbConnection(),
            _serviceProvider.GetRequiredService<ILogger<LedgerDbSchemaMigrator>>(),
            historyTableName: LedgerConsts.SchemaHistoryTable,
            constraintName: "PK_" + LedgerConsts.SchemaHistoryTable,
            initialMigrationId: LedgerConsts.InitialMigrationId,
            baselineProductVersion: "10.0.9",
            sentinelTableName: "FinAccounts");

        await dbContext.Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<LedgerDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }
}
