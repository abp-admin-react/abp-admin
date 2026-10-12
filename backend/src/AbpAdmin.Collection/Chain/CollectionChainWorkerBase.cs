using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 扫链/归集 worker 公共骨架（计划第 5.6 节）：开关关闭时空转退出（默认关）；
/// 每轮取 Redis 分布式锁（等待 5 秒不是租期），轮内有墙钟预算——超时取消并告警
///（Redis 锁自动续期，挂死进程不能永久占锁）；一种代币失败不挡住另一种。
/// </summary>
public abstract class CollectionChainWorkerBase : Microsoft.Extensions.Hosting.BackgroundService
{
    protected ILogger Logger { get; }

    private readonly IAbpDistributedLock _distributedLock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _roundBudget;

    protected CollectionChainWorkerBase(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        TimeSpan interval,
        TimeSpan roundBudget,
        ILogger logger)
    {
        _distributedLock = distributedLock;
        _scopeFactory = scopeFactory;
        _interval = interval;
        _roundBudget = roundBudget;
        Logger = logger;
    }

    protected abstract string LockKey { get; }

    /// <summary>开关（Collection:Chain:ScanEnabled / SweepEnabled）：默认关——关闭时每轮直接睡间隔，不取锁不碰链。</summary>
    protected abstract bool Enabled { get; }

    /// <summary>一轮：scope 内解析依赖，逐代币执行 RunTokenAsync。</summary>
    protected abstract Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("{Worker} 启动（轮间隔 {Interval}s，墙钟预算 {Budget}s）",
            GetType().Name, _interval.TotalSeconds, _roundBudget.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!Enabled)
            {
                // 开关默认关（计划第 5.6 节）：不取锁、不碰链、不打 RPC——纯睡眠等待运维开启
                try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await using var lockHandle =
                    await _distributedLock.TryAcquireAsync(LockKey, timeout: TimeSpan.FromSeconds(5),
                        stoppingToken);
                if (lockHandle == null)
                {
                    Logger.LogDebug("{Worker} 未拿到锁 {Key}，等下一轮", GetType().Name, LockKey);
                }
                else
                {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    budget.CancelAfter(_roundBudget);
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await RunRoundAsync(scope.ServiceProvider, budget.Token);
                    }
                    catch (OperationCanceledException) when (budget.IsCancellationRequested
                                                             && !stoppingToken.IsCancellationRequested)
                    {
                        // 墙钟预算耗尽：锁在 using 释放（自动续期终止），告警提示
                        Logger.LogError("[Collection] {Worker} 一轮超过墙钟预算 {Budget}s，已取消并释放锁",
                            GetType().Name, _roundBudget.TotalSeconds);
                    }

                    stopwatch.Stop();
                    Logger.LogDebug("{Worker} 一轮完成 {Elapsed}ms", GetType().Name, stopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一轮失败不挡下一轮（对齐「单币失败不挡另一种」的同族纪律）
                Logger.LogError(ex, "{Worker} 一轮异常，等下一轮重试", GetType().Name);
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Logger.LogInformation("{Worker} 已停止", GetType().Name);
    }

    /// <summary>解析链网关（解密 RPC 密文；cap 未配置返回 null 并由调用方跳过——不设缺省上限不开扫链）。</summary>
    protected static IBscChainGateway? CreateGateway(ChainGatewayFactory factory, CollectionDbContext dbContext,
        Entities.ColNetwork network, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (network.NodeLogResultCap is not int cap || cap <= 0)
        {
            logger.LogError(
                "网络 {Network} 未配置 NodeLogResultCap（实测或文档上限），按计划拒绝打开扫链；请先完成测量并回填",
                network.Name);
            return null;
        }

        if (string.IsNullOrEmpty(network.RpcCipher))
        {
            logger.LogError("网络 {Network} 未配置 RPC（RpcCipher 为空），扫链无法进行", network.Name);
            return null;
        }

        return factory.Create(network.RpcCipher, cap);
    }
}

/// <summary>网关工厂：解密 RPC 连接串（含项目密钥）并组装网关。HttpClient 由宿主 IHttpClientFactory 提供。</summary>
public class ChainGatewayFactory
{
    private readonly System.Net.Http.HttpClient _http;
    private readonly Volo.Abp.Security.Encryption.IStringEncryptionService _encryptor;
    private readonly string _etherscanEndpoint;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;

    public ChainGatewayFactory(
        System.Net.Http.HttpClient http,
        Volo.Abp.Security.Encryption.IStringEncryptionService encryptor,
        string etherscanEndpoint,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        _http = http;
        _encryptor = encryptor;
        _etherscanEndpoint = etherscanEndpoint;
        _loggerFactory = loggerFactory;
    }

    public string? MasterKey { get; set; }

    public IBscChainGateway Create(string rpcCipher, int cap)
    {
        var url = _encryptor.Decrypt(
            rpcCipher.StartsWith('\u0001') ? rpcCipher[1..] : rpcCipher, MasterKey
            ?? throw new InvalidOperationException("Collection:MasterKey 未配置，无法解密 RPC 连接串"));
        return new AnkrBscChainGateway(
            _http, url, cap, _etherscanEndpoint,
            _loggerFactory.CreateLogger<AnkrBscChainGateway>());
    }
}
