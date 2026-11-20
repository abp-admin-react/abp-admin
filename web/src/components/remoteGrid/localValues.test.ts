import { describe, expect, it } from 'vitest';
import { compareDecimal, localColumn, matchesKeyword } from './localValues';

describe('完整明细的列查询', () => {
  it('金额按数据库原精度排序和区间筛选', () => {
    const low = '9007199254740993.000000000000000001';
    const high = '9007199254740993.000000000000000002';
    expect(compareDecimal(low, high)).toBeLessThan(0);
    expect(compareDecimal('-1.10', '-1.100')).toBe(0);
    expect(compareDecimal('-12', '-2')).toBeLessThan(0);
    const opts = localColumn({ kind: 'decimal' }, {}).filterParams
      .filterOptions;
    const between = opts.find(
      (o: { displayKey: string }) => o.displayKey === 'decimal-between',
    ).predicate;
    expect(between([low, high], high)).toBe(true);
    expect(between([high, high], low)).toBe(false);
    expect(between([low, high], null)).toBe(false);
  });

  it('完整关键词在许可字段内匹配，可以选取嵌套字段', () => {
    const row = {
      name: 'ALPHA beta',
      label: { key: 'code-100' },
      private: 'other',
    };
    expect(matchesKeyword(row, ['name', 'label.key'], 'ha BE')).toBe(true);
    expect(matchesKeyword(row, ['label.key'], '100')).toBe(true);
    expect(matchesKeyword(row, ['name', 'label.key'], 'beta code')).toBe(false);
    expect(matchesKeyword(row, ['name', 'label.key'], 'other')).toBe(false);
  });

  it('日期列按 UTC 日历日比较，排序保留时间', () => {
    const col = localColumn({ kind: 'date' }, {});
    const sample = '2025-01-02T03:00:00+08:00';
    expect(col.filterParams.comparator(new Date(2025, 0, 1), sample)).toBe(0);
    expect(col.filterParams.isValidDate(null)).toBe(false);
  });
});
