import { request } from '@umijs/max';
import {
  getApiAppAuditLog,
  getApiAppAuditLogId,
  postApiAppAuditLogIdMarkHandled,
  postApiAppAuditLogIdUnmarkHandled,
} from '@/services/abpadmin/auditLog';
import type { PagedResult } from './identity';

export async function getAuditLogs(params: {
  current?: number;
  pageSize?: number;
  httpMethod?: string;
  url?: string;
  userName?: string;
  correlationId?: string;
  startTime?: string;
  endTime?: string;
  hasException?: boolean;
  /** 仅未处理错误（错误口径：状态码≥400 或有异常；已标记处理的排除） */
  unhandledErrorOnly?: boolean;
}) {
  // 走冻结的历史 openapi 生成物（不再新增/重生成——web/AGENTS.md 约定；改动时整域迁入 src/abp）
  return getApiAppAuditLog({
    SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
    MaxResultCount: params.pageSize ?? 10,
    HttpMethod: params.httpMethod,
    Url: params.url,
    UserName: params.userName,
    CorrelationId: params.correlationId,
    StartTime: params.startTime,
    EndTime: params.endTime,
    HasException: params.hasException,
    UnhandledErrorOnly: params.unhandledErrorOnly,
  });
}

export async function getAuditLog(id: string) {
  return getApiAppAuditLogId({ id });
}

/** 标记审计日志（错误）为已处理。幂等：重复标记刷新处理人与备注。 */
export async function markAuditLogHandled(id: string, note?: string) {
  return postApiAppAuditLogIdMarkHandled({ id }, { note });
}

/** 取消审计日志的已处理标记（未标记时静默成功；ABP 动态 API 生成 POST）。 */
export async function unmarkAuditLogHandled(id: string) {
  return postApiAppAuditLogIdUnmarkHandled({ id });
}

/** 导出审计日志。同步返回 Blob，异步返回 { isQueued: true }。 */
export async function exportAuditLogs(params: {
  httpMethod?: string;
  url?: string;
  userName?: string;
  startTime?: string;
  endTime?: string;
  hasException?: boolean;
  correlationId?: string;
  /** 仅未处理错误：与列表筛选同口径（后端同步/异步导出链路均支持） */
  unhandledErrorOnly?: boolean;
}): Promise<{ isQueued: false; blob: Blob } | { isQueued: true }> {
  // 过滤掉 undefined 值，避免序列化成空对象
  const queryParams = Object.fromEntries(
    Object.entries({
      HttpMethod: params.httpMethod,
      Url: params.url,
      UserName: params.userName,
      StartTime: params.startTime,
      EndTime: params.endTime,
      HasException: params.hasException,
      CorrelationId: params.correlationId,
      UnhandledErrorOnly: params.unhandledErrorOnly,
    }).filter(([_, v]) => v !== undefined && v !== null && v !== ''),
  );

  // 先尝试同步导出（GET，返回文件流）
  try {
    // getResponse: true 时 umi request 返回完整 AxiosResponse
    const res = await request<Blob>('/api/app/audit-log/export', {
      method: 'GET',
      params: queryParams,
      responseType: 'blob',
      getResponse: true,
      // 同步导出返回文件流，跳过统一 JSON 解析
      skipErrorHandler: true,
    });
    const headers = res.headers as
      | (Record<string, any> & { get?: (name: string) => string | undefined })
      | undefined;
    const contentType = String(
      headers?.get?.('content-type') ?? headers?.['content-type'] ?? '',
    );
    if (contentType.includes('application/vnd.openxmlformats')) {
      return { isQueued: false, blob: res.data as Blob };
    }
  } catch (error: any) {
    // 如果是 400 错误（超过同步阈值），则转异步导出
    if (error?.response?.status !== 400) {
      throw error;
    }
  }

  // 异步导出：转后台作业（ABP 动态 API 生成的路由是 POST /api/app/audit-log/enqueue-export）
  await request('/api/app/audit-log/enqueue-export', {
    method: 'POST',
    data: queryParams,
  });
  return { isQueued: true };
}

