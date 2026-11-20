import { useCallback } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import type { PagedResult } from '@/abp/identity';

/** 业务范围变更时更换数据源，其他页面更新保留 AG 的缓存与分页状态。 */
export function useScopedGridQuery<T>(
  read: (
    input: GridQuery,
    signal: AbortSignal,
    scope: string,
  ) => Promise<PagedResult<T>>,
  scope: string,
) {
  return useCallback(
    (input: GridQuery, signal: AbortSignal) => read(input, signal, scope),
    [read, scope],
  );
}
