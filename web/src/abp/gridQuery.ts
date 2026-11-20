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
