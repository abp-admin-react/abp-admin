import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import {
  approveAdjust,
  createAdjust,
  deleteAdjust,
  rejectAdjust,
  reopenAdjust,
  reverseAdjust,
  runReconcile,
  submitAdjust,
  updateAdjust,
} from './finance';

// 调账写接口由页面 onActionError 出中文提示，必须跳过全局报错，避免 403 双提示。
// runReconcile 的 skipErrorHandler 曾真实漏过一次（OCR 修复记录），全部写路径逐一上锁。
describe('adjust write requests skip global error handler', () => {
  beforeEach(() => request.mockReset().mockResolvedValue({ id: 'a1', status: 0 }));

  const cases: Array<[string, () => Promise<unknown>]> = [
    ['createAdjust', () => createAdjust({ amount: 1, currency: 'CNY' } as never)],
    ['updateAdjust', () => updateAdjust('a1', { summary: 'x' })],
    ['submitAdjust', () => submitAdjust('a1')],
    ['deleteAdjust', () => deleteAdjust('a1')],
    ['approveAdjust', () => approveAdjust('a1', { id: 'u1', name: '审批人' })],
    ['rejectAdjust', () => rejectAdjust('a1', { id: 'u1', name: '审批人' }, '理由')],
    ['reopenAdjust', () => reopenAdjust('a1', { id: 'u1', canCreate: true })],
    ['reverseAdjust', () => reverseAdjust('a1')],
    ['runReconcile', () => runReconcile({ rangeStart: '2026-01-01T00:00:00Z', rangeEnd: '2026-01-31T23:59:59Z' })],
  ];

  it.each(cases)('%s 携带 skipErrorHandler', async (_name, call) => {
    await call().catch(() => {});
    expect(request.mock.calls[0][1]).toMatchObject({ skipErrorHandler: true });
  });
});
