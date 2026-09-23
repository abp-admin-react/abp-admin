using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.DistributedLocking;
using Xunit;

namespace AbpAdmin.DependencyInjection;

/// <summary>
/// Rebuilds the host service graph with Microsoft.Extensions.DependencyInjection purely as a static
/// check for captive dependencies. The running host is untouched and keeps using Autofac.
/// </summary>
/// <remarks>
/// <para>
/// ValidateScopes and ValidateOnBuild live on <see cref="ServiceProviderOptions"/>, and only MS.DI's
/// <see cref="DefaultServiceProviderFactory"/> reads them. Program.cs calls UseAutofac(), which swaps
/// in AbpAutofacServiceProviderFactory, so in this solution those two switches are not merely off,
/// they are unread. Where MS.DI refuses to build a singleton that takes a scoped service, Autofac
/// resolves that scoped service on the root scope and lets the singleton pin it for the lifetime of
/// the process, while every request scope gets its own instance. Nothing is logged, so the bug
/// travels through development, CI and single-instance production until stale cross-request state
/// surfaces somewhere else entirely.
/// </para>
/// <para>
/// <c>HostInitializationLogTests</c> builds the same host the opposite way, with Autofac, and the
/// contrast is deliberate rather than an inconsistency: the warnings that class reads are written by
/// ABP's own Autofac registration code and do not exist under MS.DI, while the lifetime validation
/// here does not exist under Autofac. Changing either class to match the other would blind it.
/// </para>
/// <para>
/// Known blind spot: MS.DI's validator only walks constructor parameters. ABP injects properties
/// through Autofac's PropertiesAutowired with AbpPropertySelector, and registrations backed by a
/// factory delegate are opaque to the validator because it cannot see inside the lambda. A singleton
/// that captures a scoped service through an injected property, or by resolving it inside a factory,
/// passes this test. A green run therefore means "no constructor-level captive dependency", not "no
/// captive dependency". Do not read it as full coverage.
/// </para>
/// </remarks>
public class HostServiceGraphValidationTests
{
    /// <summary>
    /// Captive dependencies that are accepted as they are, normally because they come from an
    /// upstream package that cannot be fixed from this repository.
    /// </summary>
    /// <remarks>
    /// An entry matches only when the fully qualified singleton type and scoped type both line up, so
    /// it can never absorb a violation it was not written for. Adding one is a deliberate decision:
    /// state why it is tolerated and what would make it worth revisiting.
    /// </remarks>
    private static readonly IReadOnlyList<ToleratedCaptiveDependency> ToleratedCaptiveDependencies = [];

    /// <summary>
    /// Registrations that MS.DI cannot construct even though Autofac can, for example because they
    /// rely on a resolution feature MS.DI does not implement.
    /// </summary>
    /// <remarks>
    /// These are tolerated rather than fixed, but they are not free: MS.DI stops at the first error
    /// per registration, so anything listed here has its subgraph excluded from the captive
    /// dependency check above. Matching is on the exact descriptor text MS.DI reports.
    /// </remarks>
    private static readonly IReadOnlyList<ToleratedUnconstructibleRegistration> ToleratedUnconstructibleRegistrations = [];

    private const int MaxReportedFailures = 25;

