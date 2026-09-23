using AbpAdmin.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 文件相关自研实体（缩略图/分享链接）与 EasyAbp File 模块补充索引
/// （自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class FileThumbnailConfig : IEntityTypeConfiguration<FileThumbnail>
{
    public void Configure(EntityTypeBuilder<FileThumbnail> b)
    {
        // T3.1：文件 → 缩略图映射表。不建跨模块外键（FileId 指向 EasyAbp 模块表），只建索引。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "FileThumbnails", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.BlobName).HasMaxLength(128);
        b.Property(x => x.FailureReason).HasMaxLength(512);
        b.HasIndex(x => new { x.TenantId, x.FileId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.State });
        // 回填管理器在 Disable<IMultiTenant> 下跨租户查 Pending，复合索引的前导列用不上——
        // 补 State 单列索引支撑（round3 performance lens）
        b.HasIndex(x => x.State);
    }
}

public class FileShareLinkConfig : IEntityTypeConfiguration<FileShareLink>
{
    public void Configure(EntityTypeBuilder<FileShareLink> b)
    {
        // T4.6：文件分享链接
        b.ToTable(AbpAdminConsts.DbTablePrefix + "FileShareLinks", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Token).IsRequired().HasMaxLength(FileShareLinkConsts.MaxTokenLength);
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.FileId });
    }
}
