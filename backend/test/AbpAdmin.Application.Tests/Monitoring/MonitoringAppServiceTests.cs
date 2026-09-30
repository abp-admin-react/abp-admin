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
 * 因此 Redis IO 分支（SCAN 网络往返/取值/删除的命令交互）无法在此覆盖——分页协议本身
 * 由 CacheMonitorScanPaginationTests 以脚本化 SCAN 驱动覆盖。本文件覆盖：
 * 服务监控各指标字段可取值、CPU 采样含 300ms 窗口不抛错、
 * 缓存监控 Memory 后端的概览与拒绝枚举行为、键枚举扫描模式的 ABP 键空间锚定、
 * 以及读/删两条路径的键空间守卫（守卫跑在 Redis 可用性检查之前，Memory 环境即可钉住）。
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
        // memory 路径永不触碰命令级降级字段（InfoError 只属于 redis 概览统计）
        info.InfoError.ShouldBeNull();

        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.GetKeysAsync(null, 0, 100)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorRedisDisabled);
    }

    [Theory]
    [InlineData(null, null, "[ct]:**")]
    [InlineData("user", null, "[ct]:*user*")]
    // 用户词以结构前缀开头 → 锚定段收窄到该前缀并剥掉重复段（否则 c: 变成 [ct]:*c:*
    // 要求前缀后再次出现 "c:"，一个键都匹配不到——页面默认过滤词就是 c:）
    [InlineData("c:", null, "c:**")]
    [InlineData("c:Volo", null, "c:*Volo*")]
    [InlineData("t:3a23", null, "t:*3a23*")]
    [InlineData("t:", null, "t:**")]
    // 用户词恰为隔离前缀（页面默认过滤词形态）：与 keyPrefix 必含段天然重合
    [InlineData("AbpAdmin:", null, "[ct]:*AbpAdmin:*")]
    // 无冒号的 "c" 是普通包含词，不收窄锚定；大写 "C:" 同理（glob 区分大小写，
    // 键的实际前缀是小写 c:——用户输大写得到空表是可解释行为，钉住防"顺手修"）
    [InlineData("c", null, "[ct]:*c*")]
    [InlineData("C:", null, "[ct]:*C:*")]
    // 用户词原样进入 glob（含元字符也只影响中段、逃不出锚定；后置 IsAllowedKey 兜底）
    [InlineData("user*name", null, "[ct]:*user*name*")]
    // 隔离前缀（keyPrefix）非空：必含段拼进 glob——与 IsAllowedKey 硬边界同口径，
    // 扫描天然只命中本应用键（此前前缀只是页面过滤词，属装饰性边界）
    // 空用户词 + 必含段：收尾 **（与无必含段时空用户词的 "[ct]:**" 同一口径）
    [InlineData(null, "AbpAdmin:", "[ct]:*AbpAdmin:**")]
    [InlineData("user", "AbpAdmin:", "[ct]:*AbpAdmin:*user*")]
    [InlineData("c:Volo", "AbpAdmin:", "c:*AbpAdmin:*Volo*")]
    // 必含段 + 空用户词：收尾 **（glob 等价 *，与无必含段时空用户词的口径一致）
    [InlineData("c:", "App1:", "c:*App1:**")]
    // 用户词已含前缀 → 不重复拼（重复拼接会要求键中出现两次而漏光）
    [InlineData("AbpAdmin:custom", "AbpAdmin:", "[ct]:*AbpAdmin:custom*")]
    public void ScanPattern_Should_Be_Anchored_To_The_Abp_Keyspace(
        string? prefix, string? keyPrefix, string expected)
    {
        // 锚定语义（信息隔离，依据见 BuildScanPattern 注释）：[ct]: 字符类一次覆盖
        // 宿主键 c: 与租户键 t:——键枚举绝不允许退回无锚定的 *{prefix}* 全库扫描
        CacheMonitorAppService.BuildScanPattern(prefix, keyPrefix).ShouldBe(expected);
    }

    [Fact]
    public void Info_Text_Parsing_Extracts_Whitelist_Fields_Only()
    {
        // 真实形态片段：CRLF 行尾、节头、*_human 诱饵键、keyspace 行、无冒号空行
        var infoText = "# Server\r\nredis_version:8.6.7\r\n\r\n# Memory\r\n" +
                       "used_memory:3354368\r\nused_memory_human:3.20M\r\n" +
                       "maxmemory:0\r\nmaxmemory_human:0B\r\n" +
                       "# Keyspace\r\ndb3:keys=781,expires=2,avg_ttl=0\r\n";
        var dto = new CacheMonitorInfoDto();

        CacheMonitorAppService.ApplyInfoText(dto, infoText);

        dto.RedisVersion.ShouldBe("8.6.7");
        dto.UsedMemoryBytes.ShouldBe(3354368);
        dto.MaxMemoryBytes.ShouldBe(0); // 0 = 未限制，是有效值不得被吞
        // 诱饵键不进白名单字段之外的任何位置——DTO 也没有别的可写口
    }

    [Fact]
    public void Info_Text_Parsing_Tolerates_Null_And_Garbage()
    {
        var dto = new CacheMonitorInfoDto();
        CacheMonitorAppService.ApplyInfoText(dto, null);
        CacheMonitorAppService.ApplyInfoText(dto, "");
        CacheMonitorAppService.ApplyInfoText(dto, "no-colon-line\n:\n:bad\nused_memory:not-a-number\nredis_version:");

        dto.RedisVersion.ShouldBe(""); // 空值也算"解析到了"——与服务端原文一致
        dto.UsedMemoryBytes.ShouldBeNull(); // 非数字解析失败落 null，不抛
        dto.MaxMemoryBytes.ShouldBeNull();
    }

    [Theory]
    [InlineData("other-system:key")]
    [InlineData("AbpAdmin:DataProtection-Keys:Development")]
    [InlineData("C:foo")]
    [InlineData("")]
    public async Task Value_Read_Rejects_Keys_Outside_The_Abp_Keyspace(string key)
    {
        // 键空间守卫跑在 Redis 可用性检查之前——memory 测试环境即可钉住拒绝语义
        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.GetValueAsync(key)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorKeyNotAllowed);
    }

    [Theory]
    [InlineData("c:Volo.Abp.SettingManagement.Setting")]
    [InlineData("t:3a23d8e4, c:Volo.Abp.SettingManagement.Setting")]
    public async Task Value_Read_Passes_Keyspace_Guard_Then_Falls_To_Redis_Disabled(string key)
    {
        // 正例对照：守卫放行后才会撞"Redis 未启用"——证明上面的拒绝确实发生在守卫
        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.GetValueAsync(key)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorRedisDisabled);
    }

    [Theory]
    [InlineData("other-system:key")]
    [InlineData("AbpAdmin:DataProtection-Keys:Development")]
    [InlineData("C:foo")]
    [InlineData("")]
    public async Task Key_Delete_Rejects_Keys_Outside_The_Abp_Keyspace(string key)
    {
        // 删除是破坏性路径（模块立身之本：共享 Redis 上防误删其他系统的数据），守卫与
        // 读路径同一实现但必须有独立的红测试——AssertAbpKey 在 DeleteKeyAsync 里被重排或
        // 删除时，靠读路径测试间接担保是不够的（删除语义不允许只被间接覆盖）
        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.DeleteKeyAsync(key)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorKeyNotAllowed);
    }

    [Theory]
    [InlineData("c:Volo.Abp.SettingManagement.Setting")]
    [InlineData("t:3a23d8e4, c:Volo.Abp.SettingManagement.Setting")]
    public async Task Key_Delete_Passes_Keyspace_Guard_Then_Falls_To_Redis_Disabled(string key)
    {
        // 正例对照：守卫放行后才撞"Redis 未启用"——[Authorize(Manage)] 由测试基建
        // AddAlwaysAllowAuthorization 放行，证明删除拒绝/放行都发生在键空间守卫
        (await Should.ThrowAsync<BusinessException>(() =>
                _cacheMonitorAppService.DeleteKeyAsync(key)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorRedisDisabled);
    }
}
