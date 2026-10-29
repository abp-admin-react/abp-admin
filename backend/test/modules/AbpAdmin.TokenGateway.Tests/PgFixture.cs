using Xunit;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway;

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class PlainProtector : IKeyProtector
{
    public string Protect(string plain) => "enc:" + plain;
    public string Unprotect(string cipher) => cipher[4..];
}

public sealed class TestDbFactory(string cs) : IDbContextFactory<TokenGatewayDbContext>
{
    public TokenGatewayDbContext CreateDbContext()
    {
        var b = new DbContextOptionsBuilder<TokenGatewayDbContext>();
        TokenGatewayDbContext.ConfigureNpgsql(b, cs);
        return new TokenGatewayDbContext(b.Options);
    }
}

/// <summary>真 PG（Testcontainers），跑真实迁移；每个用例用新用户 Id 隔离。</summary>
public sealed class PgFixture
{
    public IDbContextFactory<TokenGatewayDbContext> Db { get; }

    public PgFixture()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var database = AbpAdminTestPg.GetDatabase(typeof(PgFixture).Assembly);
        Db = new TestDbFactory(database.ConnectionString);
        database.MigrateOnce("token-gateway", () =>
        {
            using var db = Db.CreateDbContext();
            db.Database.Migrate();
        });
    }
}

[CollectionDefinition(Name)]
public sealed class PgCollection : ICollectionFixture<PgFixture>
{
    public const string Name = "tg-pg";
}

/// <summary>NV-08：身份状态门测试桩。默认放行，置 Usable=false 模拟停用/锁定/已删。</summary>
public sealed class TestUserGate : ITgUserGate
{
    public bool Usable { get; set; } = true;
    public int Calls { get; private set; }

    public Task<bool> IsUsableAsync(Guid userId)
    {
        Calls++;
        return Task.FromResult(Usable);
    }
}

public abstract class GatewayTestBase(PgFixture pg)
{
    protected readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));
    protected readonly IOptions<TokenGatewayOptions> Opt = Options.Create(new TokenGatewayOptions());
    protected readonly Guid U = Guid.NewGuid();
    protected IDbContextFactory<TokenGatewayDbContext> Db => pg.Db;
    protected readonly TestUserGate UserGate = new();

    protected Wallet NewWallet() => new(Db, Clock);
    protected PlanCatalog NewPlans() => new(Db, Clock);
    protected KeyStore NewKeys() => new(Db, Clock, new PlainProtector(), Opt, UserGate);
    protected UsageLog NewUsage() => new(Db, Clock);

    protected (OrderBook Orders, Wallet Wallet) NewOrders()
    {
        var w = NewWallet();
        return (new OrderBook(Db, Clock, NewPlans(), w, Opt), w);
    }
}
