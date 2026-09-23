using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Volo.Abp.Autofac;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Logging;
using Xunit;

namespace AbpAdmin.Logging;

/// <summary>
/// Builds the host's Autofac container and holds everything ABP wrote to its initialization-time log
/// at Warning or above to an exact, declared set.
/// </summary>
/// <remarks>
/// <para>
/// The reason this is a test rather than something to notice while reading startup output: ABP does
/// not send these entries to <see cref="ILoggerFactory"/> as they happen. The code that produces
/// them writes through <c>services.GetInitLogger&lt;T&gt;()</c>, which appends to an in-memory
/// <c>DefaultInitLogger&lt;T&gt;.Entries</c> list and nothing else. That list is drained by
/// <c>AbpApplicationBase.WriteInitLogs</c>, which is called on the first line of
/// <c>InitializeModulesAsync</c>, i.e. from <c>app.InitializeApplicationAsync()</c> in Program.cs.
/// Any startup failure before that line takes the whole buffer with it and prints none of it. The
/// host's own non-Development branch is exactly such a failure: it loads openiddict.pfx from disk
/// while services are still being configured, so on a machine without that file every warning
/// collected up to that point is produced and then silently discarded. Waiting for one of these to
/// show up in a log is therefore not a reliable way to find out about it.
/// </para>
/// <para>
/// Container choice, and why it is the reverse of <c>HostServiceGraphValidationTests</c>: these
/// warnings come from ABP's own <c>Volo.Abp.Autofac.AutofacRegistration.Populate</c>, which runs
/// only inside <see cref="AbpAutofacServiceProviderFactory"/>, so this class has to build with
/// Autofac or it observes an empty log. That class refuses Autofac for the mirror-image reason:
/// ValidateScopes and ValidateOnBuild exist only on MS.DI's <c>DefaultServiceProviderFactory</c>,
/// and Autofac neither reads them nor reports captive dependencies at all. Neither class is wrong
/// about the other; they need different containers because the two defects are only visible to
/// different containers. If a future change makes them agree, that is the thing worth checking.
/// </para>
/// <para>
/// <c>InitializeApplicationAsync()</c> is deliberately never called. With Redis enabled it resolves
/// <see cref="IAbpDistributedLock"/>, then <c>IDistributedLockProvider</c>, whose factory calls
/// <c>ConnectionMultiplexer.Connect</c> and really does dial Redis; on a machine without one that is
/// a nine-second stall followed by <c>AbpInitializationException</c>. Reading the buffer straight
/// after the container is built is both the only safe observation point and the only one that exists
/// before the buffer is drained.
/// </para>
/// </remarks>
public class HostInitializationLogTests
{
    /// <summary>
    /// Initialization-time warnings that are accepted as they are.
    /// </summary>
    /// <remarks>
    /// The T3.5 entry is deliberate rather than incidental: the spec requires the "emails are not
    /// really sent" state to be visible in the startup log instead of silent.
    /// </remarks>
    private static readonly IReadOnlyList<ExpectedInitializationWarning> ExpectedWarningsWithRedisDisabled =
    [
        ExpectedT35NullEmailSenderWarning.Entry
    ];

    /// <summary>
    /// The same list for the host as it looks with Redis switched on.
    /// </summary>
    /// <remarks>
    /// Turning Redis on registers <c>MedallionAbpDistributedLock</c>, which lives in the
    /// Volo.Abp.DistributedLocking assembly, and <c>WarnForOrphanedAbpModules</c> then notices that
    /// the assembly ships an ABP module that is not in the host's [DependsOn] chain. With Redis off
    /// nothing from that assembly is registered, so the warning does not exist; the distributed
    /// locking types that are always present come from Volo.Abp.DistributedLocking.Abstractions,
    /// whose module is in the chain.
    /// </remarks>
    private static readonly IReadOnlyList<ExpectedInitializationWarning> ExpectedWarningsWithRedisEnabled =
    [
        ExpectedT35NullEmailSenderWarning.Entry,
        new(
            CategoryName: typeof(AbpAutofacModule).FullName!,
            Level: LogLevel.Warning,
            MessageMustContain: typeof(AbpDistributedLockingModule).FullName!,
            Reason:
            "Known and consciously deferred. Property injection into types from this assembly will " +
            "not work, but the host resolves IAbpDistributedLock through the abstractions module " +
            "and does not depend on property injection into Medallion-backed types today, so the " +
            "[DependsOn] chain is being left alone until the work that actually uses distributed " +
            "locking lands. Recorded as a T3.3 to-do.",
            RevisitWhen:
            "T3.3 (Quartz scheduling with distributed locks) starts, or anything begins resolving " +
            "types out of Volo.Abp.DistributedLocking directly. The fix is one line: add " +
            "typeof(AbpDistributedLockingModule) to AbpAdminHttpApiHostModule's [DependsOn]. Doing " +
            "that makes the warning disappear, which fails this test until the entry is deleted, " +
            "and deleting it is the last step of the fix.")
    ];

