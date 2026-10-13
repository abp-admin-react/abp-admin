using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using File = EasyAbp.FileManagement.Files.File;
using FileType = EasyAbp.FileManagement.Files.FileType;

namespace AbpAdmin.Files;

/// <summary>
/// 缩略图回填（T3.1）：扫 State == Pending 的映射行与「还没有映射行的图片文件」，批量入队。
/// T3.3 的 ScheduledJobHandler（ThumbnailBackfillJobHandler）调本管理器做周期回填；
/// 也可以被管理端手动触发。
///
/// 注意 Failed 不在自动回填范围：失败是永久留痕（如像素超限），自动重试会形成无限循环，
/// 人工把状态改回 Pending 即会被下一轮回填拾起。
/// </summary>
public class FileThumbnailBackfillManager : DomainService
{
    private const int PageSize = 500;

    /// <summary>单轮入队上限：历史库很大时防止一轮回填瞬间压入数万个后台作业，超出部分留给调度器下一轮。</summary>
    private const int MaxEnqueuedPerRun = PageSize * 10;

    private readonly IRepository<FileThumbnail, Guid> _thumbnailRepository;
    private readonly EasyAbp.FileManagement.Files.IFileRepository _fileRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly AbpAdminImagingOptions _options;

    public FileThumbnailBackfillManager(
        IRepository<FileThumbnail, Guid> thumbnailRepository,
        EasyAbp.FileManagement.Files.IFileRepository fileRepository,
        IBackgroundJobManager backgroundJobManager,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter,
        IOptions<AbpAdminImagingOptions> options)
    {
        _thumbnailRepository = thumbnailRepository;
        _fileRepository = fileRepository;
        _backgroundJobManager = backgroundJobManager;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
        _options = options.Value;
    }

