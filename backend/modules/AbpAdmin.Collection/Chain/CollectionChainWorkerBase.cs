using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    /// <summary>
    /// 解析链网关（cap 未配置返回 null 并由调用方跳过——不设缺省上限不开扫链）。
    /// RPC 来源双轨：扫链 key 池（ColChainApiKeys，管理页维护）优先，池空回退网络行 RpcCipher。
    /// </summary>
    protected static async Task<IBscChainGateway?> CreateGatewayAsync(ChainGatewayFactory factory,
        CollectionDbContext dbContext, Entities.ColNetwork network, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (network.NodeLogResultCap is not int cap || cap <= 0)
        {
            logger.LogError(
                "网络 {Network} 未配置 NodeLogResultCap（实测或文档上限），按计划拒绝打开扫链；请先完成测量并回填",
                network.Name);
            return null;
        }

        var hasPool = await dbContext.ChainApiKeys.AsNoTracking().AnyAsync(
            k => k.NetworkId == network.Id && k.Provider == Entities.ChainApiKeyProvider.RpcBsc && k.IsEnabled);
        if (!hasPool && string.IsNullOrEmpty(network.RpcCipher))
        {
            logger.LogError("网络 {Network} 未配置扫链 key（key 池为空且 RpcCipher 为空），扫链无法进行", network.Name);
            return null;
        }

        try
        {
            return await factory.CreateAsync(dbContext, network, cap);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "网络 {Network} 组装链网关失败（key 解密失败或池为空）", network.Name);
            return null;
        }
    }
}

/// <summary>
/// 网关工厂：组装扫链网关，RPC/Etherscan 双通道都支持「key 池优先、旧配置回退」——
/// 池 = ColChainApiKeys（同服务商多把，管理页增删启停），网关内遇限流/认证错误自动换下一把；
/// 池空回退网络行 RpcCipher（完整 URL 含 key）/ 宿主配置的 Etherscan 端点（行为与旧版一致）。
/// 跨轮轮换：进程内递增偏移，每轮从不同 key 起步（Ankr/Etherscan 免费档限流按 key 计）。
/// HttpClient 由宿主 IHttpClientFactory 提供。
/// </summary>
public class ChainGatewayFactory
{
    private readonly System.Net.Http.HttpClient _http;
    private readonly Volo.Abp.Security.Encryption.IStringEncryptionService _encryptor;
    private readonly string _etherscanEndpoint;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;
    private int _rpcCursor;
    private int _etherscanCursor;

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

    /// <summary>组网关：取启用 key（按创建序），偏移起步轮换；首选 key 刷 LastUsedAt（运维可见度，失败不影响扫链）。</summary>
    public virtual async Task<IBscChainGateway> CreateAsync(CollectionDbContext dbContext,
        Entities.ColNetwork network, int cap)
    {
        var masterKey = MasterKey
                        ?? throw new InvalidOperationException("Collection:MasterKey 未配置，无法解密扫链 key");
        var now = DateTime.UtcNow;

        var rpcUrls = new List<string>();
        var rpcKeys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id
                        && k.Provider == Entities.ChainApiKeyProvider.RpcBsc && k.IsEnabled)
            .OrderBy(k => k.CreationTime).ToListAsync();
        foreach (var k in rpcKeys)
        {
            // 先解密再拼接（OCR R2 修复）：此前 base + Decrypt() 把 null 当空串拼出裸基座 URL，
            // 坏 key 静默混进池、下方「全部解密失败」分支永不可达
            var key = Decrypt(k.KeyCipher, masterKey);
            if (key != null)
            {
                rpcUrls.Add(AnkrBscChainGateway.AnkrBscRpcUrlBase + key);
            }
        }

        if (rpcUrls.Count == 0)
        {
            // key 池有条目但全部解密失败时，报错必须指向「解密失败」而不是「池为空」——
            // 口令轮换/密文损坏是运维动作问题，与「还没录入 key」的排障方向完全不同
            if (rpcKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    $"网络 {network.Name} 无可用 RPC：key 池 {rpcKeys.Count} 条全部解密失败（疑似口令轮换/密文损坏），请重新录入 key");
            }

            // 兼容旧配置：key 池为空时回退网络行 RpcCipher（完整 URL，行为与多 key 之前一致）
            if (string.IsNullOrEmpty(network.RpcCipher))
            {
                throw new InvalidOperationException(
                    $"网络 {network.Name} 无可用 RPC：key 池为空且 RpcCipher 为空");
            }

            var legacyUrl = Decrypt(network.RpcCipher, masterKey);
            if (legacyUrl != null)
            {
                rpcUrls.Add(legacyUrl);
            }
            else
            {
                throw new InvalidOperationException(
                    $"网络 {network.Name} 无可用 RPC：RpcCipher 解密失败（疑似口令轮换/密文损坏）");
            }
        }
        else
        {
            await TouchLastUsedAsync(dbContext, rpcKeys, _rpcCursor, now);
        }

        var etherscanUrls = new List<string>();
        var etherscanKeys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id
                        && k.Provider == Entities.ChainApiKeyProvider.EtherscanV2 && k.IsEnabled)
            .OrderBy(k => k.CreationTime).ToListAsync();
        if (etherscanKeys.Count > 0)
        {
            var etherscanBase = BuildEtherscanBase(_etherscanEndpoint);
            foreach (var k in etherscanKeys)
            {
                etherscanUrls.Add(etherscanBase + Decrypt(k.KeyCipher, masterKey));
            }

            await TouchLastUsedAsync(dbContext, etherscanKeys, _etherscanCursor, now);
        }
        else
        {
            // 回退宿主配置端点（secrets 里的完整 URL，可能自带 key）
            etherscanUrls.Add(_etherscanEndpoint);
        }

        return new AnkrBscChainGateway(
            _http, rpcUrls.ToArray(), etherscanUrls.ToArray(), cap,
            System.Threading.Interlocked.Increment(ref _rpcCursor) - 1,
            System.Threading.Interlocked.Increment(ref _etherscanCursor) - 1,
            _loggerFactory.CreateLogger<AnkrBscChainGateway>());
    }

    /// <summary>把本轮起步 key 标个使用时间（管理页展示；AsNoTracking 加载 → ExecuteUpdate 无状态冲突）。</summary>
    private static async Task TouchLastUsedAsync(CollectionDbContext dbContext,
        List<Entities.ColChainApiKey> keys, int offset, DateTime now)
    {
        try
        {
            var chosen = keys[((offset % keys.Count) + keys.Count) % keys.Count];
            await dbContext.ChainApiKeys
                .Where(k => k.Id == chosen.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));
        }
        catch
        {
            // 展示用时间戳，任何失败都不挡扫链
        }
    }

    /// <summary>配置端点截取到 apikey= 为止的基座（chainid 等参数跟配置走）；没有该参数则补上。</summary>
    internal static string BuildEtherscanBase(string endpoint)
    {
        var param = AnkrBscChainGateway.EtherscanApiKeyParam;
        var idx = endpoint.IndexOf(param, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return endpoint[..(idx + param.Length)];
        }

        return endpoint + (endpoint.Contains('?') ? "&" : "?") + param;
    }

    private string? Decrypt(string? cipher, string masterKey)
    {
        // 调用方 CreateGatewayAsync 已确保池或 RpcCipher 至少一个非空；null 时返回 null（仅日志排查用途）
        if (cipher == null) return null;
        return _encryptor.Decrypt(cipher.StartsWith('\u0001') ? cipher[1..] : cipher, masterKey);
    }
}
