using System;
using System.Threading.Tasks;
using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AbpAdmin.TokenGateway.Core;

/// <summary>
/// 预占催收（进程中断兜底）。ChatBilling 的请求内释放（Settle/Fail）覆盖不到
/// 「预占提交后、结算前进程崩溃/重启」的窗口——滞留 Open 的预占单会永久占用
/// TgWallets.Frozen，吃掉用户可用额度。本 worker 周期扫描创建时间早于
/// TokenGateway:ReservationStaleHours（默认 24h，正常对话生命周期以分钟计，余量极大）
/// 的 Open 单，走 Wallet.ReleaseAsync 全额解冻：条件 UPDATE 只翻转 Status==Open，
/// 与真实结算并发安全；语义与 FailAsync 一致——是解冻不是退款。
/// 只在宿主注册（见 AbpAdminHttpApiHostModule）；DbMigrator 是一次性短进程，不需要。
/// </summary>
public class ReservationJanitor : AsyncPeriodicBackgroundWorkerBase
{
    private readonly Wallet _wallet;
    private readonly IDbContextFactory<TokenGatewayDbContext> _dbf;
    private readonly TimeProvider _clock;
    private readonly TokenGatewayOptions _options;
    private readonly ILogger<ReservationJanitor> _log;

    /// <summary>单轮最多催收条数：防止极端积压时一轮跑太久；扫不完下轮继续。</summary>
    private const int BatchLimit = 200;

    public ReservationJanitor(
        AbpAsyncTimer timer,
        IServiceScopeFactory scopeFactory,
        Wallet wallet,
        IDbContextFactory<TokenGatewayDbContext> dbf,
        TimeProvider clock,
        IOptions<TokenGatewayOptions> options,
        ILogger<ReservationJanitor> log)
        : base(timer, scopeFactory)
    {
        // 下界 fail-fast：0/负数会把在途对话的预占单当残留解冻（资损/可用性面）
        if (options.Value.ReservationStaleHours < 1)
        {
            throw new Volo.Abp.AbpInitializationException(
                "TokenGateway:ReservationStaleHours 必须 ≥ 1（正常对话生命周期以分钟计，无需更激进的阈值）");
        }
        timer.Period = 30 * 60 * 1000; // 30 分钟一轮（AbpAsyncTimer.Period 单位毫秒）
        _wallet = wallet;
        _dbf = dbf;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var cutoff = _clock.GetUtcNow().AddHours(-_options.ReservationStaleHours);
        await using var db = await _dbf.CreateDbContextAsync();
        var stale = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Open && r.CreatedAt < cutoff)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.UserId, r.Amount })
            .Take(BatchLimit)
            .ToListAsync(workerContext.CancellationToken);

        foreach (var r in stale)
        {
            // 逐条隔离：单条失败不让本轮剩余条目全部放弃，避免毒行顶队堵死积压
            try
            {
                // ReleaseAsync 内部条件翻转保证幂等：与真实 Settle 并发时只有一方生效
                await _wallet.ReleaseAsync(r.UserId, r.Id);
                _log.LogWarning(
                    "催收滞留预占单：{ReservationId}（用户 {UserId}，{Amount} token，创建早于 {Cutoff:o}）——进程中断残留，已全额解冻",
                    r.Id, r.UserId, r.Amount, cutoff);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "催收滞留预占单失败：{ReservationId}（用户 {UserId}），下轮重试", r.Id, r.UserId);
            }
        }

        if (stale.Count >= BatchLimit)
        {
            _log.LogWarning("滞留预占单单轮达 {Limit} 上限（可能仍有剩余），下轮继续；如持续增长请检查宿主崩溃/上游健康度", BatchLimit);
        }
    }
}
