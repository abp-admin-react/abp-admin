using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.Monitoring;

/// <summary>
/// 服务监控（对标 RuoYi「服务监控」+「连接池监视」的进程侧部分）。
/// 只读、无实体、无表：全部数据来自 System.Diagnostics / GC / DriveInfo，
/// 不引入指标采集依赖。系统级信息仅 Host 可见（租户不应看到宿主基础设施）。
/// CPU 采样会占用请求约 300ms，属有意为之（单次差分最短窗口）。
/// </summary>
[Authorize(AbpAdminPermissions.ServerMonitor.Default)]
public class ServerMonitorAppService : AbpAdminAppService, IServerMonitorAppService
{
    public virtual async Task<ServerMonitorDto> GetAsync()
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        ThreadPool.GetMinThreads(out var minWorkerThreads, out _);
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out _);

        var dto = new ServerMonitorDto
        {
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = Environment.Version.ToString(),
            ProcessStartTimeUtc = process.StartTime.ToUniversalTime(),
            UptimeSeconds = (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds,
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            GcHeapSizeBytes = gcInfo.HeapSizeBytes,
            GcTotalMemoryLimitBytes = gcInfo.TotalAvailableMemoryBytes,
            // IsServerGC 不在 GCMemoryInfo 上（那是 GCSettings 的属性）
            IsServerGc = System.Runtime.GCSettings.IsServerGC,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            ThreadCount = process.Threads.Count,
            ThreadPoolAvailableWorkerThreads = availableWorkerThreads,
            ThreadPoolMinWorkerThreads = minWorkerThreads,
            Disks = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new DiskInfoDto
                {
                    Name = d.Name,
                    DriveFormat = d.DriveFormat,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace
                })
                .ToList()
        };

        dto.ProcessCpuUsagePercent = await SampleProcessCpuUsageAsync(
            process, Environment.ProcessorCount, Logger);
        return dto;
    }

    /// <summary>
    /// 用 TotalProcessorTime 差分估算进程 CPU 占用：
    /// 占用% = ΔCPU时间 / (窗口时长 × 逻辑核心数) × 100，按全部核心归一（与任务管理器口径一致）。
    /// 采样窗口约 300ms（单次差分最短窗口），用 Task.Delay 异步等待而不是 Thread.Sleep
    /// 同步钉死请求线程。采样失败（如 Linux 容器的 PlatformNotSupportedException）记 Warning 后返回 null。
    /// </summary>
    protected static async Task<double?> SampleProcessCpuUsageAsync(
        Process process, int processorCount, ILogger logger)
    {
        if (processorCount <= 0)
        {
            return null;
        }

        try
        {
            var first = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            await Task.Delay(300);
            var second = process.TotalProcessorTime;
            watch.Stop();

            var cpuUsedMs = (second - first).TotalMilliseconds;
            var totalMs = watch.ElapsedMilliseconds * processorCount;
            if (totalMs <= 0)
            {
                return null;
            }

            return Math.Round(cpuUsedMs / totalMs * 100, 1);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Process CPU usage sampling failed.");
            return null;
        }
    }
}
