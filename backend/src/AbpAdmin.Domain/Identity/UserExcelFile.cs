using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导入/导出产生的 Excel 文件记录（重构报告问题 6：Identity 模块自有表，
/// 不再借用审计模块的 AuditLogExcelFile——语义混淆 + 审计清理策略可能误删用户导出文件）。
/// Id 即 BlobName，文件内容存 <see cref="UserExportBlobContainer"/>。
/// 下载时校验 CreatorId == 当前用户，跨用户不可下载。
/// </summary>
public class UserExcelFile : CreationAuditedEntity<Guid>, IMultiTenant
{
    public static int MaxFileNameLength { get; set; } = 256;

    public virtual Guid? TenantId { get; protected set; }

    public virtual string FileName { get; protected set; }

    protected UserExcelFile()
    {
    }

    public UserExcelFile(
        Guid id,
        string fileName,
        Guid? tenantId = null,
        Guid? creatorId = null)
        : base(id)
    {
        FileName = Check.NotNullOrWhiteSpace(fileName, nameof(fileName), MaxFileNameLength);
        TenantId = tenantId;
        CreatorId = creatorId;
    }
}
