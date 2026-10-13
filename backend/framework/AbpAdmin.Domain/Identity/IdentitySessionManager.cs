using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

/// <summary>
/// T4.1：开源 Identity 有 <see cref="IdentitySession"/> 实体和仓储，没有 SessionManager / CleanupWorker。
/// <see cref="CreateAsync"/> 按 sessionId upsert（已存在则刷新 LastAccessed/IP）。
/// <see cref="TouchIfStaleAsync"/> 给请求中间件续期，避免 Cleanup 把仍在用的会话当僵尸删掉。
/// 撤销会清动态声明缓存。
/// </summary>
public class IdentitySessionManager : DomainService
{
    /// <summary>中间件续期最小间隔，避免每个 API 都写库。</summary>
    public static readonly TimeSpan TouchMinInterval = TimeSpan.FromMinutes(1);

    private readonly IIdentitySessionRepository _sessionRepository;
    private readonly IRepository<IdentitySession, Guid> _sessionQueryable;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IdentityDynamicClaimsPrincipalContributorCache _dynamicClaimsCache;

    public IdentitySessionManager(
        IIdentitySessionRepository sessionRepository,
        IRepository<IdentitySession, Guid> sessionQueryable,
        IAsyncQueryableExecuter asyncExecuter,
        IdentityDynamicClaimsPrincipalContributorCache dynamicClaimsCache)
    {
        _sessionRepository = sessionRepository;
        _sessionQueryable = sessionQueryable;
        _asyncExecuter = asyncExecuter;
        _dynamicClaimsCache = dynamicClaimsCache;
    }

