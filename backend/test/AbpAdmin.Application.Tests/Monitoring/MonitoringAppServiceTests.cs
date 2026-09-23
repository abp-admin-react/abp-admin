using System;
using System.Threading.Tasks;
using AbpAdmin.Monitoring;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Monitoring;

/* 服务监控 / 缓存监控测试。
 * 测试环境没有 Redis:Configuration（按未启用降级，见 CacheMonitorRedisConnection 判定口径），
 * 因此 Redis 分支（SCAN/取值/删除）无法在单测里覆盖；覆盖的是：
 * 服务监控各指标字段可取值、CPU 采样含 300ms 窗口不抛错、
 * 缓存监控 Memory 后端的概览与拒绝枚举行为。
 */
public abstract class MonitoringAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IServerMonitorAppService _serverMonitorAppService;
    private readonly ICacheMonitorAppService _cacheMonitorAppService;

    protected MonitoringAppServiceTests()
    {
        _serverMonitorAppService = GetRequiredService<IServerMonitorAppService>();
        _cacheMonitorAppService = GetRequiredService<ICacheMonitorAppService>();
    }

    [Fact]
    public async Task ServerMonitor_Should_Return_Metrics()
    {
        var dto = await _serverMonitorAppService.GetAsync();

        dto.MachineName.ShouldNotBeNullOrWhiteSpace();
        dto.ProcessorCount.ShouldBeGreaterThan(0);
        dto.UptimeSeconds.ShouldBeGreaterThanOrEqualTo(0);
        dto.WorkingSetBytes.ShouldBeGreaterThan(0);
        dto.GcHeapSizeBytes.ShouldBeGreaterThanOrEqualTo(0);
        dto.Gen0Collections.ShouldBeGreaterThanOrEqualTo(0);
        dto.ThreadCount.ShouldBeGreaterThan(0);
        dto.Disks.ShouldNotBeEmpty(); // 测试机至少有一个就绪分区
        dto.Disks.ShouldAllBe(d => d.TotalBytes >= d.FreeBytes);
    }

    [Fact]
    public async Task CacheMonitor_MemoryBackend_Should_Report_And_Reject_Enumeration()
    {
        var info = await _cacheMonitorAppService.GetInfoAsync();

        // 测试环境无 Redis:Configuration → Memory 后端；KeyPrefix 默认为空（结构前缀 c:/t:）
        info.Backend.ShouldBe("memory");
        info.KeyPrefix.ShouldNotBeNull();
        info.ConnectionError.ShouldBeNull();

        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.GetKeysAsync(null, 0, 100)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorRedisDisabled);
    }
}
