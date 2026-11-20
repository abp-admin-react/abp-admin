import { history, request } from '@umijs/max';
import type { PagedResult } from './identity';

export type GridFilterOperation =
  | 'eq'
  | 'ne'
  | 'contains'
  | 'notContains'
  | 'startsWith'
  | 'endsWith'
  | 'gt'
  | 'gte'
  | 'lt'
  | 'lte'
  | 'between';

export interface GridCondition {
  op: GridFilterOperation;
  value: string;
  valueTo?: string | null;
}

export interface GridQuery {
  skipCount: number;
  maxResultCount: number;
  keyword?: string | null;
  filters: { field: string; join: 'and' | 'or'; conditions: GridCondition[] }[];
  sort: { field: string; direction: 'asc' | 'desc' }[];
}
/** 表格自行展示筛选与网络错误，登录失效仍按管理端约定返回登录页。 */
export async function postGridQuery<T>(
  url: string,
  input: GridQuery,
  signal: AbortSignal,
) {
  try {
    return await request<PagedResult<T>>(url, {
      method: 'POST',
      data: input,
      signal,
      skipErrorHandler: true,
    });
  } catch (error) {
    if (
      !signal.aborted &&
      (error as { response?: { status?: number } } | null)?.response?.status ===
        401
    ) {
      history.replace('/user/login');
    }
    throw error;
  }
}
