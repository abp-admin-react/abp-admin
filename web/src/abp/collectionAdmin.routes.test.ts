import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import {
  getCollectionPayment,
  getCollectionPayments,
  getCollectionRefunds,
  getMemberBalances,
  getMemberTxns,
  getTreasuryDashboard,
  queryCollectionPayment,
  queryCollectionRefund,
  requestCollectionRefund,
} from './collectionAdmin';

// 会员资金查询端点形状锁：非空 Guid 参数被 ABP 生成路径段（query 形态不命中路由，
// 页面曾因兜底 HTML 把 items 踩崩——E2E 实测同 approveLargeDeposit 教训）
describe('collectionAdmin endpoint URL shapes', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('uses the Collection admin API with pagination and filters', async () => {
    await getCollectionPayments({
      skipCount: 20,
      maxResultCount: 20,
      filter: 'COL-1',
      status: 'Pending',
      channel: 'WechatNative',
    });
    expect(request).toHaveBeenCalledWith('/api/app/collection-payment-admin', {
      params: {
        SkipCount: 20,
        MaxResultCount: 20,
        Filter: 'COL-1',
        Status: 'Pending',
        Channel: 'WechatNative',
      },
    });
  });

  it('looks up a receipt by ID in the detail path', async () => {
    await getCollectionPayment('receipt-id');
    expect(request).toHaveBeenCalledWith(
      '/api/app/collection-payment-admin/receipt-id',
    );
  });

  it('uses explicit provider query and refund routes and preserves a refund retry key and amount', async () => {
    const input = {
      collectionId: 'receipt',
      requestId: 'stable-key',
      amount: '12.30',
      currency: 1,
    };
    await queryCollectionPayment('receipt');
    await getCollectionRefunds('receipt');
    await requestCollectionRefund(input);
    await requestCollectionRefund(input);
    await queryCollectionRefund('refund');
    expect(request.mock.calls).toEqual([
      ['/api/app/collection-payments/receipt/query', { method: 'POST' }],
      ['/api/app/collection-payments/receipt/refunds'],
      [
        '/api/app/collection-refunds',
        { method: 'POST', data: input, skipErrorHandler: true },
      ],
      [
        '/api/app/collection-refunds',
        { method: 'POST', data: input, skipErrorHandler: true },
      ],
      ['/api/app/collection-refunds/refund/query', { method: 'POST' }],
    ]);
  });

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
