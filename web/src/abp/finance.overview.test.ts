import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getFinanceOverview } from './finance';

// OCR E2E 回归锁：live overview 字段归一（该缺陷曾让 live 页面全 0 且恒显「借贷不平衡」告警）。
// 后端 todayIncome/isBalanced/unearnedTotal/... → UI todayIn/balanced/prepaymentTotal/...，
// 与 finance.voucher.test.ts 的凭证归一锁同根因同套路。
describe('overview live DTO normalization', () => {
  beforeEach(() => {
    request.mockReset().mockResolvedValue({
      todayIncome: 188,
      todayExpense: 20,
      isBalanced: false,
      unreconciledCount: 3,
      availableTotal: 160,
      frozenTotal: 30,
      unearnedTotal: 80,
      recognizedIncomeTotal: 55,
    });
  });

  it('字段名对齐 + isBalanced=false 透传为不平衡', async () => {
    const o = await getFinanceOverview();
    expect(o).toEqual({
      todayIn: 188,
      todayOut: 20,
      balanced: false,
      unreconciledCount: 3,
      availableTotal: 160,
      frozenTotal: 30,
      prepaymentTotal: 80,
      recognizedIncome: 55,
    });
  });

  it('isBalanced 缺省按平衡处理（!== false），数值缺省归 0', async () => {
    request.mockResolvedValueOnce({});
    const o = await getFinanceOverview();
    expect(o.balanced).toBe(true);
    expect(o.todayIn).toBe(0);
    expect(o.unreconciledCount).toBe(0);
  });
});
