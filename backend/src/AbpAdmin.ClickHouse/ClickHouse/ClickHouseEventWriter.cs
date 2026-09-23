using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ClickHouse.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.ClickHouse;

/// <summary>
/// 采集事件缓冲批量写入器(后台常驻,IHostedService 驱动)。
///
/// 工作方式:业务侧 <see cref="Enqueue"/> 零阻塞入队内存缓冲(Channel 有界,容量
/// <see cref="AbpAdminClickHouseOptions.BufferCapacity"/>,满载 DropOldest 丢最旧);
/// 后台每 <see cref="AbpAdminClickHouseOptions.FlushInterval"/> 周期冲刷一次,单个周期内
/// 把缓冲清空为止(批次间检查取消令牌),因此稳态吞吐不受"间隔×单批上限"约束;
/// 每批走官方 <c>InsertBinaryAsync</c>(RowBinary 流式,列序见 <see cref="CollectedEventsTable"/>)。
///
/// 交付语义:at-least-once。写入失败整批退回缓冲等待重试,"结果未知"的失败
/// (超时但服务端可能已提交)重放会产生重复行;退回批排在新事件之后,顺序不保证。
/// 取消(停机/调用方令牌)时在途批同样退回缓冲,不丢数据。
/// 对可重放的分析数据可接受;需要精确一次时应改用带幂等键的表(如 ReplacingMergeTree + insert_id 列)。
///
/// 故障姿态:
/// - CH 不可用:已入队事件不丢(退回重试),仅缓冲溢出(DropOldest)丢最旧——丢弃发生在入队侧、
///   无计数上报,属已知取舍,容量按峰值入队速率×故障时长×单条字节预算
///   (<see cref="AbpAdminClickHouseOptions.BufferCapacity"/> 注释);
/// - 建表缺失(宿主启动窗口 CH 未就绪):由写入失败路径按冷却期(<see cref="DdlRetryCooldown"/>)自愈
///   重跑幂等 DDL,CH 恢复后自动收敛,无需重启宿主;
/// - 停机:预算内(<see cref="_shutdownFlushBudget"/>)循环冲到清空;预算耗尽或写失败退出时,
///   残余条数告警后随进程终止丢失。
/// </summary>
public class ClickHouseEventWriter : BackgroundService, IClickHouseEventWriter
{
    /// <summary>建表自愈重试冷却期:CH 宕机期间避免每轮冲刷都白打一次 DDL 请求</summary>
    private static readonly TimeSpan DdlRetryCooldown = TimeSpan.FromMinutes(1);

    /// <summary>停机冲刷默认预算:超时放弃残余事件。进程真正被强杀的时限是 HostOptions.ShutdownTimeout(默认 30s),本预算必须小于它</summary>
    private static readonly TimeSpan DefaultShutdownFlushBudget = TimeSpan.FromSeconds(10);

    /// <summary>FlushInterval 非法(非正)时的回退默认值,与 <see cref="AbpAdminClickHouseOptions.FlushInterval"/> 默认一致</summary>
    private static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(5);

    private readonly IClickHouseClient _client;
    private readonly string _tableName;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _shutdownFlushBudget;
    private readonly ILogger<ClickHouseEventWriter> _logger;
    private readonly Channel<CollectedEvent> _channel;

    /// <summary>
    /// 冲刷门闩。FlushAsync 是公开接口(测试/运维手动冲刷),与后台循环并发读同一个 Channel
    /// 会破坏 SingleReader=true 契约(SingleReader 下 Channel 读路径无同步,并发读结果未定义)。
    /// 所有冲刷入口经此闩串行化,拿不到闩立即返回 0(不排队,手动冲刷不阻塞调用方)。
    /// </summary>
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    /// <summary>
    /// 上次自愈建表尝试时刻(Environment.TickCount64 毫秒)。初值取 long 的一半负刻度,
    /// 保证"距现在已远超冷却期"——否则宿主在系统开机 60s 内启动时,首次自愈会被
    /// 初值 0 与 TickCount64(开机起算)的差值误抑制一个冷却周期。
    /// </summary>
    private long _lastDdlAttemptTicks = long.MinValue >> 1;

    /// <summary>冲刷连续失败标记:用于日志降噪(首次 Error、持续失败 Warning、恢复 Info)</summary>
    private bool _flushFailing;

