using AbpAdmin.Biz.Template.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 业务模块自带 DbContext：与宿主 AbpAdminDbContext 同库（Default 连接串）但互不相干——
/// 各自迁移程序集 + 各自 History 表（__BizTemplateMigrations），业务建表不产生框架仓库改动。
/// 迁移程序集按 Database:Provider 择一，配置见 BizTemplateModule。
/// </summary>
[ConnectionStringName(BizTemplateConsts.ConnectionStringName)]
public class BizTemplateDbContext : AbpDbContext<BizTemplateDbContext>
{
    public DbSet<BizProject> BizProjects { get; set; }

    public BizTemplateDbContext(DbContextOptions<BizTemplateDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new BizProjectConfig());
    }
}
