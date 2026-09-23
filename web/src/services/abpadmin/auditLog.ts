// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/audit-log */
export async function getApiAppAuditLog(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1AuditLogDto>("/api/app/audit-log", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 单条日志详情：含动作/实体变更明细、处理状态（列投影）与 IP 归属地。 GET /api/app/audit-log/${param0} */
export async function getApiAppAuditLogId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.AuditLogDto>(`/api/app/audit-log/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 标记审计日志为已处理（对标 ruoyi ApiErrorLog 的标记已处理）。幂等 upsert：
重复标记刷新处理人/时间/备注（同值覆盖，last-write-wins）。普通日志也允许标记。
状态写在 AuditLog 行内的 HandledAt/HandledByUserId/HandledByName/HandledNote
扩展属性映射列上（AbpAdminEfCoreEntityExtensionMappings）。
并发语义：AggregateRoot 自带 ConcurrencyStamp（乐观并发），多管理员真正同时标记
时后写者会撞 409（AbpDbConcurrencyException）——本操作是纯覆盖、无丢失更新风险，
故就地重试取最新值再写（见 RetryOnConcurrency），把 last-write-wins 真正落到实处，
不必再像侧表时代那样靠分布式锁串行化。 POST /api/app/audit-log/${param0}/mark-handled */
export async function postApiAppAuditLogIdMarkHandled(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppAuditLogIdMarkHandledParams,
  body: API.MarkAuditLogHandledInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/audit-log/${param0}/mark-handled`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 取消已处理标记；未标记或日志不存在时静默成功（前端按钮幂等）。并发重试语义同标记。 POST /api/app/audit-log/${param0}/unmark-handled */
export async function postApiAppAuditLogIdUnmarkHandled(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppAuditLogIdUnmarkHandledParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/audit-log/${param0}/unmark-handled`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 平均执行时长统计。聚合在数据库侧完成（IRepository<AuditLog, Guid>.GetQueryableAsync + GroupBy）。 GET /api/app/audit-log/average-execution-duration-per-day */
export async function getApiAppAuditLogAverageExecutionDurationPerDay(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogAverageExecutionDurationPerDayParams,
  options?: { [key: string]: any }
) {
  return request<API.AuditLogAverageDurationDto[]>(
    "/api/app/audit-log/average-execution-duration-per-day",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 异步导出审计日志。转后台作业，完成后发邮件通知。
同步导出请走 Controller 的 GET /api/app/audit-log/export。
ABP 动态 API 路由：POST /api/app/audit-log/enqueue-export
T2.5: 操作限流 - 按当前用户 1 天 10 次（租户隔离） POST /api/app/audit-log/enqueue-export */
export async function postApiAppAuditLogEnqueueExport(
  body: API.GetAuditLogListInput,
  options?: { [key: string]: any }
) {
  return request<API.AuditLogExportResultDto>(
    "/api/app/audit-log/enqueue-export",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      data: body,
      ...(options || {}),
    }
  );
}

/** 单实体完整变更历史。优先用实体级权限，未定义或未授予时回退到 AbpAdmin.AuditLogs。 GET /api/app/audit-log/entity-change-history */
export async function getApiAppAuditLogEntityChangeHistory(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogEntityChangeHistoryParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1EntityChangeHistoryDto>(
    "/api/app/audit-log/entity-change-history",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 错误率统计。错误口径唯一出处：AuditLogQueryExtensions.IsErrorPredicate()
（与「仅未处理错误」列表/导出共用）。总量/错误量按两次 GroupBy 查询后在内存合并——
换取谓词共享，避免同一段判断在两处维护漂移。 GET /api/app/audit-log/error-rate */
export async function getApiAppAuditLogErrorRate(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogErrorRateParams,
  options?: { [key: string]: any }
) {
  return request<API.AuditLogErrorRateDto>("/api/app/audit-log/error-rate", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
