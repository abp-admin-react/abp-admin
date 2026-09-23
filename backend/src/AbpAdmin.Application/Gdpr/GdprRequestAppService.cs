using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Volo.Abp.Caching;
using Volo.Abp.Content;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 个人数据请求应用服务。
/// 对齐 Pro 的授权模型：不定义可授予的 GDPR 权限，类级只要求已认证，
/// 方法内部显式校验请求归属当前用户；唯一下载端点匿名，靠 token 鉴权。
/// </summary>
[Authorize]
public class GdprRequestAppService : AbpAdminAppService, IGdprRequestAppService
{
    private readonly IRepository<GdprRequest, Guid> _gdprRequestRepository;
    private readonly IRepository<GdprInfo, Guid> _gdprInfoRepository;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IDistributedCache<GdprDownloadTokenCacheItem, string> _downloadTokenCache;
    private readonly IdentityUserManager _userManager;
    private readonly AbpAdminGdprOptions _gdprOptions;
    private readonly IAbpDistributedLock _distributedLock;

    public GdprRequestAppService(
        IRepository<GdprRequest, Guid> gdprRequestRepository,
        IRepository<GdprInfo, Guid> gdprInfoRepository,
        IDistributedEventBus distributedEventBus,
        IDistributedCache<GdprDownloadTokenCacheItem, string> downloadTokenCache,
        IdentityUserManager userManager,
        IOptions<AbpAdminGdprOptions> gdprOptions,
        IAbpDistributedLock distributedLock)
    {
        _gdprRequestRepository = gdprRequestRepository;
        _gdprInfoRepository = gdprInfoRepository;
        _distributedEventBus = distributedEventBus;
        _downloadTokenCache = downloadTokenCache;
        _userManager = userManager;
        _gdprOptions = gdprOptions.Value;
        _distributedLock = distributedLock;
    }

