import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getMemberBalances, getMemberTxns, getTreasuryDashboard } from './collectionAdmin';

// 会员资金查询端点形状锁：非空 Guid 参数被 ABP 生成路径段（query 形态不命中路由，
// 页面曾因兜底 HTML 把 items 踩崩——E2E 实测同 approveLargeDeposit 教训）
describe('collectionAdmin endpoint URL shapes', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('getMemberBalances 走 userId 路径段', async () => {
    await getMemberBalances('u1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/member-balances/u1',
    );
  });

  it('getTreasuryDashboard 指向本服务 BASE（收款查看权限位）', async () => {
    await getTreasuryDashboard();
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/dashboard',
    );
  });

  it('getMemberTxns 路径段 + 分页 query', async () => {
    await getMemberTxns({ userId: 'u1', skipCount: 20, maxResultCount: 20 });
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/member-txns/u1',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      params: { skipCount: 20, maxResultCount: 20 },
    });
  });
});