    /// <summary>
    /// T3.5 的开发环境邮件告警条目（两个清单共用）。
    /// </summary>
    private static class ExpectedT35NullEmailSenderWarning
    {
        internal static readonly ExpectedInitializationWarning Entry = new(
            CategoryName: typeof(AbpAdminDomainModule).FullName!,
            Level: LogLevel.Warning,
            // 71f0256 起降级实现从静默丢件的 NullEmailSender 换成 FileEmlEmailSender（.eml 落盘
            // App_Data/outgoing-emails），告警文案同步换名——匹配词跟实现走。
            MessageMustContain: "FileEmlEmailSender",
            Reason:
                "T3.5 的设计要求：开发配置（appsettings.Development.json 的 Mailing:UseRealSender=false）下" +
                "邮件不真发，这条 Warning 让『邮件改落盘』在启动日志里可见而不是静默（规格 T3.5 第 3 步 b）。",
            RevisitWhen:
                "若开发环境改为默认真发邮件（删除 UseRealSender 开关或置 true），这条警告消失，" +
                "本条目需同步删除，否则 missing 断言会失败。");
    }

    /// <summary>
    /// The floor for how many entries the initialization log must contain before a verdict about it
    /// means anything.
    /// </summary>
    /// <remarks>
    /// "No warnings" is trivially true of an empty list, and an empty list is the shape this test
    /// takes if ABP changes where the buffer lives, if <c>GetAllEntries</c> is read after something
    /// has already drained it, or if the container is never built. Measured at 140 with Redis off
    /// and 141 with it on, all but the one warning being Debug entries from
    /// <c>Volo.Abp.AbpApplicationBase</c>. The floor sits far below that because the exact number
    /// moves with every module added or removed and is not itself worth guarding; there is no
    /// ceiling for the same reason.
    /// </remarks>
    private const int MinimumInitLogEntries = 50;

    private const int MaxReportedEntries = 25;

    private const string InjectedWarningMessage =
        "Injected by " + nameof(Host_Initialization_Log_Inspection_Should_Detect_An_Injected_Warning) + ".";

    /// <summary>
    /// Turns on the host's Redis branch for
    /// <see cref="Host_Initialization_Log_Should_Contain_Only_The_Expected_Warnings_With_Redis_Enabled"/>.
    /// </summary>
    /// <remarks>
    /// IsEnabled is written as an explicit true rather than left absent, because the two readers of
    /// this key disagree on what absent means: the host's own GetValue("Redis:IsEnabled", false)
    /// treats a missing key as off, while AbpCachingStackExchangeRedisModule treats it as on. Only an
    /// explicit value gets both to the same branch.
    /// <para>
    /// Port 1 is privileged, unassigned and could not plausibly be a real Redis. Nothing here
    /// resolves a Redis service, so no connection should ever be attempted; the point of an address
    /// this obviously fake is that if some future change does start connecting, it fails at once and
    /// visibly, instead of quietly succeeding against whatever happens to be running on a developer's
    /// machine and making this test depend on it.
    /// </para>
    /// </remarks>
    private static readonly KeyValuePair<string, string?>[] RedisEnabledConfiguration =
    [
        new("Redis:IsEnabled", "true"),
        new("Redis:Configuration", "127.0.0.1:1")
    ];

    [Fact]
    public async Task Host_Initialization_Log_Should_Contain_Only_The_Expected_Warnings()
    {
        var entries = await ReadInitializationLogAsync();

        AssertWarningsMatchExactly(
            entries,
            ExpectedWarningsWithRedisDisabled,
            nameof(ExpectedWarningsWithRedisDisabled),
            "the configuration as it ships");
    }

    /// <summary>
    /// Runs the same check over the host as it looks with Redis switched on.
    /// </summary>
    /// <remarks>
    /// This is the half that currently has something to say. T3.2 (SignalR with a Redis backplane)
    /// and T3.3 (Quartz scheduling with distributed locks) both need Redis, so the branch that is off
    /// today is the shape this host is heading for, and the orphaned-module warning is a defect that
    /// lands the day the switch is flipped.
    /// <para>
    /// It doubles as the proof that the configuration override worked. If the in-memory source were
    /// ignored, or the key renamed, the Redis branch would not run, the warning would not be
    /// produced, and the set comparison would fail on the missing side rather than reporting success
    /// over a graph identical to the test above.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Host_Initialization_Log_Should_Contain_Only_The_Expected_Warnings_With_Redis_Enabled()
    {
        var entries = await ReadInitializationLogAsync(RedisEnabledConfiguration);

        AssertWarningsMatchExactly(
            entries,
            ExpectedWarningsWithRedisEnabled,
            nameof(ExpectedWarningsWithRedisEnabled),
            "Redis enabled");
    }

