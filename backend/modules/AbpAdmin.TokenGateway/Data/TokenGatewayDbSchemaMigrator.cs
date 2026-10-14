using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.TokenGateway.Data;

/// <summary>迁移循环约定点：宿主 AutoMigrateOnStartup / DbMigrator 自动枚举。</summary>
public class TokenGatewayDbSchemaMigrator(IDbContextFactory<TokenGatewayDbContext> factory) : IAbpAdminDbSchemaMigrator
{
    public async Task MigrateAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Database.GetPendingMigrationsAsync()).Any();
    }
}
