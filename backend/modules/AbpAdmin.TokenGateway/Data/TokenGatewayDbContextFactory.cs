using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AbpAdmin.TokenGateway.Data;

/// <summary>
/// 设计时工厂（dotnet ef）。生成迁移不连库，给占位连接串即可：
/// <code>
/// dotnet ef migrations add &lt;Name&gt; --project backend/modules/AbpAdmin.TokenGateway
///   --startup-project backend/framework/AbpAdmin.DbMigrator --context TokenGatewayDbContext
/// </code>
/// </summary>
public class TokenGatewayDbContextFactory : IDesignTimeDbContextFactory<TokenGatewayDbContext>
{
    public TokenGatewayDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time_only";
        var b = new DbContextOptionsBuilder<TokenGatewayDbContext>();
        TokenGatewayDbContext.ConfigureNpgsql(b, cs);
        return new TokenGatewayDbContext(b.Options);
    }
}