    [Fact]
    public async Task Host_Initialization_Log_Inspection_Should_Detect_An_Injected_Warning()
    {
        // A guardrail that passes because it inspected nothing is worse than no guardrail. This
        // writes one warning into the real host's initialization log through the same entry point
        // ABP itself uses, and demands that the comparison call it unexpected. A buffer that came
        // back empty, a read that happens after something drained it, or an allow-list predicate
        // wide enough to swallow an unrelated warning all surface here rather than turning the two
        // tests above green for the wrong reason.
        var builder = await HostUnderTest.CreateBuilderAsync(useAutofac: true);
        builder.Services.GetInitLogger<InjectedWarningProbe>().LogWarning(InjectedWarningMessage);

        var entries = await BuildContainerAndReadInitializationLogAsync(builder);

        UnexpectedWarnings(entries, ExpectedWarningsWithRedisDisabled).ShouldContain(
            entry => entry.CategoryName == typeof(InjectedWarningProbe).FullName
                     && entry.Message == InjectedWarningMessage,
            $"{nameof(Host_Initialization_Log_Should_Contain_Only_The_Expected_Warnings)} is no longer " +
            "detecting anything. Either the initialization log came back empty, or ABP stopped " +
            $"buffering entries the way {nameof(BuildContainerAndReadInitializationLogAsync)} reads " +
            "them.");
    }

    private static void AssertWarningsMatchExactly(
        IReadOnlyList<InitializationLogEntry> entries,
        IReadOnlyList<ExpectedInitializationWarning> expected,
        string expectedListName,
        string scenario)
    {
        entries.Count.ShouldBeGreaterThanOrEqualTo(
            MinimumInitLogEntries,
            $"Only {entries.Count} initialization log entry(ies) were read with {scenario}, which is " +
            $"below the {MinimumInitLogEntries} this test needs before it can claim anything. A " +
            "verdict about warnings in a log nobody managed to read is meaningless, so this fails " +
            "instead of reporting success. Check that the Autofac container is still being built " +
            "before the entries are read, and that nothing has already drained the buffer.");

        var warnings = WarningsAndAbove(entries);

        var unexpected = UnexpectedWarnings(entries, expected);
        var missing = expected.Where(candidate => !warnings.Any(candidate.Matches)).ToList();

        unexpected.ShouldBeEmpty(BuildUnexpectedMessage(unexpected, expectedListName, scenario));
        missing.ShouldBeEmpty(BuildMissingMessage(missing, warnings, expectedListName, scenario));
    }

    private static IReadOnlyList<InitializationLogEntry> UnexpectedWarnings(
        IReadOnlyList<InitializationLogEntry> entries,
        IReadOnlyList<ExpectedInitializationWarning> expected)
    {
        return WarningsAndAbove(entries)
            .Where(entry => !expected.Any(candidate => candidate.Matches(entry)))
            .ToList();
    }

    private static IReadOnlyList<InitializationLogEntry> WarningsAndAbove(
        IReadOnlyList<InitializationLogEntry> entries)
    {
        return entries
            .Where(entry => entry.Level >= LogLevel.Warning && entry.Level != LogLevel.None)
            .ToList();
    }

    private static async Task<IReadOnlyList<InitializationLogEntry>> ReadInitializationLogAsync(
        IEnumerable<KeyValuePair<string, string?>>? configurationOverrides = null)
    {
        // useAutofac is the whole point. Without it Build() uses MS.DI, Populate never runs, and the
        // warnings this class exists for are never produced.
        var builder = await HostUnderTest.CreateBuilderAsync(configurationOverrides, useAutofac: true);

        return await BuildContainerAndReadInitializationLogAsync(builder);
    }

    private static async Task<IReadOnlyList<InitializationLogEntry>> BuildContainerAndReadInitializationLogAsync(
        WebApplicationBuilder builder)
    {
        // The factory is a singleton instance sitting in the service collection, so this is the same
        // object the entries are written into and reading it needs no container resolution at all.
        // This is how ABP's own GetInitLogger<T>() finds it.
        var initLoggerFactory = builder.Services.GetSingletonInstance<IInitLoggerFactory>();

        // Build() and nothing further. This runs AbpAutofacServiceProviderFactory, which is where
        // Populate writes its warnings. InitializeApplicationAsync() would drain the buffer into
        // ILoggerFactory and, with Redis on, try to open a Redis connection; see the class remarks.
        await using var app = builder.Build();

        return initLoggerFactory
            .GetAllEntries()
            .Select(entry => new InitializationLogEntry(entry.CategoryName, entry.LogLevel, entry.Message))
            .ToList();
    }

