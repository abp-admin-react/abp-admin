import { describe, expect, it } from 'vitest';
import { sorterToAbpSorting } from './sorting';

describe('sorterToAbpSorting', () => {
  it('转换单字段排序', () => {
    expect(sorterToAbpSorting({ UserName: 'descend' })).toBe('UserName desc');
    expect(sorterToAbpSorting({ CreationTime: 'ascend' })).toBe(
      'CreationTime asc',
    );
  });

  it('转换多字段排序', () => {
    expect(
      sorterToAbpSorting({ UserName: 'descend', CreationTime: 'ascend' }),
    ).toBe('UserName desc,CreationTime asc');
  });

  it('忽略空值与非法排序方向', () => {
    expect(sorterToAbpSorting({ UserName: undefined, Email: 'ascend' })).toBe(
      'Email asc',
    );
    expect(sorterToAbpSorting({})).toBeUndefined();
    expect(sorterToAbpSorting()).toBeUndefined();
    expect(sorterToAbpSorting(null)).toBeUndefined();
  });
});