    public virtual async Task<IdentitySession?> FindBySessionIdAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return await _sessionRepository.FindAsync(sessionId);
    }

    /// <summary>
    /// 全新登录的会话建立（新 session_id）。插入新行后执行防并发登录策略。
    /// 既有 sessionId 的重放（refresh token 等刷新类授权）必须走 <see cref="RenewAsync"/>——
    /// 本方法不得用于复活已删除的会话，否则吊销/互踢可被持有 refresh token 的客户端原地绕过。
    /// </summary>
    public virtual async Task<IdentitySession> CreateAsync(
        Guid userId,
        string sessionId,
        string device,
        string? deviceInfo,
        string? clientId,
        string? ipAddresses)
    {
        var existing = await FindBySessionIdAsync(sessionId);
        if (existing != null)
        {
            existing.UpdateLastAccessedTime(Clock.Now);
            if (!string.IsNullOrWhiteSpace(ipAddresses))
            {
                existing.SetIpAddresses(ipAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }

            await _sessionRepository.UpdateAsync(existing);
            return existing;
        }

        var session = new IdentitySession(
            GuidGenerator.Create(),
            sessionId,
            device,
            deviceInfo,
            userId,
            CurrentTenant.Id,
            clientId,
            ipAddresses,
            Clock.Now);

        await _sessionRepository.InsertAsync(session);

        // 对标 ABP Identity Pro 的 Prevent Concurrent Login：仅在新会话建立时执行，
        // 靠会话校验中间件（库中无行 → 401 session_revoked）使被踢会话下一请求即失效。
        await EnforceConcurrentLoginPolicyAsync(session);

        return session;
    }

    /// <summary>
    /// 刷新类授权（refresh token 等）对既有 session_id 的续期语义：
    /// 行存在且属于该用户 → 刷新 LastAccessed/IP 返回会话；行不存在（已被吊销/互踢）
    /// 或不属于该用户 → 返回 null，调用方不得重建。
    /// 有意不接收 device/deviceInfo/clientId：会话的设备轴在建立时定死，续期不得换组
    ///（否则 Mobile 会话可经 refresh 静默迁移进 Web 组参与不同互踢面）。
    /// 调用方（IdentitySessionOpenIddictServerHandler）拿到 null 不得重建行——否则被踢端
    /// 只要还握着 refresh token，下一次静默续期就能原地复活，吊销与三档互踢全部失效。
    /// 复活防护成立的前提是旧 sessionId 保留在重发的票里：后续 API 请求会被
    /// 会话校验中间件以 401 session_revoked 拦下，强制客户端重新走登录建新会话。
    /// </summary>
    public virtual async Task<IdentitySession?> RenewAsync(
        Guid userId,
        string sessionId,
        string? ipAddresses)
    {
        var existing = await FindBySessionIdAsync(sessionId);
        if (existing == null || existing.UserId != userId)
        {
            return null;
        }

        existing.UpdateLastAccessedTime(Clock.Now);
        if (!string.IsNullOrWhiteSpace(ipAddresses))
        {
            existing.SetIpAddresses(ipAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        await _sessionRepository.UpdateAsync(existing);
        return existing;
    }

    /// <summary>
    /// 按设置 <see cref="AbpAdminSettings.Account.PreventConcurrentLoginMode"/> 清理该用户的旧会话：
    /// LogoutFromSameTypeDevices 只踢同 Device 类型（与 ABP Pro 语义一致），
    /// LogoutFromAllDevices 踢该用户全部其它会话。被踢用户清一次动态声明缓存即可（同 userId）。
    /// </summary>
    protected virtual async Task EnforceConcurrentLoginPolicyAsync(IdentitySession session)
    {
        var settingProvider = LazyServiceProvider.LazyGetRequiredService<ISettingProvider>();
        var modeText = await settingProvider.GetOrNullAsync(AbpAdminSettings.Account.PreventConcurrentLoginMode)
                       ?? PreventConcurrentLoginMode.Disabled.ToString();
        if (!Enum.TryParse<PreventConcurrentLoginMode>(modeText, ignoreCase: true, out var mode))
        {
            // 脏值（手改库/迁移/绕过 UI 写设置）静默等于关闭策略且零痕迹不可接受：
            // 记警告让运维可发现，行为上回退 Disabled（默认档，文档化默认值）
            Logger.LogWarning(
                "Invalid {SettingName} value '{Value}'; falling back to Disabled.",
                AbpAdminSettings.Account.PreventConcurrentLoginMode, modeText);
            return;
        }

        if (mode == PreventConcurrentLoginMode.Disabled)
        {
            return;
        }

        var queryable = await _sessionQueryable.GetQueryableAsync();
        var staleSessions = queryable.Where(x =>
            x.UserId == session.UserId &&
            x.Id != session.Id &&
            // 只踢比自己早建立的会话：并发登录两笔互看不见对方的未提交行时，
            // 无此守卫会 A 删 B、B 删 A 双双 401；有此守卫则稳定 last-wins
            x.SignedIn < session.SignedIn);
        if (mode == PreventConcurrentLoginMode.LogoutFromSameTypeDevices)
        {
            staleSessions = staleSessions.Where(x => x.Device == session.Device);
        }

        var stale = await _asyncExecuter.ToListAsync(staleSessions);
        await RevokeManyCoreAsync(stale);
    }

    /// <summary>
    /// 距上次访问超过 <see cref="TouchMinInterval"/> 才写库，避免每个 API 都 UPDATE。
    /// 写库失败会随当前 UoW 失败；中间件不单独吞掉异常，以免会话状态撒谎。
    /// </summary>
    public virtual async Task TouchIfStaleAsync(IdentitySession session)
    {
        var now = Clock.Now;
        if (session.LastAccessed.HasValue && now - session.LastAccessed.Value < TouchMinInterval)
        {
            return;
        }

        session.UpdateLastAccessedTime(now);
        await _sessionRepository.UpdateAsync(session);
    }

    public virtual async Task RevokeAsync(Guid id)
    {
        var session = await _sessionRepository.FindAsync(id);
        if (session == null)
        {
            return;
        }

        await RevokeManyCoreAsync([session]);
    }

    /// <summary>
    /// 「删会话行 → 清动态声明缓存」不变式的单一 owner：所有删除路径都从这里走，
    /// 租户口径统一取自会话行自身（不再混用 CurrentTenant.Id / sessions[0].TenantId），
    /// 同一 (UserId, TenantId) 只清一次缓存。新增删除路径必须经此方法，防止漏清缓存
    /// 造成被踢用户拿旧动态声明继续通过鉴权的 split-brain。
    /// </summary>
    protected virtual async Task RevokeManyCoreAsync(IReadOnlyCollection<IdentitySession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        await _sessionRepository.DeleteManyAsync(sessions);
        foreach (var key in sessions.Select(x => (x.UserId, x.TenantId)).Distinct())
        {
            await _dynamicClaimsCache.ClearAsync(key.UserId, key.TenantId);
        }
    }

    /// <summary>
    /// 吊销指定用户的全部会话（强制全端下线）。与会话页的单会话吊销对应，
    /// 供管理端「按用户吊销全部」使用。
    /// </summary>
    public virtual async Task RevokeAllForUserAsync(Guid userId)
    {
        var queryable = await _sessionQueryable.GetQueryableAsync();
        var sessions = await _asyncExecuter.ToListAsync(queryable.Where(x => x.UserId == userId));
        await RevokeManyCoreAsync(sessions);
    }

    [UnitOfWork]
    public virtual async Task CleanupInactiveAsync(TimeSpan inactiveTimeSpan)
    {
        if (inactiveTimeSpan <= TimeSpan.Zero)
        {
            return;
        }

        var threshold = Clock.Now.Subtract(inactiveTimeSpan);

        // 按 Id 分批（每批 500）处理，替代一次性把所有过期会话物化进内存；
        // 活跃系统下过期会话可能数万条，全量加载会放大单轮清理的内存与时间。
        const int batchSize = 500;
        while (true)
        {
            var queryable = await _sessionQueryable.GetQueryableAsync();
            var batch = await _asyncExecuter.ToListAsync(
                queryable
                    .Where(x => (x.LastAccessed ?? x.SignedIn) < threshold)
                    .OrderBy(x => x.Id)
                    .Take(batchSize));

            if (batch.Count == 0)
            {
                break;
            }

            // 批量删除；同一用户多设备过期只清一次动态声明缓存（按 userId 去重）
            await RevokeManyCoreAsync(batch);

            // 刷新删除到数据库，否则下一批查询仍会命中已删除的行（同 UoW 内待提交的删除对查询不可见）
            var currentUnitOfWork = LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current;
            if (currentUnitOfWork == null)
            {
                // 防御兜底：没有环境 UoW 时删除永远刷不进库（[UnitOfWork] 特性被类内 this 调用
                // 绕过拦截时会出现），下一批查询命中同一批行 → 无限循环。记警告后退出本轮，
                // 由调用方补 UoW 重试，宁可少删不可死循环（见代码审查 round1）。
                Logger.LogWarning(
                    "CleanupInactiveAsync is running without an ambient unit of work; " +
                    "deletions cannot be flushed and the batch loop is aborted to avoid an infinite loop.");
                break;
            }

            await currentUnitOfWork.SaveChangesAsync();
        }
    }
}
