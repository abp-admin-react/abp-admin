using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Files;

/// <summary>
/// T4.6：文件分享链接。匿名端点凭 token 下载。
/// 有效条件是 token 命中 + <see cref="IsPublic"/> + <see cref="IsUsable"/>（未过期且未用尽次数）。
/// 次数消耗必须走 <see cref="TryConsumeDownload"/>：先判定再自增，配合乐观并发避免并发超次。
/// </summary>
public class FileShareLink : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid FileId { get; protected set; }

    public virtual string Token { get; protected set; } = default!;

    public virtual DateTime ExpireTime { get; protected set; }

    public virtual bool IsPublic { get; protected set; }

    public virtual int DownloadCount { get; protected set; }

    public virtual int? MaxDownloads { get; protected set; }

    protected FileShareLink()
    {
    }

    public FileShareLink(
        Guid id,
        Guid? tenantId,
        Guid fileId,
        string token,
        DateTime expireTime,
        bool isPublic,
        int? maxDownloads)
        : base(id)
    {
        TenantId = tenantId;
        FileId = fileId;
        Token = Check.NotNullOrWhiteSpace(token, nameof(token), FileShareLinkConsts.MaxTokenLength);
        ExpireTime = expireTime;
        IsPublic = isPublic;
        // 0 或负数会使 IsUsable 恒为 false，等于创建即失效；拒绝以免状态撒谎。
        if (maxDownloads.HasValue && maxDownloads.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDownloads), "MaxDownloads must be null or a positive integer.");
        }

        MaxDownloads = maxDownloads;
    }

    /// <summary>
    /// 过期或次数已用尽则不可用。MaxDownloads 为 null 表示不限次。
    /// </summary>
    public virtual bool IsUsable(DateTime now)
    {
        if (now >= ExpireTime)
        {
            return false;
        }

        if (MaxDownloads.HasValue && DownloadCount >= MaxDownloads.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 原子语义上的「占用一次下载额度」：不可用则返回 false 且不改计数。
    /// 调用方必须随后 <c>UpdateAsync</c>；并发时靠聚合根 ConcurrencyStamp 让第二笔失败。
    /// </summary>
    public virtual bool TryConsumeDownload(DateTime now)
    {
        if (!IsUsable(now))
        {
            return false;
        }

        DownloadCount++;
        return true;
    }

    /// <summary>
    /// 回滚一次误占的下载额度：先扣次数后取流的流程里，blob 读取失败等
    /// 服务端故障不应消耗用户的下载次数（MaxDownloads=1 的链接会被一次故障直接作废）。
    /// 仅回退计数不做可用性判定；回滚写入若再遇并发冲突由调用方放弃（见 FileShareAppService.TryRollbackDownloadAsync）。
    /// </summary>
    public virtual void RollbackDownload()
    {
        if (DownloadCount > 0)
        {
            DownloadCount--;
        }
    }
}