    /// <summary>
    /// 构造写入器。表名在此处做白名单校验(配置来源,但所有拼接 SQL 的路径都必须过校验);
    /// 非法的容量/批量/间隔配置在使用点收敛到安全值,避免宿主启动崩溃或静默停摆。
    /// </summary>
    /// <param name="options">配置(来自 <see cref="AbpAdminClickHouseOptions.SectionName"/> 配置节)。</param>
    /// <param name="client">DI 单例客户端,生命周期由容器管理,本类不释放。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="shutdownFlushBudget">停机冲刷预算(测试注入口);默认 <see cref="DefaultShutdownFlushBudget"/>。</param>
    /// <exception cref="InvalidOperationException">配置表名未通过白名单校验。</exception>
    public ClickHouseEventWriter(
        AbpAdminClickHouseOptions options,
        IClickHouseClient client,
        ILogger<ClickHouseEventWriter> logger,
        TimeSpan? shutdownFlushBudget = null)
    {
        _client = client;
        _logger = logger;
        // 配置值在使用点收敛防御:BufferCapacity<=0 会让 BoundedChannel 构造抛异常(宿主启动失败)、
        // MaxBatchSize<=0 会永久读不出批(静默停摆)、非正间隔会让 PeriodicTimer 抛异常
        _tableName = options.CollectedEventsTable;
        CollectedEventsTable.EnsureValidTableName(_tableName);
        _flushInterval = options.FlushInterval <= TimeSpan.Zero ? DefaultFlushInterval : options.FlushInterval;
        _maxBatchSize = Math.Max(1, options.MaxBatchSize);
        _shutdownFlushBudget = shutdownFlushBudget ?? DefaultShutdownFlushBudget;
        _channel = Channel.CreateBounded<CollectedEvent>(new BoundedChannelOptions(Math.Max(1, options.BufferCapacity))
        {
            // DropOldest:TryWrite 永不阻塞,溢出时挤掉队头最旧事件;
            // 代价是丢弃无计数、无事件通知,靠容量预算兜底
            FullMode = BoundedChannelFullMode.DropOldest,
            // 单读者契约:所有读路径必须持有 <see cref="_flushGate"/>(见其注释)
            SingleReader = true,
        });
    }

    /// <inheritdoc />
    public void Enqueue(CollectedEvent collectedEvent)
    {
        // DropOldest 下写入永不阻塞:本事件必入队,溢出代价是挤掉队头最旧事件
        _channel.Writer.TryWrite(collectedEvent);
    }

