using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// EasyAbp FileManagement 模块实体的补充索引（Wave1 新增，自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class FileManagementFileConfig : IEntityTypeConfiguration<EasyAbp.FileManagement.Files.File>
{
    public void Configure(EntityTypeBuilder<EasyAbp.FileManagement.Files.File> b)
    {
        // 配额 SUM（FileStorageQuotaChecker 按 TenantId + FileType 聚合 RegularFile 的 ByteSize）
        // 每次上传都会触发，十万级文件量下无索引会全表扫描。模块自带的映射没有这组索引，这里补上。
        // 注意：仅改模型不会改已有库，需由主控生成并应用一次迁移（dotnet-ef migrations add）。
        b.HasIndex(x => new { x.TenantId, x.FileType });
    }
}
