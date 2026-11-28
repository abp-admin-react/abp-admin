using System;

namespace AbpAdmin.Collection.Chain;

public enum SponsorHeadAction
{
    /// <summary>无动作：队头为空 / 刚出现 / 有任务跟踪 / 最近无 pending 证据 / 冷却中。</summary>
    None = 0,

    /// <summary>发一笔 Gas Sponsor 自转（to=自己, value=0, nonce=latest）清理无主卡死队头。</summary>
    Replace = 1,

    /// <summary>连续抬价用尽，进入 1 小时冷却等待外因恢复。</summary>
    GiveUp = 2,
}

/// <summary>
/// Gas Sponsor 无主队头看门狗状态机（无 IO 状态转移判定器）。
/// 通过 <see cref="FromSnapshot"/> / <see cref="ToSnapshot"/> 在 <c>ColNetwork.SponsorWatch*</c> 持久化跨 Pod 共享时间线；
/// 当 Sponsor 存在无主卡池交易（pending &gt; latest 且无 Fueling 任务持有 latest）持续超过 5 分钟时，触发 0-BNB 自转同 nonce 替换。
/// </summary>
public sealed class SponsorHeadWatch
{
    private readonly object _sync = new();

    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PendingEvidenceWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ReplaceRetryAfter = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GiveUpRetryAfter = TimeSpan.FromHours(1);
    public const int MaxReplaceAttempts = BscGasPricePolicy.MaxBumpSteps;

    public long? HeadNonce { get; private set; }
    public DateTime? StuckSince { get; private set; }
    public DateTime? LastSeenPendingAt { get; private set; }
    public DateTime? LastReplaceAt { get; private set; }
    public int ReplaceAttempts { get; private set; }
    public bool GaveUp { get; private set; }
    public DateTime? GaveUpAt { get; private set; }
    public int NextBumpSteps { get { lock (_sync) { return ReplaceAttempts + 1; } } }

    public SponsorHeadWatchSnapshot ToSnapshot()
    {
        lock (_sync)
        {
            return new SponsorHeadWatchSnapshot(
                HeadNonce, StuckSince, LastSeenPendingAt, LastReplaceAt,
                ReplaceAttempts, GaveUp, GaveUpAt);
        }
    }

    public static SponsorHeadWatch FromSnapshot(SponsorHeadWatchSnapshot snapshot)
    {
        return new SponsorHeadWatch
        {
            HeadNonce = snapshot.HeadNonce,
            StuckSince = snapshot.StuckSince,
            LastSeenPendingAt = snapshot.LastSeenPendingAt,
            LastReplaceAt = snapshot.LastReplaceAt,
            ReplaceAttempts = snapshot.ReplaceAttempts,
            GaveUp = snapshot.GaveUp,
            GaveUpAt = snapshot.GaveUpAt,
        };
    }

    public SponsorHeadAction Decide(long latest, long pending, bool trackedHeadExists, DateTime now)
    {
        lock (_sync)
        {
            if (HeadNonce != latest)
            {
                ResetCore();
                if (pending > latest)
                {
                    HeadNonce = latest;
                    StuckSince = now;
                    LastSeenPendingAt = now;
                }

                return SponsorHeadAction.None;
            }

            if (pending > latest)
            {
                LastSeenPendingAt = now;
            }

            if (trackedHeadExists)
            {
                return SponsorHeadAction.None;
            }

            if (LastSeenPendingAt is not { } seen || now - seen > PendingEvidenceWindow)
            {
                return SponsorHeadAction.None;
            }

            if (GaveUp)
            {
                if (GaveUpAt is { } gaveUpAt && now - gaveUpAt < GiveUpRetryAfter)
                {
                    return SponsorHeadAction.None;
                }

                GaveUp = false;
                GaveUpAt = null;
                ReplaceAttempts = 0;
                LastReplaceAt = null;
            }

            if (LastReplaceAt is { } lastReplace)
            {
                if (now - lastReplace < ReplaceRetryAfter)
                {
                    return SponsorHeadAction.None;
                }
            }
            else if (StuckSince is { } stuckSince && now - stuckSince < StuckAfter)
            {
                return SponsorHeadAction.None;
            }

            if (ReplaceAttempts >= MaxReplaceAttempts)
            {
                GaveUp = true;
                GaveUpAt = now;
                return SponsorHeadAction.GiveUp;
            }

            return SponsorHeadAction.Replace;
        }
    }

    public void RecordBroadcast(DateTime now)
    {
        lock (_sync)
        {
            ReplaceAttempts++;
            LastReplaceAt = now;
        }
    }

    public void RecordRejected()
    {
        lock (_sync)
        {
            ReplaceAttempts++;
        }
    }

    private void ResetCore()
    {
        HeadNonce = null;
        StuckSince = null;
        LastSeenPendingAt = null;
        LastReplaceAt = null;
        ReplaceAttempts = 0;
        GaveUp = false;
        GaveUpAt = null;
    }
}

/// <summary>持久化快照（用于 ColNetwork 跨 Pod 共享看门狗时间线）。</summary>
public sealed record SponsorHeadWatchSnapshot(
    long? HeadNonce,
    DateTime? StuckSince,
    DateTime? LastSeenPendingAt,
    DateTime? LastReplaceAt,
    int ReplaceAttempts,
    bool GaveUp,
    DateTime? GaveUpAt);