    /// <summary>
    /// 停机冲刷的框架级兜底。.NET 10 的 BackgroundService.StartAsync 用
    /// <c>Task.Run(factory, _stoppingCts.Token)</c> 启动 ExecuteAsync——取消若发生在线程池
    /// 开始执行委托之前，委托<b>不会运行</b>（任务直接 Canceled），ExecuteAsync 里
    /// "finally 必达"的停机冲刷随之被整体跳过，且 StopAsync 的 SuppressThrowing 会吞掉
    /// Canceled 异常——快速 Start→Stop（测试/宿主秒停/重启）时缓冲事件静默丢失、无任何日志。
    /// 这里在后台任务等待结束后<b>再补一次</b>冲刷：正常路径（ExecuteAsync 已运行）此刻缓冲
    /// 已清，补刷为无害空转；未运行路径则由这里兜住。FlushOnShutdownAsync 自带门闩与预算，
    /// 重复调用安全。
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushOnShutdownAsync();
    }

    /// <inheritdoc />
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        // WaitAsync(0):非阻塞抢闩。抢不到说明后台循环正在冲刷,本批会由它带走,直接返回 0。
        // 调用方令牌已取消时此处直接抛 OCE(约定语义,见接口注释)
        if (!await _flushGate.WaitAsync(0, cancellationToken))
        {
            return 0;
        }

        try
        {
            return await FlushOnceAsync(cancellationToken);
        }
        finally
        {
            TryReleaseGate();
        }
    }

    /// <summary>
    /// 后台主循环:按 <see cref="_flushInterval"/> 周期唤醒,每个周期在门闩内把缓冲清空为止。
    /// 停机令牌在任何位置触发(定时器等待/抢闩/在途批写入)都统一汇入
    /// <see cref="FlushOnShutdownAsync"/>(finally 保证必达),不让 OCE 逃出本方法——
    /// 否则"停机冲刷"恰好在最需要它的场景(停止时有在途批)被静默跳过。
    /// </summary>
    /// <param name="stoppingToken">宿主停机令牌(StopAsync 触发)。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_flushInterval);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    // 单周期内循环冲到清空:高频入队时本周期顺带消化积压,不被"间隔×单批"的吞吐卡死;
                    // 抢不到门闩(手动冲刷正在进行)则跳过本周期,下个周期再来
                    if (!await _flushGate.WaitAsync(0, stoppingToken))
                    {
                        continue;
                    }

                    try
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            // 返回值 < 单批上限 => 本轮没读满,缓冲已空,本周期结束
                            if (await FlushOnceAsync(stoppingToken) < _maxBatchSize)
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        TryReleaseGate();
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机令牌在抢闩或批次写入中途触发:在途批已退回缓冲(见 FlushOnceAsync 取消分支),
                    // 走出本循环由 finally 接手停机冲刷
                    break;
                }
            }
        }
        finally
        {
            await FlushOnShutdownAsync();
        }
    }

    /// <summary>
    /// 停机冲刷:预算(<see cref="_shutdownFlushBudget"/>)内循环冲到清空。
    /// 预算通过令牌传入每一批的 InsertBinaryAsync(驱动可中断单批请求);
    /// 预算耗尽或写入失败退出时,记录缓冲残余条数——残余随进程终止丢失,
    /// 是有别于 DropOldest 的第二条已知损耗路径,必须有告警留痕。
    /// </summary>
    private async Task FlushOnShutdownAsync()
    {
        // 后台循环已退出,正常情况下门闩必然空闲;WaitAsync(0) 仅作防御
        if (!await _flushGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            using var budget = new CancellationTokenSource(_shutdownFlushBudget);
            while (true)
            {
                int written;
                try
                {
                    written = await FlushOnceAsync(budget.Token);
                }
                catch (OperationCanceledException)
                {
                    // 预算耗尽:在途批已被退回缓冲(FlushOnceAsync 取消分支),此刻 Count 是准确残余
                    _logger.LogWarning(
                        "停机冲刷预算 {Budget} 耗尽,缓冲残余 {Remaining} 条事件随进程终止丢失",
                        _shutdownFlushBudget, _channel.Reader.Count);
                    return;
                }

                if (written < _maxBatchSize)
                {
                    break;
                }
            }

            if (_channel.Reader.Count > 0)
            {
                // 非取消退出(批写入失败退回)同样可能残余,统一告警不静默
                _logger.LogWarning(
                    "停机冲刷结束仍残余 {Remaining} 条事件（写入失败），随进程终止丢失",
                    _channel.Reader.Count);
            }
        }
        finally
        {
            TryReleaseGate();
        }
    }

    /// <summary>
    /// 单次冲刷:取一批(≤<see cref="_maxBatchSize"/>)写入 CH。
    /// 成功返回写入行数(调用方据此判断缓冲是否清空);失败退回整批并返回 0。
    /// 取消令牌触发时:在途批先退回缓冲再抛 OCE(不丢数据);调用方必须持有 <see cref="_flushGate"/>。
    /// </summary>
    private async Task<int> FlushOnceAsync(CancellationToken cancellationToken)
    {
        // 空批零分配:TryPeek 快速探测,避免 CH 故障期每个周期白付一批 List 容量
        if (!_channel.Reader.TryPeek(out _))
        {
            return 0;
        }

        var batch = new List<CollectedEvent>(_maxBatchSize);
        while (batch.Count < _maxBatchSize && _channel.Reader.TryRead(out var collectedEvent))
        {
            batch.Add(collectedEvent);
        }

        try
        {
            // 令牌传给驱动:单批 HTTP 请求可被取消(停机预算/调用方取消都依赖这一点)
            await _client.InsertBinaryAsync(
                _tableName,
                CollectedEventsTable.Columns,
                batch.Select(CollectedEventsTable.ToRow),
                cancellationToken: cancellationToken);
            LogFlushRecovered(batch.Count);
            return batch.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 取消路径:批未落库,先退回缓冲保住数据再上抛——
            // 停机冲刷的下一轮预算/宿主重启后的后台循环都能接着消化
            foreach (var collectedEvent in batch)
            {
                _channel.Writer.TryWrite(collectedEvent);
            }

            throw;
        }
        catch (Exception ex)
        {
            LogFlushFailure(ex, batch.Count);
            // 写失败可能是"表不存在"(启动窗口建表失败/表被误删),按冷却期自愈重跑幂等 DDL;
            // 令牌透传:停机预算同样约束 DDL,不会挂驱动默认超时(约 2 分钟)击穿预算
            await TryEnsureTableAsync(cancellationToken);
            // 失败退回(at-least-once,见类注释):退回批排到队尾,顺序不保证;
            // DropOldest 下退回必成功,代价是缓冲近满时挤掉更旧的未尝试事件
            foreach (var collectedEvent in batch)
            {
                _channel.Writer.TryWrite(collectedEvent);
            }

            return 0;
        }
    }

    /// <summary>
    /// DDL 自愈:启动窗口建表失败(CH 未就绪)或表被误删时,仅在启动时建一次表会导致
    /// CH 恢复后写入器永远报表不存在、缓冲被 DropOldest 静默丢光。
    /// 这里按 <see cref="DdlRetryCooldown"/> 冷却期重跑幂等 CREATE IF NOT EXISTS 收敛:
    /// 每次冲刷失败至多多打一次 DDL,CH 仍不可达时 DDL 也失败并计入下一轮冷却。
    /// </summary>
    /// <param name="cancellationToken">调用方令牌(停机预算/手动冲刷令牌),约束 DDL 请求时长。</param>
    private async Task TryEnsureTableAsync(CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDdlAttemptTicks);
        if (now - last < DdlRetryCooldown.TotalMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDdlAttemptTicks, now);
        try
        {
            await _client.ExecuteNonQueryAsync(
                CollectedEventsTable.BuildCreateTableSql(_tableName),
                cancellationToken: cancellationToken);
            _logger.LogInformation("ClickHouse 采集事件表自愈建表成功: {Table}", _tableName);
        }
        catch (Exception ddlEx)
        {
            // 只降级为 Warning:CH 不可达时 DDL 失败与写入失败同源,上一条写入失败日志已带完整异常
            _logger.LogWarning(ddlEx, "ClickHouse 自愈建表失败,{Cooldown} 后随下次写入失败重试: {Table}",
                DdlRetryCooldown, _tableName);
        }
    }

    /// <summary>
    /// 写失败日志(降噪):首次失败打 Error 带完整异常;持续失败降为 Warning,
    /// 避免 CH 长时间故障时每个周期一条 Error 刷屏(且会被 ES 日志链路二次放大)。
    /// </summary>
    private void LogFlushFailure(Exception ex, int count)
    {
        if (_flushFailing)
        {
            _logger.LogWarning(ex, "ClickHouse 批量写入仍未恢复（{Count} 行已退回缓冲，等待下轮重试）", count);
        }
        else
        {
            _flushFailing = true;
            _logger.LogError(ex, "ClickHouse 批量写入失败（{Count} 行），事件已退回缓冲等待重试", count);
        }
    }

    /// <summary>从失败恢复后的第一条 Info 日志(仅在失败状态下触发一次)</summary>
    private void LogFlushRecovered(int count)
    {
        if (_flushFailing)
        {
            _flushFailing = false;
            _logger.LogInformation("ClickHouse 写入已恢复，本批 {Count} 行落库", count);
        }
    }

    /// <summary>
    /// 释放门闩。宿主停机超时(ShutdownTimeout)时会先于后台方法结束触发容器 Dispose,
    /// 此时 Release 会撞上已释放的信号量——吞掉 ObjectDisposedException,
    /// 避免把可预期的停机竞态变成宿主退出码 1。
    /// </summary>
    private void TryReleaseGate()
    {
        try
        {
            _flushGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // 宿主已 Dispose 本写入器,无资源可还,静默即可
        }
    }

    /// <summary>
    /// 释放门闩(自建资源;客户端连接池由容器负责)。
    /// 注意:这里不能 Release——门闩在 StopAsync 正常结束后必然处于平衡态,
    /// 盲目 Release 会抛 SemaphoreFullException;并发冲刷未结束时被 Dispose 的窗口
    /// 由 TryReleaseGate 的 ODE 防御兜底。
    /// </summary>
    public override void Dispose()
    {
        _flushGate.Dispose();
        base.Dispose();
    }
}
