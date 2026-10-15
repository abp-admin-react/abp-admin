import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getVouchers, mapVoucherStatus } from './finance';

// OCR E2E 回归锁：live 凭证 DTO 归一（本缺陷曾让列表恒显「已冲销」、借贷合计列空）。
// mapVoucherStatus 五态 + fromLiveVoucher 字段名映射（后端 totalDebit/totalCredit → UI）。

describe('mapVoucherStatus', () => {
  it('数字枚举：0=Posted、1=Reversed', () => {
    expect(mapVoucherStatus(0)).toBe('Posted');
    expect(mapVoucherStatus(1)).toBe('Reversed');
  });

  it('字符串原样识别', () => {
    expect(mapVoucherStatus('Posted')).toBe('Posted');
    expect(mapVoucherStatus('Reversed')).toBe('Reversed');
  });

  it('未知/缺省回落 Posted（方向=可操作态，后端会拒绝不可红冲凭证兜底）', () => {
    expect(mapVoucherStatus(undefined)).toBe('Posted');
    expect(mapVoucherStatus('garbage')).toBe('Posted');
  });
});

describe('voucher live DTO normalization', () => {
  beforeEach(() => {
    request.mockReset().mockResolvedValue({
      totalCount: 1,
      items: [
        {
          id: '3a244ff0-57c5-319c-69ed-d1286b94621d',
          bizType: 'demo.credit',
          bizRefId: 'DEMO-CREDIT-001',
          phase: 'pay',
          status: 0,
          totalDebit: '1250',
          totalCredit: '1250',
          voucherNo: 'V-3A244FF05E',
          periodLabel: '2026-10',
          creationTime: '2026-10-15T04:13:06Z',
          isDemo: true,
        },
      ],
    });
  });

  it('getVouchers live：状态归一 + 借贷合计字段名对齐', async () => {
    const page = await getVouchers();
    const v = page.items[0];
    expect(v.status).toBe('Posted');
    expect(v.debitTotal).toBe(1250);
    expect(v.creditTotal).toBe(1250);
    expect(v.voucherNo).toBe('V-3A244FF05E');
    expect(v.periodLabel).toBe('2026-10');
  });

  it('红冲后的凭证（status=1）归一为 Reversed', async () => {
    request.mockResolvedValue({
      totalCount: 1,
      items: [{ id: 'v2', bizType: 'demo.credit', bizRefId: 'X', phase: 'pay', status: 1 }],
    });
    const page = await getVouchers();
    expect(page.items[0].status).toBe('Reversed');
  });
});
