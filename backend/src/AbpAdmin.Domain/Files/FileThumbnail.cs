using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Files;

/// <summary>
/// 文件 → 缩略图映射表（T3.1，表 AppFileThumbnails）。
/// EasyAbp 的 File 实体在模块内定义不能加字段，缩略图状态机必须可查询
/// （重试、回填、统计），所以落一张自建映射表而不是 File.ExtraProperties JSON 列。
/// </summary>
public class FileThumbnail : AuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>EasyAbp FileManagement 的 File.Id。不建跨模块外键，只建唯一索引。</summary>
    public virtual Guid FileId { get; protected set; }

    /// <summary>
    /// 缩略图在 file-thumbnails 容器里的 blob 名。
    /// 后缀是实际图片格式（.jpg/.png/.webp）——ABP SkiaSharp contributor 不转码，
    /// 输出保持源格式（已核实 TryResizeAsync/TryCompressAsync 用 codec.EncodedFormat 重编码）。
    /// </summary>
    public virtual string? BlobName { get; protected set; }

    public virtual int Width { get; protected set; }

    public virtual int Height { get; protected set; }

    public virtual FileThumbnailStateEnum State { get; protected set; }

    public virtual string? FailureReason { get; protected set; }

    protected FileThumbnail()
    {
    }

    public FileThumbnail(Guid id, Guid? tenantId, Guid fileId)
        : base(id)
    {
        TenantId = tenantId;
        FileId = fileId;
        State = FileThumbnailStateEnum.Pending;
    }

    public virtual void MarkDone(string blobName, int width, int height)
    {
        BlobName = blobName;
        Width = width;
        Height = height;
        State = FileThumbnailStateEnum.Done;
        FailureReason = null;
    }

    public virtual void MarkUnsupported(string? reason)
    {
        State = FileThumbnailStateEnum.Unsupported;
        FailureReason = reason;
    }

    public virtual void MarkFailed(string? reason)
    {
        State = FileThumbnailStateEnum.Failed;
        FailureReason = reason;
    }

    public virtual void ResetToPending()
    {
        State = FileThumbnailStateEnum.Pending;
        FailureReason = null;
    }
}
