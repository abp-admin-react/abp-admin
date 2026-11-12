using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导出的 Excel 文件记录。Id 即 BlobName，内容在 <see cref="UserExportBlobContainer"/>。
/// 下载校验 CreatorId，跨用户不可下载。
/// </summary>
public class UserExcelFile : CreationAuditedEntity<Guid>, IMultiTenant
{
    public static int MaxFileNameLength { get; set; } = 256;

    public virtual Guid? TenantId { get; protected set; }

    public virtual string FileName { get; protected set; } = default!;

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
