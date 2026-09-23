using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiniExcelLibs;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Modularity;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 审计日志测试共享基类：插入审计日志 / 解析 xlsx。
/// 注意测试库在 collection 内共享，查询一律带唯一 Url 标记或独立时间窗口，不假设全表只有自己插入的行。
/// </summary>
public abstract class AuditLogTestBase<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    /// <summary>
    /// 插入一条审计日志（host 上下文）。url 用于测试间的数据隔离（每个用例传唯一标记）。
    /// </summary>
    protected async Task<AuditLog> CreateAuditLogAsync(
        DateTime executionTime,
        string url,
        int executionDuration = 100,
        int? httpStatusCode = 200,
        string? exceptions = null,
        string? userName = "admin",
        List<EntityChange>? entityChanges = null,
        Guid? id = null)
    {
        var log = new AuditLog(
            id ?? Guid.NewGuid(),
            applicationName: "AbpAdmin.Tests",
            tenantId: null,
            tenantName: null,
            userId: AdminUserId,
            userName: userName,
            executionTime: executionTime,
            executionDuration: executionDuration,
            clientIpAddress: "127.0.0.1",
            clientName: null,
            clientId: null,
            correlationId: Guid.NewGuid().ToString("N"),
            browserInfo: null,
            httpMethod: "GET",
            url: url,
            httpStatusCode: httpStatusCode,
            impersonatorUserId: null,
            impersonatorUserName: null,
            impersonatorTenantId: null,
            impersonatorTenantName: null,
            extraPropertyDictionary: new ExtraPropertyDictionary(),
            entityChanges: entityChanges ?? new List<EntityChange>(),
            actions: new List<AuditLogAction>(),
            exceptions: exceptions,
            comments: null);

        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IAuditLogRepository>().InsertAsync(log);
        });

        return log;
    }

    /// <summary>
    /// 解析 xlsx 字节为带表头的行字典。
    /// </summary>
    protected static List<IDictionary<string, object>> ParseExcelRows(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return stream.Query(useHeaderRow: true)
            .Cast<IDictionary<string, object>>()
            .ToList();
    }
}
