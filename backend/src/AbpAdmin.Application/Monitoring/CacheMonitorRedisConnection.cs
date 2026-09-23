using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Monitoring;

/// <summary>
/// 缓存监控专用的 Redis 连接（单例、懒加载、独立多路复用器）。
/// 为什么自建：宿主的 AbpCachingStackExchangeRedisModule 走微软 AddStackExchangeRedisCache，
/// 多路复用器在 RedisCache 内部、不进 DI（已核实 rel-10.6 源码），而键级 SCAN/MEMORY/DEL
/// 需要裸 IDatabase。配置键与宿主一致（Redis:IsEnabled / Redis:Configuration），
/// 判定口径：IsEnabled 未配置视为开（与模块一致），但 Configuration 为空按未启用处理
/// （否则连的就是微软默认的 localhost:6379，监控页会给出误导结论）。
/// </summary>
public class CacheMonitorRedisConnection : ISingletonDependency
{
    private readonly IConfiguration _configuration;

    // 异步连接不能 lock/await 混用，用 SemaphoreSlim 替代双检锁里的 lock
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private volatile ConnectionMultiplexer? _multiplexer;
    private bool _connectAttempted;
    private DateTime _lastConnectFailureUtc;

    /// <summary>连接失败后的冷却间隔：期间直接复用上次失败结果，避免 Redis 宕机时每个请求都白等一次连接超时。</summary>
    private static readonly TimeSpan ConnectRetryCooldown = TimeSpan.FromSeconds(30);

    public CacheMonitorRedisConnection(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 首次连接失败的异常消息（连接成功或从未尝试过为 null）。
    /// 用于区分「未启用」（没配 Redis:Configuration）与「连接失败」两种 GetMultiplexerAsync
    /// 返回 null 的情形，避免监控页把连接失败误报成未启用。
    /// </summary>
    public string? LastConnectError { get; private set; }

    public virtual bool IsEnabled
    {
        get
        {
            var enabledRaw = _configuration["Redis:IsEnabled"];
            // TryParse：非法配置值（如 "yes"）按未启用处理，而不是抛 FormatException 打崩监控页
            if (!string.IsNullOrEmpty(enabledRaw) &&
                (!bool.TryParse(enabledRaw, out var enabled) || !enabled))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(_configuration["Redis:Configuration"]);
        }
    }

    /// <summary>
    /// 取共享多路复用器；未启用返回 null。连接失败抛原始异常（由调用方决定降级还是上抛）。
    /// 失败后按 <see cref="ConnectRetryCooldown"/> 冷却重试：完全「只试一次」会让 Redis 短暂
    /// 抖动后的监控页永久报连接失败、直到进程重启；完全不重试限流则每次请求都白等满超时。
    /// 冷却期内直接复用失败结果（不重拨），冷却过后放行下一次尝试。
    /// 连接用 ConnectAsync 异步建立：Connect 会同步阻塞调用线程（默认超时数秒），
    /// 且此前发生在 lock 内，首次调用超时会卡住所有并发请求。
    /// </summary>
    public virtual async Task<ConnectionMultiplexer?> GetMultiplexerAsync()
    {
        if (!IsEnabled)
        {
            return null;
        }

        if (_multiplexer != null)
        {
            return _multiplexer;
        }

        await _connectLock.WaitAsync();
        try
        {
            if (_multiplexer != null)
            {
                return _multiplexer;
            }

            if (_connectAttempted &&
                DateTime.UtcNow - _lastConnectFailureUtc < ConnectRetryCooldown)
            {
                return null;
            }

            _connectAttempted = true;
            try
            {
                _multiplexer = await ConnectionMultiplexer.ConnectAsync(_configuration["Redis:Configuration"]!);
                LastConnectError = null;
            }
            catch (Exception ex)
            {
                // 记下来供 GetDatabaseAsync 生成准确的错误信息；异常照抛，调用方决定降级还是上抛
                _lastConnectFailureUtc = DateTime.UtcNow;
                LastConnectError = ex.Message;
                throw;
            }

            return _multiplexer;
        }
        finally
        {
            _connectLock.Release();
        }
    }
}
