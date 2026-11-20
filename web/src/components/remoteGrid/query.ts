import type { IGetRowsParams } from 'ag-grid-community';
import type {
  GridCondition,
  GridFilterOperation,
  GridQuery,
} from '@/abp/gridQuery';

const operations: Record<string, GridFilterOperation | undefined> = {
  equals: 'eq',
  notEqual: 'ne',
  contains: 'contains',
  notContains: 'notContains',
  startsWith: 'startsWith',
  endsWith: 'endsWith',
  greaterThan: 'gt',
  greaterThanOrEqual: 'gte',
  lessThan: 'lt',
  lessThanOrEqual: 'lte',
  inRange: 'between',
};

interface NativeCondition {
  type: string;
  filterType: string;
  filter?: string | number;
  filterTo?: string | number;
  dateFrom?: string;
  dateTo?: string;
}

function condition(model: NativeCondition): GridCondition {
  if (model.type.startsWith('choice:')) {
    return { op: 'eq', value: model.type.slice('choice:'.length) };
  }
  if (model.type === 'isTrue' || model.type === 'isFalse') {
    return { op: 'eq', value: String(model.type === 'isTrue') };
  }
  const op = operations[model.type];
  if (!op) throw new Error('不支持的筛选条件');
  // AG 日期模型带本地零点字符串，接口按 UTC 日历日查询；金额的原始十进制文本直接传给后端。
  const isDate = model.filterType === 'date';
  const value = isDate ? model.dateFrom?.slice(0, 10) : model.filter;
  const valueTo = isDate ? model.dateTo?.slice(0, 10) : model.filterTo;
  if (value == null || (op === 'between' && valueTo == null)) {
    throw new Error('筛选条件缺少输入值');
  }
  return {
    op,
    value: String(value),
    ...(op === 'between' ? { valueTo: String(valueTo) } : {}),
  };
}

export function toGridQuery(
  params: Pick<
    IGetRowsParams,
    'startRow' | 'endRow' | 'filterModel' | 'sortModel'
  >,
): GridQuery {
  const models: Record<
    string,
    | NativeCondition
    | {
        operator: 'AND' | 'OR';
        conditions: NativeCondition[];
      }
  > = params.filterModel ?? {};
  return {
    skipCount: params.startRow,
    maxResultCount: params.endRow - params.startRow,
    sort: params.sortModel.map(({ colId, sort }) => ({
      field: colId,
      direction: sort,
    })),
    filters: Object.entries(models).map(([field, model]) => ({
      field,
      join: 'operator' in model && model.operator === 'OR' ? 'or' : 'and',
      conditions:
        'conditions' in model
          ? model.conditions.map(condition)
          : [condition(model)],
    })),
  };
}