/** 获取单实体完整变更历史 */
export async function getEntityChangeHistory(params: {
  entityTypeFullName: string;
  entityId: string;
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<any>>('/api/app/audit-log/entity-change-history', {
    method: 'GET',
    params: {
      EntityTypeFullName: params.entityTypeFullName,
      EntityId: params.entityId,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      Sorting: 'ChangeTime DESC',
    },
  });
}

/** 实体变更回滚结果（restoreEntityChange 的返回契约，与后端 EntityRestoreResultDto 镜像） */
export type EntityRestoreResult = {
  entityChangeId: string;
  entityTypeFullName: string;
  entityId: string;
  restoredProperties: string[];
  skippedProperties: { propertyName: string; reason: string }[];
};

/**
 * 按一条实体变更记录回滚实体属性（写回 OriginalValue）。
 * 仅 Updated 型变更支持；回滚本身会再产生一条变更记录。
 */
export async function restoreEntityChange(params: {
  entityChangeId: string;
  entityId: string;
  entityTypeFullName: string;
}): Promise<EntityRestoreResult> {
  return request<EntityRestoreResult>('/api/app/audit-log/restore-entity-change', {
    method: 'POST',
    data: {
      EntityChangeId: params.entityChangeId,
      EntityId: params.entityId,
      EntityTypeFullName: params.entityTypeFullName,
    },
  });
}

type AuditLogErrorRateDto = {
  totalCount: number;
  errorCount: number;
  errorRate: number;
  dataPoints: {
    date: string;
    totalCount: number;
    errorCount: number;
    errorRate: number;
  }[];
};

/** 首页审计概况沿用服务端最近 7 天的统计窗口，失败在卡片内展示。 */
export async function getAuditLogErrorRate() {
  return request<AuditLogErrorRateDto>('/api/app/audit-log/error-rate', {
    method: 'GET',
    skipErrorHandler: true,
  });
}

export async function getEditions(params?: {
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params?.pageSize ?? 20;
  const skipCount = ((params?.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<{ id: string; displayName: string }>>(
    '/api/app/edition',
    {
      method: 'GET',
      params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
    },
  );
}

export async function getAllEditions() {
  return request<{ items: { id: string; displayName: string }[] }>(
    '/api/app/edition/lookup',
    { method: 'GET' },
  );
}

export async function createEdition(data: { displayName: string }) {
  return request('/api/app/edition', { method: 'POST', data });
}

export async function updateEdition(id: string, data: { displayName: string }) {
  return request(`/api/app/edition/${id}`, { method: 'PUT', data });
}

export async function deleteEdition(
  id: string,
  moveTenantsToEditionId?: string,
) {
  // 后端是 ABP 约定控制器：DELETE 的复杂入参按属性从 query 绑定（body 不生效）
  return request(`/api/app/edition/${id}`, {
    method: 'DELETE',
    params: moveTenantsToEditionId
      ? { MoveTenantsToEditionId: moveTenantsToEditionId }
      : {},
  });
}

/** 该版本下的租户数量（删除确认弹窗展示受影响租户数用） */
export async function getEditionTenantCount(id: string) {
  return request<number>(`/api/app/edition/${id}/tenant-count`, {
    method: 'GET',
  });
}

export async function setTenantEdition(tenantId: string, editionId?: string) {
  return request('/api/app/edition/set-tenant-edition', {
    method: 'POST',
    params: { tenantId, editionId },
  });
}

// 文本模板与虚拟文件浏览器的 API 封装已删除：
// 页面统一改用 OpenAPI 生成客户端（services/abpadmin/textTemplate.ts、virtualFileExplorer.ts）

export type BackgroundJobDto = {
  id: string;
  applicationName: string | null;
  jobName: string;
  jobArgs: string;
  tryCount: number;
  creationTime: string;
  nextTryTime: string;
  lastTryTime: string | null;
  isAbandoned: boolean;
  completionTime: string | null;
  priority: number;
};

export async function getBackgroundJobs(params: {
  current?: number;
  pageSize?: number;
  jobName?: string;
  isAbandoned?: boolean;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<BackgroundJobDto>>('/api/app/background-job', {
    method: 'GET',
    params: {
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      JobName: params.jobName,
      IsAbandoned: params.isAbandoned,
    },
  });
}

export async function abandonBackgroundJob(id: string) {
  return request(`/api/app/background-job/${id}/abandon`, { method: 'POST' });
}

export async function retryBackgroundJob(id: string) {
  return request(`/api/app/background-job/${id}/retry`, { method: 'POST' });
}

export async function deleteBackgroundJob(id: string) {
  return request(`/api/app/background-job/${id}`, { method: 'DELETE' });
}

/* ========== 服务监控 / 缓存监控（Host 专属） ========== */

export type ServerMonitorDto = {
  machineName: string;
  osDescription: string;
  osArchitecture: string;
  processArchitecture: string;
  processorCount: number;
  dotNetVersion: string;
  processStartTimeUtc: string;
  uptimeSeconds: number;
  /** null = 300ms 差分采样失败（区分于 0% 空闲），前端渲染为「-」 */
  processCpuUsagePercent?: number | null;
  workingSetBytes: number;
  privateMemoryBytes: number;
  gcHeapSizeBytes: number;
  gcTotalMemoryLimitBytes: number;
  isServerGc: boolean;
  gen0Collections: number;
  gen1Collections: number;
  gen2Collections: number;
  threadCount: number;
  threadPoolAvailableWorkerThreads: number;
  threadPoolMinWorkerThreads: number;
  disks: {
    name: string;
    driveFormat: string;
    totalBytes: number;
    freeBytes: number;
  }[];
};

export async function getServerMonitor() {
  return request<ServerMonitorDto>('/api/app/server-monitor', {
    method: 'GET',
  });
}

export type CacheMonitorInfoDto = {
  backend: 'redis' | 'memory';
  keyPrefix: string;
  totalKeys?: number;
  redisVersion?: string;
  usedMemoryBytes?: number;
  maxMemoryBytes?: number;
  connectionError?: string;
  /** 概览统计命令（DBSIZE/INFO）被拒的原因：连接正常、键浏览不受影响 */
  infoError?: string;
};

export async function getCacheMonitorInfo() {
  return request<CacheMonitorInfoDto>('/api/app/cache-monitor/info', {
    method: 'GET',
  });
}

export type CacheKeyDto = {
  key: string;
  type: string;
  sizeBytes?: number;
  ttlSeconds?: number | null;
};

export async function getCacheKeys(params: {
  prefix?: string;
  cursor?: number;
  maxResultCount?: number;
}) {
  return request<{ keys: CacheKeyDto[]; nextCursor: number }>(
    '/api/app/cache-monitor/keys',
    {
      method: 'GET',
      params: {
        prefix: params.prefix,
        cursor: params.cursor ?? 0,
        maxResultCount: params.maxResultCount ?? 100,
      },
    },
  );
}

export async function getCacheValue(key: string) {
  return request<{
    key: string;
    type: string;
    ttlSeconds?: number | null;
    content: string;
    truncated: boolean;
    /** hash 键 data 字段原文（写值预填用；超限或非 RedisCache 条目为 null） */
    dataField?: string | null;
  }>('/api/app/cache-monitor/value', {
    method: 'GET',
    params: { key },
  });
}

export async function deleteCacheKey(key: string) {
  // 路由必须是单数 /key（对应后端 DeleteKeyAsync，query 绑定 key）——/keys 只有 GET 列表
  return request('/api/app/cache-monitor/key', {
    method: 'DELETE',
    params: { key },
  });
}

export type CacheSetValueInput = {
  key: string;
  /** 写入的值原文。hash 键写 data 字段（保持 ABP 缓存的 JSON 信封可反序列化）；string 键覆盖整键 */
  value: string;
  /** 绝对过期（从现在起算的秒数）；不传 = 沿用既有 absexp 元数据（无则不过期） */
  absoluteExpirationSeconds?: number;
  /** 滑动过期（秒数）；不传 = 沿用既有 sldexp 元数据 */
  slidingExpirationSeconds?: number;
};

export async function updateCacheValue(input: CacheSetValueInput) {
  // 后端 SetValueAsync：无动词前缀 → ABP 默认 POST + kebab-case 全名路由 set-value
  // （swagger 实测：/api/app/cache-monitor/set-value；与 GET /value 不同路径）
  return request('/api/app/cache-monitor/set-value', {
    method: 'POST',
    data: input,
  });
}

export async function refreshCacheKey(input: {
  key: string;
  absoluteExpirationSeconds?: number;
  slidingExpirationSeconds?: number;
}) {
  // 后端 RefreshAsync：无动词前缀 → ABP 默认 POST + 全名 camel 路由
  return request('/api/app/cache-monitor/refresh', {
    method: 'POST',
    data: input,
  });
}
