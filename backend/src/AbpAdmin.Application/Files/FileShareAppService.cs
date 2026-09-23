using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using EasyAbp.FileManagement.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using File = EasyAbp.FileManagement.Files.File;

namespace AbpAdmin.Files;

/// <summary>
/// T4.6：文件分享。创建/查询/撤销都要求当前用户持有该文件的「下载」权限
/// （复用 EasyAbp 的 GetDownloadInfo 操作授权 + 每用户限流）——分享本质是把下载权外包给匿名 token，
/// 只用「读」权限校验会让仅有查看权限的用户为任意文件开出公开下载链接。
/// 匿名下载只认公开、未过期、未用尽次数的 token；先预检文件与 BlobName、再占用次数（乐观并发）、
/// 后按流读 blob：并发超次失败，大文件不整包进内存；取流失败回滚次数（服务端故障不误扣）。
/// </summary>
[Authorize]
public class FileShareAppService : AbpAdminAppService, IFileShareAppService
{
    private readonly IRepository<FileShareLink, Guid> _shareRepository;
    private readonly IFileAppService _fileAppService;
    private readonly IFileRepository _fileRepository;
    private readonly IBlobContainer<AdminFileBlobContainer> _blobContainer;

    public FileShareAppService(
        IRepository<FileShareLink, Guid> shareRepository,
        IFileAppService fileAppService,
        IFileRepository fileRepository,
        IBlobContainer<AdminFileBlobContainer> blobContainer)
    {
        _shareRepository = shareRepository;
        _fileAppService = fileAppService;
        _fileRepository = fileRepository;
        _blobContainer = blobContainer;
    }

    public virtual async Task<FileShareLinkDto> CreateAsync(CreateFileShareLinkInput input)
    {
        // 分享 = 把下载权外包给匿名 token，必须按「下载」权限校验（File.GetDownloadInfo），
        // 不能复用「读」权限（File.Get）。GetDownloadInfoAsync 内部走 FileOperation 授权 +
        // 租户级每用户限流（GetDownloadInfoTimesLimitEachUserPerMinute），无权下载直接 403/404。
        await _fileAppService.GetDownloadInfoAsync(input.FileId);

        // 本批只做匿名公开链接；IsPublic=false 没有已认证下载路径，创建出来会永远 业务失败。
        if (!input.IsPublic)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkInvalid);
        }

        var now = Clock.Now;
        var maxExpire = now.AddDays(FileShareLinkConsts.MaxExpireDays);
        var expire = input.ExpireTime ?? now.AddDays(7);
        if (expire <= now || expire > maxExpire)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkInvalid);
        }

        if (input.MaxDownloads.HasValue && input.MaxDownloads.Value <= 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkInvalid);
        }

        var entity = new FileShareLink(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.FileId,
            CreateToken(),
            expire,
            isPublic: true,
            input.MaxDownloads);

        await _shareRepository.InsertAsync(entity);
        return Map(entity);
    }

    public virtual async Task<ListResultDto<FileShareLinkDto>> GetListAsync(Guid fileId)
    {
        // 分享列表会下发 token——token 本身就是匿名下载凭证，与创建同权限（下载），
        // 不能比 CreateAsync 更宽松。
        await _fileAppService.GetDownloadInfoAsync(fileId);
        var list = await _shareRepository.GetListAsync(x => x.FileId == fileId);
        return new ListResultDto<FileShareLinkDto>(list.Select(Map).ToList());
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var share = await _shareRepository.GetAsync(id);
        // 撤销分享同样是管理「下载能力」，与创建同权限（下载），避免只读用户撤掉别人的公开链接。
        await _fileAppService.GetDownloadInfoAsync(share.FileId);
        await _shareRepository.DeleteAsync(share);
    }

    /// <summary>
    /// 匿名下载。失败一律 <see cref="AbpAdminDomainErrorCodes.Files.ShareLinkExpired"/>，避免用错误码枚举 token。
    /// 顺序：预检文件/BlobName（不扣次）→ 占用次数并 Update（乐观并发）→ 按流读 blob，
    /// 避免整文件进内存，也避免并发把 MaxDownloads 打穿；blob 读取失败回滚次数，服务端故障不误扣。
    /// </summary>
    [AllowAnonymous]
    [RemoteService(false)]
    // 匿名端点防刷：token 是 256-bit 随机无法枚举，防的是脚本高频打有效 token 消耗次数/带宽；
    // 策略按客户端 IP 限流（FileShareDownload），[RateLimitingParameter] 只为满足参数解析
    // （ClientIp 分区不使用该参数，不标会产生误导性的「所有用户共用一个计数」告警日志）。
    [OperationRateLimiting(OperationRateLimitingPolicyNames.FileShareDownload)]
    public virtual async Task<IRemoteStreamContent> DownloadByTokenAsync(
        [RateLimitingParameter] string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > FileShareLinkConsts.MaxTokenLength)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
        }

        FileShareLink? share;
        using (DataFilter.Disable<IMultiTenant>())
        {
            share = await _shareRepository.FindAsync(x => x.Token == token);
        }

        if (share == null || !share.IsPublic)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
        }

        using (CurrentTenant.Change(share.TenantId))
        {
            // 预检：文件已删除或 BlobName 异常属于服务端状态问题，不应消耗下载次数
            var file = await _fileRepository.FindAsync(share.FileId);
            if (file is null || string.IsNullOrWhiteSpace(file.BlobName))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
            }

            if (!share.TryConsumeDownload(Clock.Now))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
            }

            try
            {
                await _shareRepository.UpdateAsync(share, autoSave: true);
            }
            catch (AbpDbConcurrencyException)
            {
                // 并发第二笔：次数已被别人占满或行已变。
                throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
            }

            try
            {
                var stream = await _blobContainer.GetAsync(file.BlobName);
                return new RemoteStreamContent(
                    stream,
                    fileName: file.FileName,
                    contentType: string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Share link {ShareId} blob missing for file {FileId}", share.Id, share.FileId);
                // 次数已在上面落库，取流失败必须回滚：否则 MaxDownloads=1 的链接被一次服务端故障作废
                await TryRollbackDownloadAsync(share);
                throw new BusinessException(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
            }
        }
    }

    /// <summary>
    /// blob 取流失败等服务端故障时回滚误占的下载额度。
    /// 回滚写入再遇乐观并发冲突则放弃（说明行已被并发修改，计数归属存疑，保守不回退）并记 Warning。
    /// </summary>
    private async Task TryRollbackDownloadAsync(FileShareLink share)
    {
        share.RollbackDownload();
        try
        {
            await _shareRepository.UpdateAsync(share, autoSave: true);
        }
        catch (AbpDbConcurrencyException ex)
        {
            Logger.LogWarning(ex, "Share {ShareId} download count rollback skipped due to concurrent update", share.Id);
        }
    }

    // 手写映射（不用 ObjectMapper）：DownloadUrl 是拼接值，且 DTO 与聚合根字段一一对应；
    // FileShareLinkDto 新增字段时必须同步这里，避免静默漏拷。
    private FileShareLinkDto Map(FileShareLink entity)
    {
        return new FileShareLinkDto
        {
            Id = entity.Id,
            FileId = entity.FileId,
            Token = entity.Token,
            ExpireTime = entity.ExpireTime,
            IsPublic = entity.IsPublic,
            DownloadCount = entity.DownloadCount,
            MaxDownloads = entity.MaxDownloads,
            DownloadUrl = $"/api/app/file-share/by-token/{entity.Token}"
        };
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
