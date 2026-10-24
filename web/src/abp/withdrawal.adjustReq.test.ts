import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import {
  approveWithdraw,
  backfillOpening,
  completeWithdraw,
  refundWithdraw,
  rejectWithdraw,
  retryWithdraw,
  setWithdrawVault,
  WD_STATUS_LABELS,
} from './withdrawal';

// 提现写接口由页面 guard() 出中文提示（熔断/偿付/状态迁移等业务码都要就地展示），
// 必须跳过全局报错避免双提示——finance.adjustReq 同款语义锁，全部写路径逐一上锁。
describe('withdrawal write requests skip global error handler', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  const cases: Array<[string, () => Promise<unknown>]> = [
    ['approveWithdraw', () => approveWithdraw('w1')],
    ['rejectWithdraw', () => rejectWithdraw('w1', '风险拒绝')],
    ['refundWithdraw', () => refundWithdraw('w1', '链上未付出')],
    ['retryWithdraw', () => retryWithdraw('w1')],
    ['completeWithdraw', () => completeWithdraw('w1', '0x' + 'ab'.repeat(32))],
    [
      'setWithdrawVault',
      () => setWithdrawVault('0x' + '1'.repeat(40), '0x' + '2'.repeat(32)),
    ],
    ['backfillOpening', () => backfillOpening()],
  ];

  it.each(cases)('%s 携带 skipErrorHandler', async (_name, call) => {
    await call().catch(() => {});
    expect(request.mock.calls[0][1]).toMatchObject({ skipErrorHandler: true });
  });
});

// 私钥只经 body 传递（不进 URL/访问日志）——配置热钱包的形状锁
describe('setWithdrawVault sends credentials in body only', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('私钥出现在 data 而非 params/url', async () => {
    await setWithdrawVault('0x' + '1'.repeat(40), '0x' + '2'.repeat(32));
    const [url, options] = request.mock.calls[0] as [
      string,
      { data?: unknown; params?: unknown },
    ];
    expect(url).not.toContain('privateKey');
    expect(JSON.stringify(options.params ?? {})).not.toContain('privateKey');
    expect(options.data).toMatchObject({ privateKey: '0x' + '2'.repeat(32) });
  });
});

// 状态色表与后端 WithdrawStatus 七态一一对应（页面渲染唯一口径，漂移即页面出裸键）
describe('WD_STATUS_LABELS covers all withdraw statuses', () => {
  it('七个状态齐全', () => {
    expect(Object.keys(WD_STATUS_LABELS).sort()).toEqual(
      [
        'BroadcastFailed',
        'Broadcasting',
        'Cancelled',
        'Completed',
        'Pending',
        'Processing',
        'Rejected',
      ].sort(),
    );
  });
});
