using AbpAdmin.Biz.Template.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 业务模块自带 DbContext：与宿主 AbpAdminDbContext 同库（Default 连接串）但互不相干。
/// 建表走本模块 Sql/ 下的脚本和 __BizTemplateMigrations，不产生框架仓库改动。
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
