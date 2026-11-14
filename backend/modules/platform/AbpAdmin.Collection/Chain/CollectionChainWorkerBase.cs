using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 链上后台任务每轮读取开关、间隔和预算。仅在开关开启且配置了 Redis 时尝试取轮锁，
/// 5 秒取锁等待与轮次执行预算独立。预算到期后向轮次发送取消信号。
/// </summary>
public abstract class CollectionChainWorkerBase : Microsoft.Extensions.Hosting.BackgroundService
{
    protected ILogger Logger { get; }

    private readonly IAbpDistributedLock _distributedLock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    /// <summary>轮次计划读取失败时的等待间隔。</summary>
    private readonly TimeSpan _defaultInterval;

    /// <summary>网页未配置单轮预算时使用的部署默认值。</summary>
    private readonly TimeSpan _roundBudget;

    private bool _lockBackendAlerted;

    protected CollectionChainWorkerBase(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        TimeSpan defaultInterval,
        TimeSpan roundBudget,
        ILogger logger)
    {
        _distributedLock = distributedLock;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _defaultInterval = defaultInterval;
        _roundBudget = roundBudget;
        Logger = logger;
    }

    protected abstract string LockKey { get; }

    protected virtual int? GetRoundBudgetSeconds(ChainRuntimeSettingsDto? runtime) => runtime?.ChainRoundBudgetSeconds;

    protected abstract Task<WorkerRoundPlan> GetRoundPlanAsync(IServiceProvider scopedServices);

    protected abstract Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _defaultInterval;
        Logger.LogInformation("{Worker} 启动（默认轮间隔 {Interval}s，默认单轮预算 {Budget}s）",
            GetType().Name, _defaultInterval.TotalSeconds, _roundBudget.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var runRound = false;
            var roundBudget = _roundBudget;
            try
            {
                using (var planScope = _scopeFactory.CreateScope())
                {
                    var plan = await GetRoundPlanAsync(planScope.ServiceProvider);
                    interval = plan.Interval;
                    runRound = plan.Enabled && EnsureRedisBackedLockBackend();
                    if (runRound)
                    {
                        var runtime = await planScope.ServiceProvider.GetRequiredService<ChainRuntimeSettings>().ReadAsync();
                        if (GetRoundBudgetSeconds(runtime) is int seconds && ChainRuntimeSettings.IsValidBudget(seconds))
                        {
                            roundBudget = TimeSpan.FromSeconds(seconds);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Worker} 读取轮计划失败，本轮跳过（按开关关闭处理）", GetType().Name);
                interval = _defaultInterval;
                runRound = false;
            }

            if (runRound)
            {
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
                        budget.CancelAfter(roundBudget);
                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            await RunRoundAsync(scope.ServiceProvider, budget.Token);
                            Logger.LogDebug("{Worker} 一轮完成 {Elapsed}ms", GetType().Name, stopwatch.ElapsedMilliseconds);
                        }
                        catch (OperationCanceledException) when (budget.IsCancellationRequested
                                                                 && !stoppingToken.IsCancellationRequested)
                        {
                            Logger.LogError("[Collection] {Worker} 一轮超过执行预算 {Budget}s，本轮已取消",
                                GetType().Name, roundBudget.TotalSeconds);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{Worker} 一轮异常，等下一轮重试", GetType().Name);
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Logger.LogInformation("{Worker} 已停止", GetType().Name);
    }

    /// <summary>
    /// 仅允许有 Redis 配置的实例执行链上动作，避免各实例用互不相干的进程内锁同时运行。
    /// 连续遇到配置缺失时只告警一次，恢复后重置。
    /// </summary>
    private bool EnsureRedisBackedLockBackend()
    {
        if (IsRedisBackedLockConfigured(_configuration))
        {
            _lockBackendAlerted = false;
            return true;
        }

        if (!_lockBackendAlerted)
        {
            _lockBackendAlerted = true;
            Logger.LogError(
                "{Prefix}{Worker} 已启用但 Redis 配置不可用（Redis:IsEnabled 为 false 或 Redis:Configuration 为空），本轮跳过。请启用并配置 Redis 或关闭该任务",
                CollectionConsts.AlertPrefixLockBackend, GetType().Name);
        }

        return false;
    }

    internal static bool IsRedisBackedLockConfigured(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var redisEnabledValue = configuration["Redis:IsEnabled"];
        var redisEnabled = !string.Equals(redisEnabledValue, "false", StringComparison.OrdinalIgnoreCase);
        var redisConfiguration = configuration["Redis:Configuration"];
        return redisEnabled && !string.IsNullOrWhiteSpace(redisConfiguration);
    }

    /// <summary>
    /// 需要明确的节点日志上限。RPC 优先使用启用的密钥池，池空时使用网络设置的 RPC 地址。
    /// </summary>
    protected static async Task<IBscChainGateway?> CreateGatewayAsync(ChainGatewayFactory factory,
        CollectionDbContext dbContext, Entities.ColNetwork network, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (network.NodeLogResultCap is not int cap || cap <= 0)
        {
            logger.LogError(
                "网络 {Network} 未配置有效的节点日志上限，链上任务本轮跳过。请按实测或节点文档填写",
                network.Name);
            return null;
        }

        var hasPool = await dbContext.ChainApiKeys.AsNoTracking().AnyAsync(
            k => k.NetworkId == network.Id && k.Provider == Entities.ChainApiKeyProvider.RpcBsc && k.IsEnabled);
        if (!hasPool && string.IsNullOrEmpty(network.RpcCipher))
        {
            logger.LogError("网络 {Network} 没有启用的 RPC 密钥或备用 RPC 地址，链上任务本轮跳过", network.Name);
            return null;
        }

        try
        {
            return await factory.CreateAsync(dbContext, network, cap);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "网络 {Network} 组装链网关失败，链上任务本轮跳过", network.Name);
            return null;
        }
    }
}

/// <summary>
/// 组装 RPC 和 Etherscan 网关。每轮按进程内偏移轮换首选密钥，网关在配额或认证失败时切换。
/// RPC 池空时用网络的备用地址。Etherscan 池空时，仅在启用回退设置后使用宿主端点。
/// </summary>
public class ChainGatewayFactory
{
    private readonly System.Net.Http.HttpClient _http;
    private readonly Volo.Abp.Security.Encryption.IStringEncryptionService _encryptor;
    private readonly string _etherscanEndpoint;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;
    private readonly IServiceScopeFactory? _scopeFactory;
    private int _rpcCursor;
    private int _etherscanCursor;

    public ChainGatewayFactory(
        System.Net.Http.HttpClient http,
        Volo.Abp.Security.Encryption.IStringEncryptionService encryptor,
        string etherscanEndpoint,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        IServiceScopeFactory? scopeFactory = null)
    {
        _http = http;
        _encryptor = encryptor;
        _etherscanEndpoint = etherscanEndpoint;
        _loggerFactory = loggerFactory;
        _scopeFactory = scopeFactory;
    }

    public string? MasterKey { get; set; }

    public virtual async Task<string?> GetMasterKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(MasterKey))
        {
            return MasterKey;
        }

        using var scope = _scopeFactory?.CreateScope();
        return scope == null ? null : await scope.ServiceProvider.GetRequiredService<ChainKeyProvider>().GetAsync();
    }

