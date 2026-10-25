import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { approveLargeDeposit } from './chainAdmin';

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
});
