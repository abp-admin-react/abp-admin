using AbpAdmin.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>用户导入/导出文件记录的实体映射。</summary>
public class UserExcelFileConfig : IEntityTypeConfiguration<UserExcelFile>
{
    public void Configure(EntityTypeBuilder<UserExcelFile> b)
    {
        // 按创建人下载的查询路径建 (TenantId, CreatorId) 索引。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "UserExcelFiles", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.FileName).IsRequired().HasMaxLength(UserExcelFile.MaxFileNameLength);
        b.HasIndex(x => new { x.TenantId, x.CreatorId });
    }
}