    /// <summary>返回本次入队的作业数。调用方必须已开 UoW（T3.3 的 worker 按 00-overview 6.5 自己开）。</summary>
    public virtual async Task<int> EnqueuePendingAndMissingAsync(CancellationToken cancellationToken = default)
    {
        // 跨租户全量扫描：回填是运维动作，不走当前请求的租户过滤
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var enqueued = 0;

            // 1) 已有行但状态 Pending：直接重新入队。
            // 单轮只入队一批：入队不改状态（作业执行前仍是 Pending），再查还是同一批 → 死循环；
            // 后续状态推进由作业自身完成（Done/Unsupported/Failed），下一轮调度自然衔接。
            // 查询即按 Id 排序 + Take(MaxEnqueuedPerRun)：历史库 Pending 堆积数万行时
            // 不再整体物化进内存，按 Id 排序保证多轮之间分批稳定（见代码审查 round1）。
            var pendingQueryable = await _thumbnailRepository.GetQueryableAsync();
            var pending = await _asyncExecuter.ToListAsync(
                pendingQueryable
                    .Where(x => x.State == FileThumbnailStateEnum.Pending)
                    .OrderBy(x => x.Id)
                    .Take(MaxEnqueuedPerRun),
                cancellationToken);
            foreach (var row in pending)
            {
                await EnqueueAsync(row.TenantId, row.FileId, cancellationToken);
                enqueued++;
            }

            // 2) 还没有映射行的图片文件（历史数据/事件丢失）：建行 + 入队。
            // round4 重写（round3 声称的「NOT EXISTS 反连接」实际未落地——EasyAbp File 与
            // FileThumbnail 分属两个 DbContext，EF 不允许单查询跨上下文组合，只能两段式）：
            // a) FileType/扩展名谓词下推进 SQL——目录与非图片文件不再进入翻页与反查
            //    （扩展名必须进 SQL：留在内存会让永远不入队的非图片占住翻页头部，真缺行的图片轮不到）；
            // b) 窄投影（Id/TenantId/CreationTime）替代宽 File 实体物化；
            // c) CreationTime >= 游标递进替代 OFFSET——深分页不再累计扫描前面所有页
            //    （旧实现 10 万文件每 30 分钟 O(N²/500) 行走读 + ~20 次反查 + 全列物化）。
            // 同刻多行用 >= 重叠 + seen 去重保证不漏（游标若用 > 会永久跳过同刻后续行）。
            // 稳态仍是 O(图片数/PageSize) 线性翻页——受跨上下文约束无法做成单查询反连接；
            // 进一步消除需水位线（记录「已回填到的文件 CreationTime 下界」），涉及存放位置取舍，未做。
            var remaining = MaxEnqueuedPerRun - enqueued;
            if (remaining > 0)
            {
                var sourceExtensionPredicate = BuildSourceExtensionPredicate(_options.ThumbnailSourceExtensions);
                var seenFileIds = new System.Collections.Generic.HashSet<Guid>();
                DateTime? cursor = null;

                while (enqueued < MaxEnqueuedPerRun)
                {
                    var filesQueryable = await _fileRepository.GetQueryableAsync();
                    var pageQuery = filesQueryable
                        .Where(f => f.FileType == FileType.RegularFile)
                        .Where(sourceExtensionPredicate);
                    if (cursor != null)
                    {
                        pageQuery = pageQuery.Where(f => f.CreationTime >= cursor);
                    }

                    var page = await _asyncExecuter.ToListAsync(
                        pageQuery
                            .OrderBy(f => f.CreationTime)
                            .ThenBy(f => f.Id)
                            .Take(PageSize)
                            .Select(f => new { f.Id, f.TenantId, f.CreationTime }),
                        cancellationToken);
                    if (page.Count == 0)
                    {
                        break;
                    }

                    cursor = page[^1].CreationTime;

                    // 本轮新见的文件才需要反查（>= 游标会让同刻行重复出现在下一页头部）。
                    // 整页已见（同刻行数 > PageSize 的批量导入场景）时游标不再前移，
                    // 继续循环会永远查回同一页——兜底终止本轮，漏检行下一轮从头重扫补上。
                    var pageFileIds = page.Where(x => seenFileIds.Add(x.Id)).Select(x => x.Id).ToList();
                    if (pageFileIds.Count == 0)
                    {
                        break;
                    }

                    var existingFileIds = (await _thumbnailRepository.GetListAsync(
                            x => pageFileIds.Contains(x.FileId),
                            includeDetails: false,
                            cancellationToken))
                        .Select(t => t.FileId)
                        .ToHashSet();

                    foreach (var file in page.Where(x => pageFileIds.Contains(x.Id) && !existingFileIds.Contains(x.Id)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (enqueued >= MaxEnqueuedPerRun)
                        {
                            break;
                        }

                        await _thumbnailRepository.InsertAsync(
                            new FileThumbnail(GuidGenerator.Create(), file.TenantId, file.Id),
                            cancellationToken: cancellationToken);
                        await EnqueueAsync(file.TenantId, file.Id, cancellationToken);
                        enqueued++;
                    }
                }
            }

            return enqueued;
        }
    }

    /// <summary>
    /// 把缩略图源扩展名白名单拼成 EF 可翻译的 OR 谓词（EndsWith → LIKE '%.jpg'，ToLower → lower()）。
    /// 动态表达式而非 exts.Any(Contains)：后者无法整体翻译，必须逐项 OR 展开。
    /// 空白名单（配置成零个扩展名）时恒 false——一个都不入队，而不是退化为全量。
    /// </summary>
    private static Expression<Func<File, bool>> BuildSourceExtensionPredicate(
        System.Collections.Generic.IReadOnlyList<string> extensions)
    {
        var parameter = Expression.Parameter(typeof(File), "f");
        var fileName = Expression.Property(parameter, nameof(File.FileName));
        var toLower = Expression.Call(
            fileName, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var endsWith = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

        Expression? body = null;
        foreach (var extension in extensions)
        {
            var ends = Expression.Call(toLower, endsWith, Expression.Constant(extension));
            body = body == null ? ends : Expression.OrElse(ends, body);
        }

        body ??= Expression.Constant(false);
        return Expression.Lambda<Func<File, bool>>(body, parameter);
    }

    private Task EnqueueAsync(Guid? tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _backgroundJobManager.EnqueueAsync(
            new ThumbnailGenerationJobArgs
            {
                TenantId = tenantId,
                FileId = fileId,
                FileName = string.Empty
            });
    }
}
