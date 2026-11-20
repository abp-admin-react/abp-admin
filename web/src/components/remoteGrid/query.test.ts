import { describe, expect, it } from 'vitest';
import { toGridQuery } from './query';

describe('toGridQuery', () => {
  it('保留跨列条件、列内 OR、多列优先级和高精度金额范围', () => {
    const request = toGridQuery({
      startRow: 40,
      endRow: 60,
      sortModel: [
        { colId: 'balance', sort: 'desc' },
        { colId: 'time', sort: 'asc' },
      ],
      filterModel: {
        name: {
          operator: 'OR',
          conditions: [
            { type: 'contains', filter: '甲', filterType: 'text' },
            { type: 'startsWith', filter: '乙', filterType: 'text' },
          ],
        },
        balance: {
          type: 'inRange',
          filterType: 'text',
          filter: '9007199254740993.123456',
          filterTo: '9007199254740993.123457',
        },
        time: {
          type: 'inRange',
          filterType: 'date',
          dateFrom: '2026-11-01 00:00:00',
          dateTo: '2026-11-19 00:00:00',
        },
      },
    });
    expect(request).toEqual({
      skipCount: 40,
      maxResultCount: 20,
      sort: [
        { field: 'balance', direction: 'desc' },
        { field: 'time', direction: 'asc' },
      ],
      filters: [
        {
          field: 'name',
          join: 'or',
          conditions: [
            { op: 'contains', value: '甲' },
            { op: 'startsWith', value: '乙' },
          ],
        },
        {
          field: 'balance',
          join: 'and',
          conditions: [
            {
              op: 'between',
              value: '9007199254740993.123456',
              valueTo: '9007199254740993.123457',
            },
          ],
        },
        {
          field: 'time',
          join: 'and',
          conditions: [
            { op: 'between', value: '2026-11-01', valueTo: '2026-11-19' },
          ],
        },
      ],
    });
  });

  it('0 是有效条件，未知操作不能降级为无条件查询', () => {
    const params = { startRow: 0, endRow: 20, sortModel: [] };
    expect(
      toGridQuery({
        ...params,
        filterModel: {
          value: { type: 'equals', filterType: 'number', filter: 0 },
        },
      }).filters[0].conditions[0].value,
    ).toBe('0');
    expect(() =>
      toGridQuery({
        ...params,
        filterModel: { value: { type: 'unsupported' } },
      }),
    ).toThrow();
  });
});
