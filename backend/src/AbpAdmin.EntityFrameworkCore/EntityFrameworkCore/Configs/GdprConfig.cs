using AbpAdmin.Gdpr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// GDPR 请求与信息实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class GdprRequestConfig : IEntityTypeConfiguration<GdprRequest>
{
    public void Configure(EntityTypeBuilder<GdprRequest> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "GdprRequests", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreationTime });
    }
}

public class GdprInfoConfig : IEntityTypeConfiguration<GdprInfo>
{
    public void Configure(EntityTypeBuilder<GdprInfo> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "GdprInfos", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Provider).IsRequired().HasMaxLength(128);
        b.Property(x => x.Data).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.RequestId });
    }
}
