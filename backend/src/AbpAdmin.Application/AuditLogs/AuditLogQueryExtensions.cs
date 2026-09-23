using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using Volo.Abp.AuditLogging;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// GetAuditLogListInput 的 10 个筛选实参此前在 AppService / ExcelBuilder / ExportController /
/// ExportJob 多处以相同顺序复制传递，新增一个筛选项要同步改多处，漏一处即产生
/// "列表有筛选、导出没有"的隐性分叉。统一封装成按 input 对象透传的扩展，筛选口径只改这里。
/// </summary>
public static class AuditLogQueryExtensions
{
    /// <summary>
    /// 错误口径唯一出处：HttpStatusCode≥400 或有异常。
    /// 列表「仅未处理错误」视图、同步/异步导出、错误率统计全部经此谓词，改口径只改这里。
    /// </summary>
    public static Expression<Func<AuditLog, bool>> IsErrorPredicate()
        => x => (x.HttpStatusCode.HasValue && (int)x.HttpStatusCode.Value >= 400)
                || !string.IsNullOrEmpty(x.Exceptions);

    /// <summary>
    /// 「仅未处理错误」组合筛选：错误口径 + 未处理 + 常规筛选。
    /// 未处理条件经 <see cref="IAuditLogHandledColumnQueries"/> 注入（EF.Property 影子列表达式，
    /// 不能出现在 Application 层——本工程刻意不引 EF Core）；常规筛选与
    /// GetListByKeysetAsync 逐条对齐（含 HasException 两分支）。
    /// </summary>
    public static IQueryable<AuditLog> ApplyUnhandledErrorFilters(
        this IQueryable<AuditLog> queryable,
        GetAuditLogListInput input,
        IAuditLogHandledColumnQueries handledColumnQueries)
    {
        var httpMethod = input.HttpMethod;
        var url = input.Url;
        var userName = input.UserName;
        var applicationName = input.ApplicationName;
        var correlationId = input.CorrelationId;

        return handledColumnQueries.ApplyUnhandled(queryable)
            .Where(IsErrorPredicate())
            .WhereIf(input.StartTime.HasValue, x => x.ExecutionTime >= input.StartTime)
            .WhereIf(input.EndTime.HasValue, x => x.ExecutionTime <= input.EndTime)
            .WhereIf(input.HasException == true, x => x.Exceptions != null && x.Exceptions != string.Empty)
            .WhereIf(input.HasException == false, x => x.Exceptions == null || x.Exceptions == string.Empty)
            .WhereIf(!string.IsNullOrEmpty(httpMethod), x => x.HttpMethod == httpMethod)
            .WhereIf(!string.IsNullOrEmpty(url), x => x.Url != null && x.Url.Contains(url!))
            .WhereIf(!string.IsNullOrEmpty(userName), x => x.UserName == userName)
            .WhereIf(!string.IsNullOrEmpty(applicationName), x => x.ApplicationName == applicationName)
            .WhereIf(!string.IsNullOrEmpty(correlationId), x => x.CorrelationId == correlationId)
            .WhereIf(input.HttpStatusCode > (HttpStatusCode)0, x => x.HttpStatusCode == (int?)input.HttpStatusCode);
    }

    /// <summary>「仅未处理错误」计数（导出阈值判断与列表页共用同一口径）。</summary>
    public static async Task<long> GetUnhandledErrorCountAsync(
        this IAuditLogRepository repository,
        GetAuditLogListInput input,
        IAuditLogHandledColumnQueries handledColumnQueries)
    {
        var queryable = await repository.GetQueryableAsync();
        return await repository.AsyncExecuter.CountAsync(
            queryable.ApplyUnhandledErrorFilters(input, handledColumnQueries));
    }

    /// <summary>
    /// 「仅未处理错误」的导出 keyset 批次：Id 降序游标（与 GetListByKeysetAsync 同语义，
    /// 截断导出保留最新 N 条）。导出链路支持该筛选是「列表与导出口径一致」规约的一部分。
    /// </summary>
    public static async Task<List<AuditLog>> GetUnhandledErrorListByKeysetAsync(
        this IAuditLogRepository repository,
        GetAuditLogListInput input,
        int maxResultCount,
        Guid afterId,
        IAuditLogHandledColumnQueries handledColumnQueries)
    {
        var queryable = await repository.GetQueryableAsync();
        queryable = queryable
            .ApplyUnhandledErrorFilters(input, handledColumnQueries)
            .Where(x => x.Id < afterId)
            .OrderByDescending(x => x.Id);

        return await repository.AsyncExecuter.ToListAsync(queryable.Take(maxResultCount));
    }