    private static string BuildUnexpectedMessage(
        IReadOnlyList<InitializationLogEntry> unexpected,
        string expectedListName,
        string scenario)
    {
        var message = new StringBuilder();

        message.AppendLine(
            $"{unexpected.Count} unexpected initialization-time warning(s) with {scenario}.");
        message.AppendLine(
            "ABP buffers these in memory and only flushes them at app.InitializeApplicationAsync(), " +
            "so they will not appear in any log if startup fails before that point. This test is " +
            "where they are meant to be noticed.");
        message.AppendLine();

        foreach (var (entry, index) in unexpected.Take(MaxReportedEntries).Select((x, i) => (x, i)))
        {
            message.AppendLine($"  [{index + 1}] {entry.Level,-8} {entry.CategoryName}");
            message.AppendLine($"      {entry.Message}");
        }

        AppendTruncationNotice(message, unexpected.Count);

        message.AppendLine();
        message.AppendLine(
            "Fix the cause. Only if it is genuinely upstream and unfixable here, add an entry to " +
            $"{expectedListName} with a reason and a revisit condition, and keep it narrow enough " +
            "that it cannot also match some other warning from the same category.");

        return message.ToString();
    }

    private static string BuildMissingMessage(
        IReadOnlyList<ExpectedInitializationWarning> missing,
        IReadOnlyList<InitializationLogEntry> warnings,
        string expectedListName,
        string scenario)
    {
        var message = new StringBuilder();

        message.AppendLine(
            $"{missing.Count} declared initialization-time warning(s) did not appear with {scenario}.");
        message.AppendLine(
            "This half of the comparison exists so the test cannot go green by observing nothing. " +
            "Either the warning was fixed, in which case delete the entry, or the reading of the " +
            "initialization log has stopped working, in which case the other half of this " +
            "comparison is no longer checking anything.");
        message.AppendLine();

        foreach (var (candidate, index) in missing.Take(MaxReportedEntries).Select((x, i) => (x, i)))
        {
            message.AppendLine($"  [{index + 1}] {candidate}");
        }

        AppendTruncationNotice(message, missing.Count);

        message.AppendLine();
        message.AppendLine(
            $"{warnings.Count} warning(s) were actually present:");

        foreach (var entry in warnings.Take(MaxReportedEntries))
        {
            message.AppendLine($"  {entry}");
        }

        AppendTruncationNotice(message, warnings.Count);

        message.AppendLine();
        message.AppendLine(
            $"If ABP merely reworded the message, loosen the entry in {expectedListName} rather than " +
            "removing it.");

        return message.ToString();
    }

    private static void AppendTruncationNotice(StringBuilder message, int total)
    {
        if (total > MaxReportedEntries)
        {
            message.AppendLine($"  ... and {total - MaxReportedEntries} more.");
        }
    }

    private sealed record InitializationLogEntry(string CategoryName, LogLevel Level, string Message)
    {
        // Shouldly dumps the collection contents before the custom message, and the generated record
        // ToString repeats every field there.
        public override string ToString() => $"[{Level}] {CategoryName}: {Message}";
    }

    /// <summary>
    /// One accepted initialization-time warning, matched on category, level and a required substring
    /// of the message.
    /// </summary>
    /// <remarks>
    /// The substring is what makes this specific, and choosing it is a trade-off with a cost on both
    /// sides. Category and level alone are not enough: <c>WarnForOrphanedAbpModules</c> emits one
    /// warning per orphaned module, all of them Warning from the same category, so an entry matched
    /// that way would absorb a second, different orphaned module without a word — precisely the
    /// event this test exists to report. Requiring the whole message instead would be tighter still,
    /// but that message is five concatenated fragments of English prose, and any rewording upstream
    /// would turn this red for a reason nobody here needs to act on. The module's full type name is
    /// the middle ground: it is specific to exactly one orphaned module, and it is a type name rather
    /// than prose, so it only changes when the thing being described changes. It is not free either
    /// — an upstream rename would show up as a missing warning — but that failure is at least
    /// pointing at something real.
    /// </remarks>
    private sealed record ExpectedInitializationWarning(
        string CategoryName,
        LogLevel Level,
        string MessageMustContain,
        string Reason,
        string RevisitWhen)
    {
        public bool Matches(InitializationLogEntry entry)
        {
            return entry.CategoryName == CategoryName
                   && entry.Level == Level
                   && entry.Message.Contains(MessageMustContain, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return $"[{Level}] {CategoryName} containing '{MessageMustContain}'";
        }
    }

    private sealed class InjectedWarningProbe;
}
