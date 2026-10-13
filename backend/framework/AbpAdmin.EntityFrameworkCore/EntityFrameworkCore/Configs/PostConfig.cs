using AbpAdmin.Identity;
using AbpAdmin.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 岗位与用户岗位关联、用户导入/导出文件记录实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class PostConfig : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> b)
    {
        // 岗位：租户内名称/编码唯一；无跨表外键（UserId 逻辑引用 IdentityUsers）。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "Posts", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(PostConsts.MaxNameLength);
        b.Property(x => x.Code).IsRequired().HasMaxLength(PostConsts.MaxCodeLength);
        b.Property(x => x.Remark).HasMaxLength(PostConsts.MaxRemarkLength);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class IdentityUserPostConfig : IEntityTypeConfiguration<IdentityUserPost>
{
    public void Configure(EntityTypeBuilder<IdentityUserPost> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "IdentityUserPosts", AbpAdminConsts.DbSchema);
        b.HasKey(x => new { x.UserId, x.PostId });
        b.ConfigureByConvention();
        b.HasIndex(x => new { x.TenantId, x.PostId });
    }
}

public class UserExcelFileConfig : IEntityTypeConfiguration<UserExcelFile>
{
    public void Configure(EntityTypeBuilder<UserExcelFile> b)
    {
        // 用户导入/导出文件记录（Identity 模块自有表，替代原先借用的 AbpAuditLogExcelFiles）。
        // 按创建人下载的查询路径建 (TenantId, CreatorId) 索引；存量数据不迁移（见重构报告问题 6）。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "UserExcelFiles", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.FileName).IsRequired().HasMaxLength(UserExcelFile.MaxFileNameLength);
        b.HasIndex(x => new { x.TenantId, x.CreatorId });
    }
}
