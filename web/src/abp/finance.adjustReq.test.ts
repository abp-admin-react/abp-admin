import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { createAdjust, submitAdjust } from './finance';

// 调账写接口由页面 onActionError 出中文提示，必须跳过全局报错，避免 403 双提示
describe('adjust write requests skip global error handler', () => {
  beforeEach(() => request.mockReset().mockResolvedValue({ id: 'a1', status: 0 }));

  it('createAdjust', async () => {
    await createAdjust({ amount: 1, currency: 'CNY' } as any).catch(() => {});
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST', skipErrorHandler: true });
  });

  it('submitAdjust', async () => {
    await submitAdjust('a1').catch(() => {});
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST', skipErrorHandler: true });
  });
});