    private static readonly Regex CaptiveDependencyPattern = new(
        @"Cannot consume scoped service '(?<scoped>[^']+)' from singleton '(?<singleton>[^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DescriptorPattern = new(
        @"^Error while validating the service descriptor '(?<descriptor>.+)': (?<detail>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>
    /// Turns on the host's Redis branch for
    /// <see cref="Host_Service_Graph_Should_Be_Valid_With_Redis_Enabled"/>.
    /// </summary>
    /// <remarks>
    /// IsEnabled is written as an explicit true (historical): the host's ConfigureRedis used to
    /// treat a missing key as off while the ABP caching module treated it as on, so an explicit
    /// value was the only way to pin the intended branch. That disagreement has since been fixed —
    /// absent/unset now means ENABLED for the host and the ABP caching module (explicit false
    /// disables; an unparseable value fails the host at startup via ConfigureRedis, while
    /// CacheMonitorRedisConnection deliberately treats unparseable as disabled so the monitor
    /// page cannot crash) — but the explicit true also guards this test against an environment
    /// config file that disables Redis, so it stays.
    /// <para>
    /// The endpoint points at a port nothing listens on. Building the graph is not supposed to open a
    /// connection at all, and pinning it to loopback means that if some future change does try, it is
    /// refused straight away instead of sitting on a connect timeout and turning this into a test
    /// that hangs.
    /// </para>
    /// </remarks>
    private static readonly KeyValuePair<string, string?>[] RedisEnabledConfiguration =
    [
        new("Redis:IsEnabled", "true"),
        new("Redis:Configuration", "127.0.0.1:6399")
    ];

    /// <summary>
    /// The registrations <see cref="AbpAdminHttpApiHostModule"/> adds only when Redis is on, used to
    /// prove <see cref="Host_Service_Graph_Should_Be_Valid_With_Redis_Enabled"/> validated a graph
    /// that actually contains them.
    /// </summary>
    /// <remarks>
    /// Each predicate has to be narrow enough to fail against the Redis-off collection.
    /// <see cref="IAbpDistributedLock"/> on its own is not: AbpDistributedLockingAbstractionsModule,
    /// which is in the graph either way, already registers LocalAbpDistributedLock as a singleton
    /// against that interface, so the Redis-off collection has an <see cref="IAbpDistributedLock"/>
    /// too and only the implementation type tells the two apart.
    /// </remarks>
    private static readonly IReadOnlyList<RedisGatedRegistration> RedisGatedRegistrations =
    [
        new(
            $"{nameof(IDistributedLockProvider)} (singleton)",
            descriptor => descriptor.ServiceType == typeof(IDistributedLockProvider)
                          && descriptor.Lifetime == ServiceLifetime.Singleton),
        new(
            $"{nameof(IAbpDistributedLock)} -> {nameof(MedallionAbpDistributedLock)} (transient)",
            descriptor => descriptor.ServiceType == typeof(IAbpDistributedLock)
                          && descriptor.ImplementationType == typeof(MedallionAbpDistributedLock))
    ];

    [Fact]
    public async Task Host_Service_Graph_Should_Not_Contain_Captive_Dependencies()
    {
        var failures = ValidateWithMicrosoftDependencyInjection(await CreateHostServiceCollectionAsync());

        var unexpected = UntoleratedCaptiveDependencies(failures);

        unexpected.ShouldBeEmpty(BuildCaptiveDependencyMessage(unexpected, failures));
    }

    [Fact]
    public async Task Host_Service_Graph_Should_Be_Fully_Constructible_By_Microsoft_DependencyInjection()
    {
        // Not a goal in itself. MS.DI abandons a registration as soon as it cannot resolve one of its
        // constructor parameters, and never gets around to checking lifetimes further down that
        // subgraph. Holding this at zero is what keeps the captive dependency check above complete.
        var failures = ValidateWithMicrosoftDependencyInjection(await CreateHostServiceCollectionAsync());

        var unexpected = UntoleratedUnconstructibleRegistrations(failures);

        unexpected.ShouldBeEmpty(BuildUnconstructibleMessage(unexpected));
    }

    [Fact]
    public async Task Host_Service_Graph_Validation_Should_Detect_An_Injected_Captive_Dependency()
    {
        // A guardrail that passes because it inspected nothing is worse than no guardrail. This plants
        // one violation in the real host collection and demands it be reported, so a service
        // collection that came back empty, a validator that quietly stopped walking, or a change in
        // MS.DI's message wording all surface here instead of turning the assertions above green for
        // the wrong reason. AddScoped/AddSingleton register probes for a throwaway provider here, they
        // are not application service registrations.
        var services = await CreateHostServiceCollectionAsync();
        services.AddScoped<ScopedProbe>();
        services.AddSingleton<SingletonProbeCapturingScoped>();

        var failures = ValidateWithMicrosoftDependencyInjection(services);

        failures.ShouldContain(
            failure => failure.SingletonType == typeof(SingletonProbeCapturingScoped).FullName
                       && failure.ScopedType == typeof(ScopedProbe).FullName,
            $"{nameof(Host_Service_Graph_Should_Not_Contain_Captive_Dependencies)} is no longer " +
            "detecting anything. Either the host service collection came back empty, or MS.DI stopped " +
            $"reporting captive dependencies the way {nameof(CaptiveDependencyPattern)} expects.");
    }

    /// <summary>
    /// Runs the same two checks over the host graph as it looks with Redis switched on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one place in this class that does not read configuration as it ships. Everything
    /// else deliberately uses the host's real appsettings.json so that what gets validated is what
    /// gets deployed, and that principle is broken here on purpose: appsettings.json has
    /// Redis:IsEnabled false, which means ConfigureRedis returns early and the two registrations it
    /// would add, <see cref="IDistributedLockProvider"/> and <see cref="IAbpDistributedLock"/>, are
    /// absent from the graph the tests above validate. T3.2 (SignalR with a Redis backplane) and T3.3
    /// (Quartz scheduling with distributed locks) both require Redis, so the branch that is unchecked
    /// today is the shape this host is heading for, and it is cheaper to hold the guardrail over it
    /// now than to discover a captive dependency after the switch is flipped.
    /// </para>
    /// <para>
    /// The reach is slightly wider than those two registrations. AbpCachingStackExchangeRedisModule
    /// reads the same key, so flipping it also puts RedisCacheImpl and AbpRedisCache in place of
    /// MemoryDistributedCache behind IDistributedCache, and those come under validation here as well.
    /// </para>
    /// <para>
    /// Division of labour: the tests above guard the shape that is deployed today, this one guards the
    /// shape that is coming. Both have to stay green. A failure here is not "a test using odd
    /// configuration", it is a defect that will land the day Redis is turned on, so it wants fixing at
    /// the same standard.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Host_Service_Graph_Should_Be_Valid_With_Redis_Enabled()
    {
        var services = await CreateHostServiceCollectionAsync(RedisEnabledConfiguration);

        AssertRedisBranchReachedTheServiceCollection(services, await CreateHostServiceCollectionAsync());

        var failures = ValidateWithMicrosoftDependencyInjection(services);

        var captiveDependencies = UntoleratedCaptiveDependencies(failures);
        var unconstructible = UntoleratedUnconstructibleRegistrations(failures);

        captiveDependencies.ShouldBeEmpty(BuildCaptiveDependencyMessage(captiveDependencies, failures));
        unconstructible.ShouldBeEmpty(BuildUnconstructibleMessage(unconstructible));
    }

    /// <summary>
    /// Proves the Redis branch is present in the collection about to be validated, and that finding it
    /// there means something.
    /// </summary>
    /// <remarks>
    /// The failure mode this exists for is a green run over a graph with no Redis in it: a renamed
    /// configuration key, an in-memory source that ends up outranked by appsettings.json, or a second
    /// condition inside ConfigureRedis that nobody noticed all leave
    /// <see cref="Host_Service_Graph_Should_Be_Valid_With_Redis_Enabled"/> validating exactly the same
    /// graph as the tests above and reporting success. The comparison against the Redis-off collection
    /// is what stops the first half from being satisfied by a registration that was there anyway.
    /// </remarks>
    private static void AssertRedisBranchReachedTheServiceCollection(
        IServiceCollection redisEnabled,
        IServiceCollection redisDisabled)
    {
        var missing = RedisGatedRegistrations
            .Where(registration => !redisEnabled.Any(registration.Matches))
            .Select(registration => registration.Description)
            .ToList();

        missing.ShouldBeEmpty(
            "The Redis branch did not take effect. The registrations listed above never reached the " +
            "service collection, so this run validated no Redis registration at all and its result " +
            "says nothing about the Redis-enabled host. " +
            $"{nameof(AbpAdminHttpApiHostModule)}.ConfigureRedis adds them only when Redis:IsEnabled " +
            "is true and Redis:Configuration is non-empty, so check that the keys in " +
            $"{nameof(RedisEnabledConfiguration)} still match the ones ConfigureRedis reads, and that " +
            "the in-memory source is added after every source WebApplication.CreateBuilder registers " +
            "so that it wins the precedence order.");

        var alsoPresentWithRedisDisabled = RedisGatedRegistrations
            .Where(registration => redisDisabled.Any(registration.Matches))
            .Select(registration => registration.Description)
            .ToList();

        alsoPresentWithRedisDisabled.ShouldBeEmpty(
            "The registrations listed above are in the graph even with Redis disabled, so finding them " +
            "no longer distinguishes a working configuration override from a broken one and the check " +
            "before this one can be satisfied by the Redis-off graph. Narrow the predicates in " +
            $"{nameof(RedisGatedRegistrations)} until they match only what ConfigureRedis adds.");
    }

    /// <summary>
    /// T3.5：邮件发送开关与短信唯一入口的服务图断言。
    /// </summary>
    /// <remarks>
    /// HostUnderTest 把环境钉在 Development，所以"默认集合"读到的是
    /// appsettings.Development.json 的 Mailing:UseRealSender=false —— 断言 FileEmlEmailSender
    /// （71f0256 起的降级实现：邮件落盘 App_Data/outgoing-emails 可观测，替代静默丢件的
    /// NullEmailSender）在最后一个 IEmailSender 注册上，证明运行期开关真的换掉了实现；
    /// 再用配置覆盖 true 模拟生产缺省（appsettings.json 不配该键时 GetValue 的默认就是 true），
    /// 断言 MailKitSmtpEmailSender。
    /// ISmsSender 断言全图只有 SmsSenderResolver 一个注册（厂商按具体类型自注册，不占接口）。
    /// </remarks>
    [Fact]
    public async Task Host_Service_Graph_Should_Wire_T35_Email_And_Sms_Senders()
    {
        var development = await CreateHostServiceCollectionAsync();

        development.Last(descriptor => descriptor.ServiceType == typeof(Volo.Abp.Emailing.IEmailSender))
            .ImplementationType.ShouldBe(typeof(AbpAdmin.Mailing.FileEmlEmailSender));

        var production = await CreateHostServiceCollectionAsync(
            [new KeyValuePair<string, string?>("Mailing:UseRealSender", "true")]);
        production.Last(descriptor => descriptor.ServiceType == typeof(Volo.Abp.Emailing.IEmailSender))
            .ImplementationType.ShouldBe(typeof(Volo.Abp.MailKit.MailKitSmtpEmailSender));

        var smsDescriptors = development
            .Where(descriptor => descriptor.ServiceType == typeof(Volo.Abp.Sms.ISmsSender))
            .ToList();
        smsDescriptors.ShouldHaveSingleItem();
        smsDescriptors[0].ImplementationType.ShouldBe(typeof(AbpAdmin.Sms.SmsSenderResolver));
    }

    /// <summary>
    /// Returns the same <see cref="IServiceCollection"/> the host builds, by replaying Program.cs
    /// without UseAutofac() and without Build().
    /// </summary>
    /// <param name="configurationOverrides">
    /// Configuration entries layered on top of the host's own files. Only
    /// <see cref="Host_Service_Graph_Should_Be_Valid_With_Redis_Enabled"/> passes any; the other tests
    /// validate the configuration as it ships.
    /// </param>
    /// <remarks>
    /// Leaving Autofac out is what makes the checks in this class possible, and
    /// <see cref="HostUnderTest.CreateBuilderAsync"/> defaults to that. It is not the shape the host
    /// runs in.
    /// </remarks>
    private static Task<IServiceCollection> CreateHostServiceCollectionAsync(
        IEnumerable<KeyValuePair<string, string?>>? configurationOverrides = null)
    {
        return HostUnderTest.CreateServiceCollectionAsync(configurationOverrides);
    }

    private static IReadOnlyList<ValidationFailure> UntoleratedCaptiveDependencies(
        IReadOnlyList<ValidationFailure> failures)
    {
        return failures
            .Where(failure => failure.IsCaptiveDependency)
            .Where(failure => !ToleratedCaptiveDependencies.Any(tolerated => tolerated.Matches(failure)))
            .ToList();
    }

    private static IReadOnlyList<ValidationFailure> UntoleratedUnconstructibleRegistrations(
        IReadOnlyList<ValidationFailure> failures)
    {
        return failures
            .Where(failure => !failure.IsCaptiveDependency)
            .Where(failure => !ToleratedUnconstructibleRegistrations.Any(tolerated => tolerated.Matches(failure)))
            .ToList();
    }

    private static IReadOnlyList<ValidationFailure> ValidateWithMicrosoftDependencyInjection(IServiceCollection services)
    {
        // Both switches are required. ValidateScopes on its own only installs the call site validator,
        // which then fires when something is actually resolved. ValidateOnBuild is what walks every
        // registration up front, which is what turns the lifetime check into a static one.
        var factory = new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        try
        {
            var provider = factory.CreateServiceProvider(factory.CreateBuilder(services));
            (provider as IDisposable)?.Dispose();
            return [];
        }
        catch (AggregateException exception)
        {
            return exception.InnerExceptions.Select(inner => ParseFailure(inner.Message)).ToList();
        }
    }

    private static ValidationFailure ParseFailure(string message)
    {
        var descriptorMatch = DescriptorPattern.Match(message);
        var captiveMatch = CaptiveDependencyPattern.Match(message);

        return new ValidationFailure(
            descriptorMatch.Success ? descriptorMatch.Groups["descriptor"].Value : message,
            descriptorMatch.Success ? descriptorMatch.Groups["detail"].Value : message,
            captiveMatch.Success ? captiveMatch.Groups["singleton"].Value : null,
            captiveMatch.Success ? captiveMatch.Groups["scoped"].Value : null);
    }

    private static string BuildCaptiveDependencyMessage(
        IReadOnlyList<ValidationFailure> violations,
        IReadOnlyList<ValidationFailure> allFailures)
    {
        var message = new StringBuilder();

        message.AppendLine(
            $"{violations.Count} captive dependency(ies) in the {nameof(AbpAdminHttpApiHostModule)} service graph.");
        message.AppendLine(
            "Each one is a singleton taking a scoped service through its constructor. It pins the " +
            "instance built on the root scope for the whole process while every request scope gets a " +
            "different one, and Autofac does this silently at runtime.");
        message.AppendLine();

        foreach (var (violation, index) in violations.Take(MaxReportedFailures).Select((x, i) => (x, i)))
        {
            message.AppendLine($"  [{index + 1}] singleton    {violation.SingletonType}");
            message.AppendLine($"      captured scoped {violation.ScopedType}");
            message.AppendLine($"      registration    {violation.Descriptor}");
        }

        AppendTruncationNotice(message, violations.Count);

        message.AppendLine();
        message.AppendLine("Fix each one, in order of preference:");
        message.AppendLine(
            "  1. Stop injecting the scoped service. Take IServiceScopeFactory and resolve it inside a " +
            "scope per operation, or reach it through LazyServiceProvider.");
        message.AppendLine(
            "  2. Give the consumer a shorter lifetime with IScopedDependency or ITransientDependency, " +
            "if it holds nothing that has to survive across requests.");
        message.AppendLine(
            $"  3. Only if the violation is upstream and unfixable here, add it to " +
            $"{nameof(ToleratedCaptiveDependencies)} with a reason and a revisit condition.");

        var unconstructible = allFailures.Count(failure => !failure.IsCaptiveDependency);
        if (unconstructible > 0)
        {
            message.AppendLine();
            message.AppendLine(
                $"Context: {unconstructible} further registration(s) could not be constructed by MS.DI " +
                $"at all, so their subgraphs were never lifetime-checked. See " +
                $"{nameof(Host_Service_Graph_Should_Be_Fully_Constructible_By_Microsoft_DependencyInjection)}.");
        }

        return message.ToString();
    }

    private static string BuildUnconstructibleMessage(IReadOnlyList<ValidationFailure> failures)
    {
        var message = new StringBuilder();

        message.AppendLine(
            $"{failures.Count} registration(s) in the {nameof(AbpAdminHttpApiHostModule)} service graph " +
            "cannot be constructed by MS.DI.");
        message.AppendLine(
            "This is not necessarily broken at runtime, because Autofac resolves more than MS.DI does. " +
            "It does mean MS.DI gave up on these registrations before checking their lifetimes, which " +
            $"narrows {nameof(Host_Service_Graph_Should_Not_Contain_Captive_Dependencies)}.");
        message.AppendLine();

        foreach (var (failure, index) in failures.Take(MaxReportedFailures).Select((x, i) => (x, i)))
        {
            message.AppendLine($"  [{index + 1}] registration {failure.Descriptor}");
            message.AppendLine($"      {failure.Detail}");
        }

        AppendTruncationNotice(message, failures.Count);

        message.AppendLine();
        message.AppendLine(
            "Either make the registration resolvable by MS.DI (usually a missing registration or a " +
            "constructor parameter only Autofac can supply), or, if it genuinely depends on Autofac, " +
            $"add it to {nameof(ToleratedUnconstructibleRegistrations)} with a reason and a revisit " +
            "condition.");

        return message.ToString();
    }

    private static void AppendTruncationNotice(StringBuilder message, int total)
    {
        if (total > MaxReportedFailures)
        {
            message.AppendLine($"  ... and {total - MaxReportedFailures} more.");
        }
    }

    private sealed record ValidationFailure(
        string Descriptor,
        string Detail,
        string? SingletonType,
        string? ScopedType)
    {
        public bool IsCaptiveDependency => SingletonType != null;

        // Shouldly dumps the collection contents before the custom message, and the generated record
        // ToString repeats every field there.
        public override string ToString()
        {
            return IsCaptiveDependency
                ? $"singleton {SingletonType} captured scoped {ScopedType}"
                : $"{Descriptor} -> {Detail}";
        }
    }

    private sealed record ToleratedCaptiveDependency(
        string SingletonType,
        string ScopedType,
        string Reason,
        string RevisitWhen)
    {
        public bool Matches(ValidationFailure failure)
        {
            return failure.SingletonType == SingletonType && failure.ScopedType == ScopedType;
        }
    }

    private sealed record ToleratedUnconstructibleRegistration(
        string Descriptor,
        string Reason,
        string RevisitWhen)
    {
        public bool Matches(ValidationFailure failure)
        {
            return failure.Descriptor == Descriptor;
        }
    }

    private sealed record RedisGatedRegistration(string Description, Func<ServiceDescriptor, bool> Matches)
    {
        public override string ToString() => Description;
    }

    private sealed class ScopedProbe;

    private sealed class SingletonProbeCapturingScoped(ScopedProbe scopedProbe)
    {
        public ScopedProbe ScopedProbe { get; } = scopedProbe;
    }
}
