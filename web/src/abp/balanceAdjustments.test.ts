import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({
  request: (...args: unknown[]) => request(...args),
}));

import {
  approveBalanceAdjustment,
  createBalanceAdjustment,
  getBalanceAdjustments,
  rejectBalanceAdjustment,
} from './balanceAdjustments';

describe('用户余额调账接口', () => {
  beforeEach(() => request.mockReset().mockResolvedValue({}));

  it('查询保留待审批状态 0 和筛选参数', async () => {
    await getBalanceAdjustments({
      skipCount: 20,
      maxResultCount: 10,
      status: 0,
      userId: 'user',
      filter: 'reason',
    });
    expect(request).toHaveBeenCalledWith(
      '/api/app/collection-balance-adjustment',
      {
        params: {
          SkipCount: 20,
          MaxResultCount: 10,
          Status: 0,
          UserId: 'user',
          Filter: 'reason',
        },
      },
    );
  });

  it('创建保留带符号小数原字符串和幂等号', async () => {
    const input = {
      requestId: 'request',
      userId: 'user',
      currency: 3,
      amount: '-0.000000000000000001',
      reason: '人工扣减',
    };
    await createBalanceAdjustment(input);
    expect(request).toHaveBeenCalledWith(
      '/api/app/collection-balance-adjustment',
      { method: 'POST', data: input },
    );
  });

  it('审批与拒绝使用各自端点', async () => {
    await approveBalanceAdjustment('adjustment');
    await rejectBalanceAdjustment('adjustment', '资料不符');
    expect(request.mock.calls).toEqual([
      [
        '/api/app/collection-balance-adjustment/adjustment/approve',
        { method: 'POST' },
      ],
      [
        '/api/app/collection-balance-adjustment/adjustment/reject',
        { method: 'POST', data: { reason: '资料不符' } },
      ],
    ]);
  });
});