    /// <summary>组网关：取启用 key（按创建序），偏移起步轮换；首选 key 刷 LastUsedAt（运维可见度，失败不影响扫链）。</summary>
    public virtual async Task<IBscChainGateway> CreateAsync(CollectionDbContext dbContext,
        Entities.ColNetwork network, int cap)
    {
        var masterKey = await GetMasterKeyAsync()
                        ?? throw new InvalidOperationException("Collection:MasterKey 未配置，无法解密扫链 key");
        var now = DateTime.UtcNow;

        var rpcUrls = new List<string>();
        var rpcKeys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id
                        && k.Provider == Entities.ChainApiKeyProvider.RpcBsc && k.IsEnabled)
            .OrderBy(k => k.CreationTime).ToListAsync();
        foreach (var k in rpcKeys)
        {
            var key = Decrypt(k.KeyCipher, masterKey);
            if (key != null)
            {
                rpcUrls.Add(AnkrBscChainGateway.AnkrBscRpcUrlBase + key);
            }
        }

        if (rpcUrls.Count == 0)
        {
            // 区分密钥池未配置与已配置但无法解密，后者需要重新录入密钥。
            if (rpcKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    $"网络 {network.Name} 无可用 RPC：key 池 {rpcKeys.Count} 条全部解密失败（疑似口令轮换/密文损坏），请重新录入 key");
            }

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
        using var settingsScope = _scopeFactory?.CreateScope();
        var runtime = settingsScope == null ? null
            : await settingsScope.ServiceProvider.GetRequiredService<ChainRuntimeSettings>().ReadAsync();
        var useLegacyExplorerFallback = runtime?.UseLegacyExplorerFallback != false;
        var etherscanKeys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id
                        && k.Provider == Entities.ChainApiKeyProvider.EtherscanV2 && k.IsEnabled)
            .OrderBy(k => k.CreationTime).ToListAsync();
        if (etherscanKeys.Count > 0)
        {
            var etherscanBase = BuildEtherscanBase(_etherscanEndpoint);
            foreach (var k in etherscanKeys)
            {
                if (Decrypt(k.KeyCipher, masterKey) is { Length: > 0 } key)
                {
                    etherscanUrls.Add(etherscanBase + key);
                }
            }

            await TouchLastUsedAsync(dbContext, etherscanKeys, _etherscanCursor, now);
        }
        else if (useLegacyExplorerFallback)
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
        if (cipher == null) return null;
        return _encryptor.Decrypt(cipher.StartsWith('\u0001') ? cipher[1..] : cipher, masterKey);
    }
}

/// <summary>轮次开关和等待间隔。<see cref="Create"/> 会限制最小间隔，避免高频请求节点。</summary>
public sealed record WorkerRoundPlan(bool Enabled, TimeSpan Interval)
{
    public static WorkerRoundPlan Create(bool enabled, TimeSpan interval)
    {
        var min = TimeSpan.FromSeconds(Settings.ChainDefaults.MinIntervalSeconds);
        return new WorkerRoundPlan(enabled, interval < min ? min : interval);
    }
}