    public virtual async Task<GdprRequestDto> CreateAsync()
    {
        // 服务端必须再校验一次间隔，不要只靠前端预检
        if (!await IsNewRequestAllowedAsync())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.RequestIntervalNotElapsed)
                .WithData("RequestTimeInterval", _gdprOptions.RequestTimeInterval);
        }

        var request = new GdprRequest(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            CurrentUser.GetId(),
            Clock.Now,
            _gdprOptions.MinutesForDataPreparation);

        await _gdprRequestRepository.InsertAsync(request, autoSave: true);

        // 发布请求事件，各模块订阅后用相同 RequestId 回发 Prepared 事件
        await _distributedEventBus.PublishAsync(new GdprUserDataRequestedEto
        {
            TenantId = CurrentTenant.Id,
            RequestId = request.Id,
            UserId = CurrentUser.GetId()
        });

        return new GdprRequestDto
        {
            Id = request.Id,
            CreationTime = request.CreationTime,
            ReadyTime = request.ReadyTime
        };
    }

    public virtual async Task<bool> IsNewRequestAllowedAsync()
    {
        var queryable = await _gdprRequestRepository.GetQueryableAsync();
        var lastRequest = queryable
            .Where(x => x.UserId == CurrentUser.GetId())
            .OrderByDescending(x => x.CreationTime)
            .FirstOrDefault();

        if (lastRequest == null)
        {
            return true;
        }

        return lastRequest.CreationTime.Add(_gdprOptions.RequestTimeInterval) <= Clock.Now;
    }

    public virtual async Task<PagedResultDto<GdprRequestDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var queryable = await _gdprRequestRepository.GetQueryableAsync();
        var query = queryable.Where(x => x.UserId == CurrentUser.GetId());

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.CreationTime)
                 .Skip(input.SkipCount)
                 .Take(input.MaxResultCount));

        return new PagedResultDto<GdprRequestDto>(
            totalCount,
            items.Select(x => new GdprRequestDto
            {
                Id = x.Id,
                CreationTime = x.CreationTime,
                ReadyTime = x.ReadyTime
            }).ToList());
    }

    public virtual async Task<string> GetDownloadTokenAsync(Guid requestId)
    {
        var request = await _gdprRequestRepository.GetAsync(requestId);

        // 不匹配抛 EntityNotFoundException 而非 403，避免泄露请求是否存在
        if (request.UserId != CurrentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(GdprRequest), requestId);
        }

        // 下载端点匿名、token 即凭据：必须用加密安全随机（GuidGenerator 是顺序 Guid，
        // 时间戳+进程计数器可推算，等于下载端点形同虚设）。有效期走 AbpAdminGdprOptions.DownloadTokenExpiration。
        var token = CreateDownloadToken();

        await _downloadTokenCache.SetAsync(
            token,
            new GdprDownloadTokenCacheItem(requestId, CurrentUser.GetId()),
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _gdprOptions.DownloadTokenExpiration
            });

        return token;
    }

    /// <summary>256 bit 加密安全随机十六进制 token（与 GuidGenerator 解耦）。</summary>
    protected static string CreateDownloadToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    // 下载只走显式 GdprDownloadController（GET），禁止动态 API 再暴露一个 POST 端点
    // [DisableAuditing]：token 是一次性安全凭据，不能被序列化进 AbpAuditLogAction.Parameters
    // （审计会被 AuditLoggingGdprDataProvider 打包进"个人数据导出物"）；审计走方法内的脱敏日志。
    // [OperationRateLimiting]：匿名端点防刷（与 FileShareDownload 同型）——token 是 256-bit 随机
    // 无法枚举，防的是脚本高频打下载端点消耗 token/带宽与 ZIP 打包开销。策略按客户端 IP 分区
    // （GdprDownload），[RateLimitingParameter] 只为满足参数解析（ClientIp 分区不使用该参数，
    // 不标会产生误导性的「所有用户共用一个计数」告警日志）。限流异常（429）由 GdprDownloadController
    // 放行给 ABP 异常处理返回 429 + Retry-After。
    [RemoteService(false)]
    [AllowAnonymous]
    [DisableAuditing]
    [OperationRateLimiting(OperationRateLimitingPolicyNames.GdprDownload)]
    public virtual async Task<IRemoteStreamContent> DownloadAsync(Guid requestId, [RateLimitingParameter] string token)
    {
        // 锁键与缓存键长度先钳制：token 是调用方可控的查询串，直接拼进锁键前先做形状校验
        //（正常 token 是 64 个十六进制字符；短于 64 直接按无效拒绝，防畸形短串在
        // 后续 token[..8] 切片处抛 ArgumentOutOfRangeException 干扰排障）
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxDownloadTokenLength || token.Length < 64)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.InvalidDownloadToken);
        }

        // 有效性预检（不消费）：token 不存在或 RequestId 不匹配直接拒绝。
        // 预检与最终消费之间有并发窗口没关系——窗口内被并发消费掉的请求会在
        // ConsumeDownloadTokenAsync 的锁定「读-验-删」处拿到 InvalidDownloadToken。
        var peeked = await _downloadTokenCache.GetAsync(token);
        if (peeked == null || peeked.RequestId != requestId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.InvalidDownloadToken);
        }

        // 数据未就绪时不得消耗 token（Pro 对齐的体验修正：早先「先消费后校验就绪」
        // 会让用户在 ReadyTime 前点一次下载就烧掉一次性 token，必须重新取）。
        // 请求不存在（已清理）与未就绪同码 DataNotReady——对持有效 token 的用户二者
        // 语义一致（都还不能下载）；无效 token 已在 peek 处被拒，此处无状态可泄露。
        // ReadyTime 不随错误下发：匿名端点不暴露准备时刻等可枚举状态。
        var request = await _gdprRequestRepository.FindAsync(requestId);
        if (request == null || request.ReadyTime > Clock.Now)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.DataNotReady);
        }

        var queryable = await _gdprInfoRepository.GetQueryableAsync();
        var infos = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.RequestId == requestId));

        // 数据就绪覆盖度校验：ReadyTime 只是时间启发（CreationTime + 固定准备时长），
        // 不代表各模块真的回发了 GdprInfo（本地总线内联时几乎同时到，切真分布式总线后
        // 事件可能丢失/延迟）。一条 provider 条目都没有时打包会产出空 ZIP——静默缺条目
        // 比报错危害更大（用户以为导出物完整）。复用 DataNotReady 错误码：与「时间未到」
        // 同码，不向调用方泄露「请求存在但无数据」这类可枚举状态差异。
        if (infos.Count == 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.DataNotReady);
        }

        // 全部校验通过后才一次性消费 token（分布式锁内「读 + 验 + 删」）
        var cacheItem = await ConsumeDownloadTokenAsync(requestId, token);

        // 脱敏审计（不带 token）：谁下载了谁的请求
        Logger.LogInformation("GDPR 个人数据下载：userId={UserId}, requestId={RequestId}", cacheItem.UserId, requestId);

        // 每份 payload 是 ZIP 里的一个 JSON 条目，文件名 {Provider}.json
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var info in infos)
            {
                var entry = archive.CreateEntry($"{info.Provider}.json");
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(info.Data);
            }
        }

        stream.Seek(0, SeekOrigin.Begin);
        return new RemoteStreamContent(
            stream,
            $"personal-data-{requestId:N}.zip",
            "application/zip",
            stream.Length);
    }

    /// <summary>下载 token 形状上限（CreateDownloadToken 生成 64 个十六进制字符，留余量）。</summary>
    protected const int MaxDownloadTokenLength = 128;

    /// <summary>
    /// 单次消费下载 token：分布式锁内「读 + 验 + 删」。
    /// 说明：ABP 10.6 的 IDistributedCache.RemoveAsync 只返回 Task、不返回被删条目
    /// （没有 Redis GETDEL 语义），原先「先 Get 再 Remove」存在 TOCTOU 窗口——并发重放
    /// 可双双通过校验各下载一次。这里借 IAbpDistributedLock 把三步收进临界区
    /// （与 DistributedCacheOperationRateLimitingStore 的计数递增同一锁模式）：
    /// 并发重放只有一个请求能在锁内命中缓存条目，删除释放后后来者必未命中。
    /// 仅在就绪校验全部通过后调用（顺序见 DownloadAsync）；未命中抛 InvalidDownloadToken。
    /// </summary>
    protected virtual async Task<GdprDownloadTokenCacheItem> ConsumeDownloadTokenAsync(Guid requestId, string token)
    {
        await using var handle = await _distributedLock.TryAcquireAsync(
            $"gdpr:download-token:{token}",
            TimeSpan.FromSeconds(10));

        if (handle == null)
        {
            // 锁竞争超时是基础设施故障：抛 500（AbpException），不要转成 4xx 泄露 token 状态
            throw new AbpException($"获取 GDPR 下载 token 消费锁超时：{token.AsSpan(0, 8).ToString()}…");
        }

        var cacheItem = await _downloadTokenCache.GetAsync(token);
        if (cacheItem == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.InvalidDownloadToken);
        }

        // RequestId 不匹配时不消耗 token，直接返回错误
        if (cacheItem.RequestId != requestId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.InvalidDownloadToken);
        }

        await _downloadTokenCache.RemoveAsync(token);

        return cacheItem;
    }

    // 删除账户走显式 GdprAccountController（DELETE），禁止动态 API 暴露
    // 因为 ABP 动态 API 对 DELETE 请求的 body 绑定有问题
    [RemoteService(false)]
    public virtual async Task DeleteCurrentUserAccountAsync(DeleteAccountInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());

        if (!await _userManager.CheckPasswordAsync(user, input.Password))
        {
            // UserFriendlyException：BusinessException 在该动态 API 路由上被观测到绕过
            // 异常转译裸抛 500（R3-GD 测试），UserFriendlyException 全管道可靠映射 403 + 本地化消息。
            throw new UserFriendlyException(L["AbpAdmin:Gdpr:IncorrectPassword"], code: AbpAdminDomainErrorCodes.Gdpr.IncorrectPassword);
        }

        // GDPR 删除是"必须可追责"的操作：显式留一条审计日志（who/when），
        // 细粒度变更依赖 ABP 实体审计记录的是匿名化后的值。
        Logger.LogWarning("GDPR 账户删除请求：userId={UserId}, userName={UserName}", user.Id, user.UserName);

        // 先发布删除事件、后清请求记录：IdentityUserDeletionHandler（本地总线内联执行）
        // 删完用户后在末尾清理该用户的 GdprRequest/GdprInfo——保证"账户在则请求在"：
        // 删除半途失败（重试耗尽）时请求记录不先行消失。若将来换真分布式总线，
        // 事件与本地写入不再同事务，此顺序同样保证事件先于清理落库。
        await _distributedEventBus.PublishAsync(new GdprUserDataDeletionRequestedEto
        {
            TenantId = CurrentTenant.Id,
            UserId = CurrentUser.GetId()
        });
    }
}
