import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getVoucher, getVouchers, mapVoucherStatus } from './finance';

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
      items: [
        {
          id: 'v2',
          bizType: 'demo.credit',
          bizRefId: 'X',
          phase: 'pay',
          status: 1,
        },
      ],
    });
    const page = await getVouchers();
    expect(page.items[0].status).toBe('Reversed');
  });

  it('流水筛选使用完整契约及 PascalCase 查询参数', async () => {
    await getVouchers({
      voucherId: 'voucher',
      userId: 'user',
      email: 'user@example.test',
      phase: 'pay',
      startTime: '2026-11-01T00:00:00Z',
    });
    expect(request).toHaveBeenCalledWith(
      '/api/app/fin-voucher',
      expect.objectContaining({
        params: expect.objectContaining({
          VoucherId: 'voucher',
          UserId: 'user',
          Email: 'user@example.test',
          Phase: 'pay',
          StartTime: '2026-11-01T00:00:00Z',
        }),
      }),
    );
  });

  it('保留精确金额、账户、余额桶和变动后余额', async () => {
    request.mockResolvedValue({
      id: 'voucher',
      userId: 'user',
      currency: 'USDT',
      amountText: '100.000000000000000001',
      totalDebitText: '100.000000000000000001',
      totalCreditText: '100.000000000000000001',
      entryCount: 2,
      entries: [
        {
          seq: 1,
          accountCode: '2201',
          accountName: '用户余额',
          ownerType: 1,
          ownerId: 'user',
          bucket: 'Available',
          debitText: '0',
          creditText: '100.000000000000000001',
          balanceAfterText: '120.000000000000000001',
        },
      ],
    });
    const voucher = await getVoucher('voucher');
    expect(voucher).toMatchObject({
      userId: 'user',
      currency: 'USDT',
      entryCount: 2,
      amountText: '100.000000000000000001',
    });
    expect(voucher.entries?.[0]).toMatchObject({
      ownerId: 'user',
      bucket: 'Available',
      creditText: '100.000000000000000001',
      balanceAfterText: '120.000000000000000001',
    });
  });
});
