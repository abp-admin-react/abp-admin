import { request } from '@umijs/max';
import type { GridQuery } from './gridQuery';
import type { PagedResult } from './identity';

export type OperationLogDto = {
  id: string;
  userId?: string | null;
  userName?: string | null;
  type: string;
  subType: string;
  bizId?: string | null;
  action?: string | null;
  extra?: string | null;
  success: boolean;
  errorMessage?: string | null;
  requestMethod?: string | null;
  requestUrl?: string | null;
  clientIpAddress?: string | null;
  ipLocation?: string | null;
  userAgent?: string | null;
  correlationId?: string | null;
  duration: number;
  executionTime: string;
};

export type GetOperationLogListParams = {
  current?: number;
  pageSize?: number;
  Filter?: string;
  Type?: string;
  SubType?: string;
  Success?: boolean;
  UserId?: string;
  CorrelationId?: string;
  StartTime?: string;
  EndTime?: string;
};

export function queryOperationLogs(input: GridQuery, signal: AbortSignal) {
  return request<PagedResult<OperationLogDto>>('/api/app/operation-log/query', {
    method: 'POST',
    data: input,
    signal,
    // 数据块加载失败时由网格显示重试入口。
    skipErrorHandler: true,
  });
}

export async function getOperationLogs(params: GetOperationLogListParams) {
  return request<PagedResult<OperationLogDto>>('/api/app/operation-log', {
    method: 'GET',
    params: {
      SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
      MaxResultCount: params.pageSize ?? 10,
      Filter: params.Filter,
      Type: params.Type,
      SubType: params.SubType,
      Success: params.Success,
      UserId: params.UserId,
      CorrelationId: params.CorrelationId,
      StartTime: params.StartTime,
      EndTime: params.EndTime,
    },
  });
}
