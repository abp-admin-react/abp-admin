import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('@umijs/max', () => ({ request: vi.fn() }));

import { isMock, resolveFinanceUserId } from './finance';

// 开关语义锁：默认 live；mock 仅由 localStorage `abpadmin:finance.useMock`（storageKeys
// .financeUseMock）= '1' 开启；live 无传入 id 时必须返回空串、绝不回落假身份——
// resolveFinanceUserId 的返回值驱动 vouchers 页自批拦截与 reopen 权限比较。
describe('isMock / resolveFinanceUserId', () => {
  afterEach(() => {
    localStorage.clear();
  });

  it('默认 live：开关关闭、无传入 id 返回空串', () => {
    expect(isMock()).toBe(false);
    expect(resolveFinanceUserId(null)).toBe('');
  });

  it('显式传入 id 优先（app.tsx 已映射 initialState.userid）', () => {
    expect(resolveFinanceUserId({ userid: 'u1' })).toBe('u1');
    expect(resolveFinanceUserId({ id: 'u2' })).toBe('u2');
  });

  it("localStorage abpadmin:finance.useMock='1' 时为 mock，id 回落种子制单人", () => {
    localStorage.setItem('abpadmin:finance.useMock', '1');
    expect(isMock()).toBe(true);
    expect(resolveFinanceUserId(null)).toBe('user-accountant');
  });

  it('显式 id 不受 mock 开关影响', () => {
    localStorage.setItem('abpadmin:finance.useMock', '1');
    expect(resolveFinanceUserId({ userid: 'live-user' })).toBe('live-user');
  });
});
