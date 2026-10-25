import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import {
  approveLargeDeposit,
  getTreasuryDashboard,
  retrySweepTask,
  setChainSwitches,
  setDepositPolicy,
} from './chainAdmin';

// 链网运维写端点的 URL 形状锁（E2E 实测：单 Guid 参数端点 ?id= 形态 302 假成功，必须路径段）
describe('chainAdmin write endpoint URL shapes', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('approveLargeDeposit 使用路径段', async () => {
    await approveLargeDeposit('d1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/d1/approve-large-deposit',
    );
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST' });
  });

  it('retrySweepTask 使用路径段（同 302 教训）', async () => {
    await retrySweepTask('t1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/t1/retry',
    );
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST' });
  });

  it('setDepositPolicy 走 query 参数（ABP 原始参数绑定，同 set-min-deposit 形态）', async () => {
    await setDepositPolicy('0.001', '5000');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/set-deposit-policy',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      params: { feeRate: '0.001', autoCreditMaxAmount: '5000' },
    });
  });

  it('getTreasuryDashboard 指向收款查看权限位的服务（随端点迁移锁 BASE）', async () => {
    await getTreasuryDashboard();
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-balance-admin/dashboard',
    );
  });

  it('setChainSwitches 只传提供的参数', async () => {
    await setChainSwitches({ scanEnabled: true });
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/set-chain-switches',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      params: { scanEnabled: true },
    });
  });
});
