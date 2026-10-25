import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getMemberBalances, getMemberTxns } from './collectionAdmin';

// 会员资金查询端点形状锁（query 形态；userId 是 Guid 值参数非路由主键）
describe('collectionAdmin endpoint URL shapes', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('getMemberBalances 带 userId 查询参数', async () => {
    await getMemberBalances('u1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/member-balances',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      params: { userId: 'u1' },
    });
  });

  it('getMemberTxns 分页参数齐带', async () => {
    await getMemberTxns({ userId: 'u1', skipCount: 20, maxResultCount: 20 });
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/member-txns',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      params: { userId: 'u1', skipCount: 20, maxResultCount: 20 },
    });
  });
});