    /// <summary>
    /// 按 input 的筛选条件分页取审计日志（含 Sorting）。
    /// afterId 非空时走 keyset 路径：追加 Id &lt; afterId 过滤并固定按 Id 降序（确定性分页，
    /// 不再透传 input.Sorting），供导出 Builder 游标推进；默认 null 保持原语义（列表页用）。
    /// </summary>
    public static Task<List<AuditLog>> GetListByInputAsync(
        this IAuditLogRepository repository,
        GetAuditLogListInput input,
        int maxResultCount,
        int skipCount,
        Guid? afterId = null)
        => afterId == null
            ? repository.GetListAsync(
                input.Sorting,
                maxResultCount,
                skipCount,
                input.StartTime,
                input.EndTime,
                input.HttpMethod,
                input.Url,
                userName: input.UserName,
                applicationName: input.ApplicationName,
                correlationId: input.CorrelationId,
                hasException: input.HasException,
                httpStatusCode: input.HttpStatusCode)
            : GetListByKeysetAsync(repository, input, maxResultCount, afterId.Value);

    /// <summary>按 input 的筛选条件取命中条数。</summary>
    public static Task<long> GetCountByInputAsync(
        this IAuditLogRepository repository,
        GetAuditLogListInput input)
        => repository.GetCountAsync(
            input.StartTime,
            input.EndTime,
            input.HttpMethod,
            input.Url,
            userName: input.UserName,
            applicationName: input.ApplicationName,
            correlationId: input.CorrelationId,
            hasException: input.HasException,
            httpStatusCode: input.HttpStatusCode);

    /// <summary>
    /// keyset 路径：OFFSET 递增分页在审计表上每批都要扫过前面所有批的偏移
    /// （AsyncMaxRows=20 万累计千万行级偏移扫描），且透传 input.Sorting 时跨批顺序未定义
    /// （插入/排序键不稳定即可重可漏）。追加 Id &lt; afterId 并固定 OrderByDescending(Id)——
    /// 降序=最新优先：截断导出保留「最新 N 条」语义（与原默认 ExecutionTime DESC 一致），
    /// 每批只扫 Id 索引区间、顺序确定、不重不漏。
    /// 筛选条件与 EfCoreAuditLogRepository.GetListQueryAsync 逐条对齐
    /// （含 hasException 的空串口径与 httpStatusCode&gt;0 守卫），导出与列表/计数口径一致。
    /// </summary>
    private static async Task<List<AuditLog>> GetListByKeysetAsync(
        IAuditLogRepository repository,
        GetAuditLogListInput input,
        int maxResultCount,
        Guid afterId)
    {
        var httpMethod = input.HttpMethod;
        var url = input.Url;
        var userName = input.UserName;
        var applicationName = input.ApplicationName;
        var correlationId = input.CorrelationId;

        var queryable = await repository.GetQueryableAsync();

        queryable = queryable
            .WhereIf(input.StartTime.HasValue, x => x.ExecutionTime >= input.StartTime)
            .WhereIf(input.EndTime.HasValue, x => x.ExecutionTime <= input.EndTime)
            .WhereIf(input.HasException == true, x => x.Exceptions != null && x.Exceptions != string.Empty)
            .WhereIf(input.HasException == false, x => x.Exceptions == null || x.Exceptions == string.Empty)
            .WhereIf(!string.IsNullOrEmpty(httpMethod), x => x.HttpMethod == httpMethod)
            .WhereIf(!string.IsNullOrEmpty(url), x => x.Url != null && x.Url.Contains(url!))
            .WhereIf(!string.IsNullOrEmpty(userName), x => x.UserName == userName)
            .WhereIf(!string.IsNullOrEmpty(applicationName), x => x.ApplicationName == applicationName)
            .WhereIf(!string.IsNullOrEmpty(correlationId), x => x.CorrelationId == correlationId)
            .WhereIf(input.HttpStatusCode > (HttpStatusCode)0, x => x.HttpStatusCode == (int?)input.HttpStatusCode)
            .Where(x => x.Id < afterId)
            .OrderByDescending(x => x.Id);

        return await repository.AsyncExecuter.ToListAsync(queryable.Take(maxResultCount));
    }
}
