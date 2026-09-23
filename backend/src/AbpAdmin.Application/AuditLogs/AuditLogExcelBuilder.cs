using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiniExcelLibs;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.AuditLogs;

// 实现 Contracts 的 IAuditLogExcelBuilder：HttpApi 同步导出控制器只依赖接口，
// 不需要（也不允许）引用 Application 项目（先例：Identity 的 IUserExcelBuilder）
public class AuditLogExcelBuilder : IAuditLogExcelBuilder, ITransientDependency
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogHandledColumnQueries _handledColumnQueries;

    public AuditLogExcelBuilder(
        IAuditLogRepository auditLogRepository,
        IAuditLogHandledColumnQueries handledColumnQueries)
    {
        _auditLogRepository = auditLogRepository;
        _handledColumnQueries = handledColumnQueries;
    }

    /// <summary>按筛选条件计数（与 BuildAsync 同一筛选扩展，见接口注释）。</summary>
    public virtual Task<long> CountAsync(GetAuditLogListInput input)
    {
        // 「仅未处理错误」筛选必须在导出链路同样生效，否则复现
        // 「列表有筛选、导出没有」的分叉（AuditLogQueryExtensions 类注释立的规约）
        if (input.UnhandledErrorOnly == true)
        {
            return _auditLogRepository.GetUnhandledErrorCountAsync(input, _handledColumnQueries);
        }

        return _auditLogRepository.GetCountByInputAsync(input);
    }

    /// <summary>
    /// 按筛选条件分批拉取审计日志并生成 Excel 字节数组。
    /// maxResultCount 是硬上限：同步分支传实际条数（≤ SyncThreshold），
    /// 后台作业分支传 AuditLogExportConsts.AsyncMaxRows（调用方负责截断语义与邮件说明）。
    /// 分批用 Id 游标推进（GetListByInputAsync 的 afterId keyset 路径）而不是 skipCount
    /// 递增 OFFSET：深偏移在审计表上每批都要重新扫过前面所有批，且透传 input.Sorting 时
    /// 跨批顺序未定义可重可漏；固定 Id 升序游标分页确定性不重不漏（导出行序随之变为 Id 升序）。
    /// 列头固定中文（T2.1 验收标准：用 Excel 打开列头是中文），不做本地化——
    /// 因此本类不注入 IStringLocalizer。
    /// </summary>
    public virtual async Task<byte[]> BuildAsync(GetAuditLogListInput input, int maxResultCount)
    {
        var logs = new List<AuditLog>();
        // 起始游标降序哨兵取 Guid.MaxValue：首个 Id 必小于它，首批即全量起点
        var afterId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        while (logs.Count < maxResultCount)
        {
            var remaining = maxResultCount - logs.Count;
            var currentBatchSize = Math.Min(AuditLogExportConsts.ExcelBatchSize, remaining);

            // 「仅未处理错误」在导出 keyset 批次同样生效（与列表/计数同口径，
            // 未处理 = HandledAt 映射列 IS NULL，经 IAuditLogHandledColumnQueries 注入）
            var batch = input.UnhandledErrorOnly == true
                ? await _auditLogRepository.GetUnhandledErrorListByKeysetAsync(
                    input, currentBatchSize, afterId, _handledColumnQueries)
                : await _auditLogRepository.GetListByInputAsync(
                    input, currentBatchSize, skipCount: 0, afterId);

            if (batch.Count == 0)
            {
                break;
            }

            logs.AddRange(batch);
            // 批内按 Id 升序，最后一个即本批最大 Id，作为下一批游标
            afterId = batch[^1].Id;

            if (batch.Count < currentBatchSize)
            {
                break;
            }
        }

        var rows = logs.Select(x => new
        {
            执行时间 = x.ExecutionTime,
            用户名 = x.UserName,
            应用 = x.ApplicationName,
            请求方法 = x.HttpMethod,
            地址 = x.Url,
            状态码 = x.HttpStatusCode,
            耗时毫秒 = x.ExecutionDuration,
            客户端IP = x.ClientIpAddress,
            关联ID = x.CorrelationId,
            异常 = x.Exceptions
        });

        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, rows);
        return stream.ToArray();
    }
}
