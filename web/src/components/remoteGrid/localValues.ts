import type { ColDef, IFilterOptionDef } from 'ag-grid-community';
import type { RemoteColumn } from './index';

export type KeywordField<T> = string | ((row: T) => unknown);

function valueAt(row: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>(
      (value, key) =>
        value != null && typeof value === 'object'
          ? (value as Record<string, unknown>)[key]
          : undefined,
      row,
    );
}

/** 与服务端相同，完整关键词须在同一个许可字段内连续出现。 */
export function matchesKeyword<T>(
  row: T,
  fields: readonly KeywordField<T>[],
  keyword: string,
): boolean {
  const term = keyword.toLowerCase();
  return fields.some((field) => {
    const value =
      typeof field === 'function' ? field(row) : valueAt(row, field);
    return value != null && String(value).toLowerCase().includes(term);
  });
}

const collator = new Intl.Collator('zh-CN', {
  numeric: true,
  sensitivity: 'base',
});
const decimalPattern = /^([+-]?)(\d*)(?:\.(\d*))?$/;
function decimalParts(value: unknown) {
  const match = decimalPattern.exec(String(value));
  if (!match || !(match[2] || match[3])) return undefined;
  const whole = match[2].replace(/^0+/, '');
  const fraction = (match[3] ?? '').replace(/0+$/, '');
  return {
    whole,
    fraction,
    sign: (whole || fraction) && match[1] === '-' ? -1 : 1,
  };
}

/** 数据库金额可能超过 IEEE 754 精度。按符号、整数位数和规范化数字比较，保留完整尾数。 */
export function compareDecimal(a: unknown, b: unknown): number {
  const left = decimalParts(a);
  const right = decimalParts(b);
  if (!left || !right) return Number(Boolean(left)) - Number(Boolean(right));
  if (left.sign !== right.sign) return left.sign - right.sign;
  const lexical = (x: string, y: string) => (x === y ? 0 : x < y ? -1 : 1);
  const difference =
    left.whole.length - right.whole.length ||
    lexical(left.whole, right.whole) ||
    lexical(left.fraction, right.fraction);
  return difference === 0 ? 0 : left.sign * difference;
}

const decimalFilters: IFilterOptionDef[] = (
  [
    ['eq', '等于', (n: number) => n === 0],
    ['gt', '大于', (n: number) => n > 0],
    ['gte', '大于或等于', (n: number) => n >= 0],
    ['lt', '小于', (n: number) => n < 0],
    ['lte', '小于或等于', (n: number) => n <= 0],
    ['ne', '不等于', (n: number) => n !== 0],
  ] as const
).map(([key, label, predicate]) => ({
  displayKey: `decimal-${key}`,
  displayName: label,
  numberOfInputs: 1,
  predicate: ([input], value) =>
    Boolean(
      decimalParts(value) &&
        decimalParts(input) &&
        predicate(compareDecimal(value, input)),
    ),
}));
decimalFilters.push({
  displayKey: 'decimal-between',
  displayName: '介于（含端点）',
  numberOfInputs: 2,
  predicate: ([start, end], value) =>
    Boolean(
      decimalParts(value) &&
        decimalParts(start) &&
        decimalParts(end) &&
        compareDecimal(value, start) >= 0 &&
        compareDecimal(value, end) <= 0,
    ),
});

function timestamp(value: unknown): number {
  const time = value == null ? NaN : new Date(String(value)).getTime();
  return Number.isFinite(time) ? time : Number.NEGATIVE_INFINITY;
}

export function localColumn<T>(
  spec: RemoteColumn<T>,
  col: ColDef<T>,
): ColDef<T> {
  if (spec.kind === 'display') return col;
  if (spec.kind === 'decimal' && !spec.choices)
    return {
      ...col,
      comparator: spec.comparator ?? compareDecimal,
      filterParams: { ...col.filterParams, filterOptions: decimalFilters },
    };
  if (spec.kind === 'date')
    return {
      ...col,
      comparator: spec.comparator ?? ((a, b) => timestamp(a) - timestamp(b)),
      filterParams: {
        ...col.filterParams,
        isValidDate: (value: unknown) => Number.isFinite(timestamp(value)),
        comparator: (localDay: Date, value: unknown) =>
          Math.floor(timestamp(value) / 86400000) -
          Date.UTC(
            localDay.getFullYear(),
            localDay.getMonth(),
            localDay.getDate(),
          ) /
            86400000,
      },
    };
  if (spec.kind !== 'number' && spec.kind !== 'boolean' && !spec.comparator) {
    return {
      ...col,
      comparator: (a, b) => collator.compare(String(a ?? ''), String(b ?? '')),
    };
  }
  return col;
}
