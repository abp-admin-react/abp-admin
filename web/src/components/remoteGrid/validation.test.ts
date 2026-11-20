import { describe, expect, it } from 'vitest';
import type { GridQuery } from '@/abp/gridQuery';
import { type ColumnKind, inputHint, serverHint } from './validation';

const columns = [
  { field: 'id', headerName: '会员 ID', kind: 'uuid' },
  { field: 'amount', headerName: '余额', kind: 'decimal' },
] satisfies { field: string; headerName: string; kind: ColumnKind }[];

function input(field: string, value: string, valueTo?: string): GridQuery {
  return {
    skipCount: 0,
    maxResultCount: 20,
    sort: [],
    filters: [
      {
        field,
        join: 'and',
        conditions: [{ op: valueTo ? 'between' : 'eq', value, valueTo }],
      },
    ],
  };
}

describe('网格输入校验', () => {
  it('本地识别未输完的 UUID 和金额区间两端的非法字符', () => {
    expect(inputHint(input('id', '1fba'), columns)).toContain('完整的 UUID');
    expect(
      inputHint(input('id', '23ab946e-cb89-4f83-b767-cdb76cb98c83'), columns),
    ).toBeUndefined();
    expect(inputHint(input('amount', '1', '2x'), columns)).toContain(
      '「余额」',
    );
    expect(
      inputHint(input('amount', '-.5', '+9007199254740993.89'), columns),
    ).toBeUndefined();
  });
  it('只解释后端 400 校验信封，未知成员使用本地通用文案', () => {
    const data = {
      error: { validationErrors: [{ members: ['query'], message: 'secret' }] },
    };
    expect(serverHint({ response: { status: 400, data } }, columns)).toBe(
      '查询条件无效，请检查关键词、各列筛选和排序。',
    );
    expect(
      serverHint({ response: { status: 500, data } }, columns),
    ).toBeUndefined();
    expect(serverHint(new Error('secret'), columns)).toBeUndefined();
  });
});
