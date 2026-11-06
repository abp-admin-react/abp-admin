using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using AbpAdmin.ClientErrors;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 管理资格降级计数。标签仅允许有界枚举与登记 ExitId。
/// </summary>
public interface IAdminEligibilityDegradeMetrics
{
    void Record(AdminEligibilityDegradeReason reason, string exitId);
}

/// <summary>
/// 降级计数：reason + 有界 ExitId。供测试快照与运行时指标共用。
/// </summary>
[ExposeServices(typeof(IAdminEligibilityDegradeMetrics))]
public class AdminEligibilityDegradeMetrics : IAdminEligibilityDegradeMetrics, ISingletonDependency
{
    public const string MeterName = "AbpAdmin.ClientErrors";
    public const string CounterName = "abpadmin.client_errors.admin_eligibility_degrade";

    private readonly Counter<long> _counter;
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

    public AdminEligibilityDegradeMetrics()
        : this(new Meter(MeterName))
    {
    }

    public AdminEligibilityDegradeMetrics(Meter meter)
    {
        _counter = meter.CreateCounter<long>(CounterName);
    }

    public void Record(AdminEligibilityDegradeReason reason, string exitId)
    {
        var boundedExitId = string.IsNullOrWhiteSpace(exitId) ? "unregistered" : exitId;
        _counter.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason.ToString()),
            new KeyValuePair<string, object?>("exit_id", boundedExitId));
        _counts.AddOrUpdate($"{reason}:{boundedExitId}", 1, (_, current) => current + 1);
    }

    public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counts);
}
