import type { GridQuery } from '@/abp/gridQuery';

export type ColumnKind =
  | 'text'
  | 'identifier'
  | 'uuid'
  | 'number'
  | 'decimal'
  | 'date'
  | 'boolean'
  | 'display';

interface FilterColumn {
  colId?: string;
  field?: string;
  headerName?: string;
  kind?: ColumnKind;
}

function lookup(columns: FilterColumn[], field: string) {
  return columns.find((col) => (col.colId ?? col.field) === field);
}

const hints: Partial<Record<ColumnKind, string>> = {
  uuid: '请输入完整的 UUID，格式为 8-4-4-4-12 位十六进制字符。',
  decimal: '请输入十进制金额，并检查数值范围和小数精度。',
  number: '请检查数字的范围和精度。',
  date: '请检查日期和区间端点。',
};

function hint(column: FilterColumn) {
  return `「${column.headerName ?? column.field}」筛选值无效。${hints[column.kind ?? 'text'] ?? '请修改或清除此列条件。'}`;
}

const formats: Partial<Record<ColumnKind, RegExp>> = {
  uuid: /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i,
  decimal: /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/,
};

export function inputHint(query: GridQuery, columns: FilterColumn[]) {
  for (const group of query.filters) {
    const column = lookup(columns, group.field);
    const format = column?.kind && formats[column.kind];
    if (
      format &&
      group.conditions.some(
        ({ value, valueTo }) =>
          !format.test(value) || (valueTo != null && !format.test(valueTo)),
      )
    ) {
      return hint(column);
    }
  }
  return undefined;
}

/** 管理端 skipErrorHandler 保留 Axios 的 ABP 信封。只用校验成员定位列，不展示服务端异常详情。 */
export function serverHint(error: unknown, columns: FilterColumn[]) {
  const response = (
    error as {
      response?: {
        status?: number;
        data?: { error?: { validationErrors?: { members?: unknown[] }[] } };
      };
    } | null
  )?.response;
  const failures = response?.data?.error?.validationErrors;
  if (response?.status !== 400 || !Array.isArray(failures) || !failures.length)
    return;
  for (const failure of failures) {
    if (!Array.isArray(failure?.members)) continue;
    for (const field of failure.members) {
      const column = typeof field === 'string' && lookup(columns, field);
      if (column) return hint(column);
    }
  }
  return '查询条件无效，请检查关键词、各列筛选和排序。';
}
