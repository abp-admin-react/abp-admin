using System;
using AbpAdmin.Collection.Chain;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class SponsorHeadWatchTests
{
    private static readonly DateTime T0 = new(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unowned_Stuck_Head_Triggers_After_Five_Minutes_And_Survives_Flaky_Pending_Reads()
    {
        var watch = new SponsorHeadWatch();

        watch.Decide(10, 11, trackedHeadExists: false, T0).ShouldBe(SponsorHeadAction.None);
        watch.HeadNonce.ShouldBe(10L);

        // 负载均衡节点视图偶尔闪回 pending==latest 不复位 StuckSince
        watch.Decide(10, 10, trackedHeadExists: false, T0.AddMinutes(1)).ShouldBe(SponsorHeadAction.None);
        watch.Decide(10, 11, trackedHeadExists: false, T0.AddMinutes(4)).ShouldBe(SponsorHeadAction.None);
        watch.Decide(10, 10, trackedHeadExists: false, T0.AddMinutes(5)).ShouldBe(SponsorHeadAction.Replace);
        watch.NextBumpSteps.ShouldBe(1);

        watch.RecordBroadcast(T0.AddMinutes(5));
        watch.NextBumpSteps.ShouldBe(2);
        watch.Decide(10, 11, trackedHeadExists: false, T0.AddMinutes(7)).ShouldBe(SponsorHeadAction.None);
        watch.Decide(10, 11, trackedHeadExists: false, T0.AddMinutes(8)).ShouldBe(SponsorHeadAction.Replace);

        // latest 前进后整体复位
        watch.Decide(11, 11, trackedHeadExists: false, T0.AddMinutes(9)).ShouldBe(SponsorHeadAction.None);
        watch.HeadNonce.ShouldBeNull();
        watch.ReplaceAttempts.ShouldBe(0);
    }

    [Fact]
    public void Stale_Pending_Evidence_Or_Tracked_Owner_Suppresses_Watchdog()
    {
        var watch = new SponsorHeadWatch();
        watch.Decide(20, 21, trackedHeadExists: false, T0).ShouldBe(SponsorHeadAction.None);

        // 连续 > 2 分钟节点均不再报告 pending > latest（交易已被池丢弃），不出手
        watch.Decide(20, 20, trackedHeadExists: false, T0.AddMinutes(5)).ShouldBe(SponsorHeadAction.None);

        // 有主队头交给 owner 自行替换，但 StuckSince 保留
        var watch2 = new SponsorHeadWatch();
        watch2.Decide(30, 31, trackedHeadExists: true, T0).ShouldBe(SponsorHeadAction.None);
        watch2.Decide(30, 31, trackedHeadExists: true, T0.AddMinutes(6)).ShouldBe(SponsorHeadAction.None);
        watch2.Decide(30, 31, trackedHeadExists: false, T0.AddMinutes(6)).ShouldBe(SponsorHeadAction.Replace);
    }

    [Fact]
    public void Rejected_Retries_Immediately_And_Exhausted_Attempts_Give_Up_For_One_Hour()
    {
        var watch = new SponsorHeadWatch();
        watch.Decide(40, 41, trackedHeadExists: false, T0).ShouldBe(SponsorHeadAction.None);

        var t = T0.AddMinutes(5);
        for (var i = 1; i <= SponsorHeadWatch.MaxReplaceAttempts; i++)
        {
            watch.Decide(40, 41, trackedHeadExists: false, t).ShouldBe(SponsorHeadAction.Replace);
            watch.NextBumpSteps.ShouldBe(i);
            watch.RecordRejected(); // 被拒后不等待 3 分钟，下轮立即可再抬档
        }

        var gaveUpAt = t.AddSeconds(10);
        watch.Decide(40, 41, trackedHeadExists: false, gaveUpAt).ShouldBe(SponsorHeadAction.GiveUp);
        watch.GaveUp.ShouldBeTrue();
        watch.Decide(40, 41, trackedHeadExists: false, gaveUpAt.AddMinutes(50)).ShouldBe(SponsorHeadAction.None);

        // 1 小时冷却期满，且最近仍有 pending 证据 → 从第 1 档重启
        watch.Decide(40, 41, trackedHeadExists: false, gaveUpAt.AddHours(1).AddSeconds(1)).ShouldBe(SponsorHeadAction.Replace);
        watch.NextBumpSteps.ShouldBe(1);
    }
}
