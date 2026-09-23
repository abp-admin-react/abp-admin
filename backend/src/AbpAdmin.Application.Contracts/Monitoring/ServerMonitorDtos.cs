using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Monitoring;

public interface IServerMonitorAppService : IApplicationService
{
    /// <summary>
    /// 当前进程所在服务器的运行指标（机器/进程/CPU/内存/GC/线程/磁盘）。
    /// </summary>
    Task<ServerMonitorDto> GetAsync();
}

public class ServerMonitorDto
{
    /// <summary>机器名。</summary>
    public string MachineName { get; set; } = default!;

    /// <summary>操作系统描述（如 Microsoft Windows 10.0.26100 / Linux 5.x）。</summary>
    public string OsDescription { get; set; } = default!;

    /// <summary>操作系统架构（X64/ARM64）。</summary>
    public string OsArchitecture { get; set; } = default!;

    /// <summary>进程架构。</summary>
    public string ProcessArchitecture { get; set; } = default!;

    /// <summary>CPU 逻辑核心数。</summary>
    public int ProcessorCount { get; set; }

    /// <summary>.NET 运行时版本。</summary>
    public string DotNetVersion { get; set; } = default!;

    /// <summary>进程启动时间（UTC）。</summary>
    public DateTime ProcessStartTimeUtc { get; set; }

    /// <summary>进程已运行秒数。</summary>
    public long UptimeSeconds { get; set; }

    /// <summary>进程 CPU 占用百分比（按全部逻辑核心归一，请求内采样约 300ms）。null 表示采样失败。</summary>
    public double? ProcessCpuUsagePercent { get; set; }

    /// <summary>进程工作集（字节）。</summary>
    public long WorkingSetBytes { get; set; }

    /// <summary>进程私有内存（字节）。</summary>
    public long PrivateMemoryBytes { get; set; }

    /// <summary>GC 托管堆大小（字节）。</summary>
    public long GcHeapSizeBytes { get; set; }

    /// <summary>GC 视角的可用物理内存上限（字节；容器内为配额，非整机内存）。</summary>
    public long GcTotalMemoryLimitBytes { get; set; }

    /// <summary>是否 Server GC。</summary>
    public bool IsServerGc { get; set; }

    /// <summary>第 0/1/2 代 GC 次数。</summary>
    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    /// <summary>进程托管线程数。</summary>
    public int ThreadCount { get; set; }

    /// <summary>线程池可用工作线程数（瞬时值）。</summary>
    public int ThreadPoolAvailableWorkerThreads { get; set; }

    /// <summary>线程池工作线程最小数（配置值）。</summary>
    public int ThreadPoolMinWorkerThreads { get; set; }

    /// <summary>已就绪的磁盘分区列表。</summary>
    public List<DiskInfoDto> Disks { get; set; } = new();
}

public class DiskInfoDto
{
    /// <summary>分区名（Windows 盘符 C:\ 或 Linux 挂载点 /）。</summary>
    public string Name { get; set; } = default!;

    /// <summary>分区文件系统格式。</summary>
    public string DriveFormat { get; set; } = default!;

    /// <summary>总容量（字节）。</summary>
    public long TotalBytes { get; set; }

    /// <summary>剩余空间（字节）。</summary>
    public long FreeBytes { get; set; }
}
