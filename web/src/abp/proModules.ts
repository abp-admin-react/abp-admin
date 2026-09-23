import { request } from '@umijs/max';
import {
  getApiAppAuditLog,
  getApiAppAuditLogId,
  postApiAppAuditLogIdMarkHandled,
  postApiAppAuditLogIdUnmarkHandled,
} from '@/services/abpadmin/auditLog';
import {
  deleteApiAppBackgroundJobId,
  getApiAppBackgroundJob,
  postApiAppBackgroundJobEnqueueTest,
  postApiAppBackgroundJobIdAbandon,
  postApiAppBackgroundJobIdRetry,
} from '@/services/abpadmin/backgroundJob';
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
  // 走 openapi 生成客户端（生成物随批次重生成，参数契约由 tsc 把关——web/AGENTS.md 约定）
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

/** 平均执行时长统计 */
export async function getAuditLogAverageDuration(params?: {
  startTime?: string;
  endTime?: string;
}) {
  return request<any[]>(
    '/api/app/audit-log/average-execution-duration-per-day',
    {
      method: 'GET',
      params: {
        StartTime: params?.startTime,
        EndTime: params?.endTime,
      },
    },
  );
}

/** 错误率统计 */
export async function getAuditLogErrorRate(params?: {
  startTime?: string;
  endTime?: string;
}) {
  return request<any>('/api/app/audit-log/error-rate', {
    method: 'GET',
    params: {
      StartTime: params?.startTime,
      EndTime: params?.endTime,
    },
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

export async function getLanguageSettings() {
  return request<{
    defaultLanguage?: string;
    languages: { cultureName: string; displayName: string }[];
  }>('/api/app/language', { method: 'GET' });
}

export async function updateLanguageSettings(data: {
  defaultLanguage?: string;
}) {
  return request('/api/app/language', { method: 'PUT', data });
}

export async function getBackgroundJobs(params: {
  current?: number;
  pageSize?: number;
  jobName?: string;
  isAbandoned?: boolean;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return getApiAppBackgroundJob({
    SkipCount: skipCount,
    MaxResultCount: maxResultCount,
    JobName: params.jobName,
    IsAbandoned: params.isAbandoned,
  });
}

export async function abandonBackgroundJob(id: string) {
  return postApiAppBackgroundJobIdAbandon({ id });
}

export async function retryBackgroundJob(id: string) {
  return postApiAppBackgroundJobIdRetry({ id });
}

export async function deleteBackgroundJob(id: string) {
  return deleteApiAppBackgroundJobId({ id });
}

export async function enqueueTestBackgroundJob(data: {
  emailAddress: string;
  subject: string;
  body?: string;
}) {
  return postApiAppBackgroundJobEnqueueTest(data);
}

/* ========== 岗位管理（对标 RuoYi sys_post） ========== */

export type PostDto = {
  id: string;
  name: string;
  code: string;
  sortOrder: number;
  status: number; // 0 启用 1 停用
  remark?: string;
  memberCount?: number;
};

export async function getPosts(params?: {
  current?: number;
  pageSize?: number;
  filter?: string;
  status?: number;
}) {
  const maxResultCount = params?.pageSize ?? 20;
  const skipCount = ((params?.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<PostDto>>('/api/app/post', {
    method: 'GET',
    params: {
      Filter: params?.filter,
      Status: params?.status,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
    },
  });
}

export async function getAllEnabledPosts() {
  return request<{ items: PostDto[] }>('/api/app/post/lookup', {
    method: 'GET',
  });
}

export async function createPost(data: Omit<PostDto, 'id' | 'memberCount'>) {
  return request<PostDto>('/api/app/post', { method: 'POST', data });
}

export async function updatePost(
  id: string,
  data: Omit<PostDto, 'id' | 'memberCount'>,
) {
  return request<PostDto>(`/api/app/post/${id}`, { method: 'PUT', data });
}

export async function deletePost(id: string) {
  return request(`/api/app/post/${id}`, { method: 'DELETE' });
}

export async function getPostMembers(
  id: string,
  params?: { current?: number; pageSize?: number; filter?: string },
) {
  const maxResultCount = params?.pageSize ?? 20;
  const skipCount = ((params?.current ?? 1) - 1) * maxResultCount;
  return request<
    PagedResult<{
      id: string;
      userName: string;
      name?: string;
      email?: string;
      isActive: boolean;
    }>
  >(`/api/app/post/${id}/members`, {
    method: 'GET',
    params: {
      Filter: params?.filter,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
    },
  });
}

export async function addPostMembers(id: string, userIds: string[]) {
  return request(`/api/app/post/${id}/members`, {
    method: 'POST',
    data: userIds,
  });
}

export async function removePostMember(id: string, userId: string) {
  return request(`/api/app/post/${id}/members/${userId}`, {
    method: 'DELETE',
  });
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
