import { describe, expect, it } from 'vitest';
import {
  consumeIntendedRedirect,
  DEFAULT_LANDING_PATH,
  isSafeMenuPath,
  isSafeRedirectTarget,
  REDIRECT_STORAGE_KEY,
  resolvePostLoginLandingPath,
  storeIntendedRedirect,
} from './redirect';

/**
 * post-auth open redirect 的回归：下面每一条走私形态都应被实锤拒绝，否则
 * // 是协议相对、/\ 被规范成 //、空白走私经 WHATWG 解析后的归一都会成为洞。
 */
describe('redirect utils', () => {
  it('接受站内相对路径（含查询与锚点）', () => {
    expect(isSafeRedirectTarget('/administration')).toBe(true);
    expect(isSafeRedirectTarget('/users/roles?next=1#frag')).toBe(true);
  });

  it.each([
    ['协议相对', '//evil.com'],
    ['反斜杠相对（WHATWG 归一成 //）', '/\\evil.com'],
    ['制表走私', '/\t/evil.com'],
    ['换行走私', '/\n/evil.com'],
    ['回车走私', '/\r/evil.com'],
    ['绝对 URL', 'https://evil.com'],
    ['同为斜杠开头的跨站形态', '///evil.com'],
    ['无斜杠开头', 'evil.com'],
    ['空串', ''],
  ])('拒绝 %s：%j', (_label, value) => {
    expect(isSafeRedirectTarget(value)).toBe(false);
  });

  it('null/undefined 拒绝', () => {
    expect(isSafeRedirectTarget(null)).toBe(false);
    expect(isSafeRedirectTarget(undefined)).toBe(false);
  });

  it('storeIntendedRedirect 只存合法目标；consumeIntendedRedirect 读后清、非法值回默认', () => {
    storeIntendedRedirect('/\t/evil.com');
    expect(sessionStorage.getItem(REDIRECT_STORAGE_KEY)).toBeNull();

    storeIntendedRedirect('/users/roles');
    expect(consumeIntendedRedirect()).toBe('/users/roles');
    expect(sessionStorage.getItem(REDIRECT_STORAGE_KEY)).toBeNull();

    // 模拟手工写入脏值：仍走同源校验，回落到默认兜底
    sessionStorage.setItem(REDIRECT_STORAGE_KEY, '/\\evil.com');
    expect(consumeIntendedRedirect()).toBe('/welcome');
    expect(consumeIntendedRedirect('/')).toBe('/');
  });

  it('isSafeMenuPath 与回跳同一口径（第二道防线不弱于第一道）', () => {
    expect(isSafeMenuPath('/system/users')).toBe(true);
    expect(isSafeMenuPath('/\\evil.com')).toBe(false);
    expect(isSafeMenuPath('/\t/evil.com')).toBe(false);
    expect(isSafeMenuPath('//evil.com')).toBe(false);
    expect(isSafeMenuPath('https://evil.com')).toBe(false);
    expect(isSafeMenuPath(undefined)).toBe(false);
    expect(isSafeMenuPath(null)).toBe(false);
  });
});

describe('resolvePostLoginLandingPath', () => {
  it('有 Finance.Ledger.View → A1 资金总览', () => {
    expect(
      resolvePostLoginLandingPath({ 'Finance.Ledger.View': true }),
    ).toBe('/finance/overview');
  });

  it('仅 Account.Manage → A2 科目', () => {
    expect(
      resolvePostLoginLandingPath({ 'Finance.Account.Manage': true }),
    ).toBe('/finance/accounts');
  });

  it('仅 BizDoc.View → A6（代理常见）', () => {
    expect(
      resolvePostLoginLandingPath({ 'Finance.BizDoc.View': true }),
    ).toBe('/finance/biz-docs');
  });

  it('按菜单顺序挑第一页：Rule 优先于 BizDoc', () => {
    expect(
      resolvePostLoginLandingPath({
        'Finance.Rule.View': true,
        'Finance.BizDoc.View': true,
      }),
    ).toBe('/finance/rules');
  });

  it('无财务权限 → /welcome 兜底', () => {
    expect(resolvePostLoginLandingPath({})).toBe(DEFAULT_LANDING_PATH);
    expect(resolvePostLoginLandingPath(undefined)).toBe(DEFAULT_LANDING_PATH);
  });

  it('financeShell=true 时落到 A1（与菜单放开一致）', () => {
    expect(resolvePostLoginLandingPath({}, { financeShell: true })).toBe(
      '/finance/overview',
    );
  });
});
