using System;
using AbpAdmin.DataScopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Modularity;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>只为过滤器用例扩展宿主模型；不会进入生产模型、迁移或 API。</summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class FrameworkDataScopeFilterTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(ServiceDescriptor.Transient<AbpAdminDbContext>(services =>
            new FrameworkDataScopeTestDbContext(services.GetRequiredService<DbContextOptions<AbpAdminDbContext>>())
            {
                LazyServiceProvider = services.GetRequiredService<IAbpLazyServiceProvider>(),
            }));
        context.Services.AddTransient<IRepository<FrameworkDataScopeRecord, Guid>,
            EfCoreRepository<AbpAdminDbContext, FrameworkDataScopeRecord, Guid>>();

        var database = AbpAdminTestPg.GetDatabase(typeof(AbpAdminEntityFrameworkCoreTestModule).Assembly);
        database.MigrateOnce(nameof(FrameworkDataScopeRecord), () =>
        {
            using var connection = new NpgsqlConnection(database.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("""
                CREATE TABLE "__TestDataScopeRecords" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NULL,
                    "OrganizationUnitId" uuid NULL,
                    "Name" character varying(128) NOT NULL,
                    "ExtraProperties" text NOT NULL,
                    "ConcurrencyStamp" character varying(40) NOT NULL,
                    "CreationTime" timestamp without time zone NOT NULL,
                    "CreatorId" uuid NULL,
                    "LastModificationTime" timestamp without time zone NULL,
                    "LastModifierId" uuid NULL
                );
                """, connection);
            command.ExecuteNonQuery();
        });
    }
}

public class FrameworkDataScopeTestDbContext : AbpAdminDbContext
{
    public DbSet<FrameworkDataScopeRecord> FrameworkDataScopeRecords { get; set; }

    public FrameworkDataScopeTestDbContext(DbContextOptions<AbpAdminDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<FrameworkDataScopeRecord>(entity =>
        {
            entity.ToTable("__TestDataScopeRecords");
            entity.ConfigureByConvention();
            entity.Property(record => record.Name).IsRequired().HasMaxLength(128);
        });
    }
}
