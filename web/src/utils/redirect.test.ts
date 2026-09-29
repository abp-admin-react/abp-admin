import { describe, expect, it } from 'vitest';
import {
  consumeIntendedRedirect,
  isSafeMenuPath,
  isSafeRedirectTarget,
  REDIRECT_STORAGE_KEY,
  storeIntendedRedirect,
} from './redirect';

/**
 * post-auth open redirect 的回归锁：表里的每一种形态都曾在真实代码里漏过——
 * // 是经典协议相对，/\ 与制表符/换行/回车走私依赖 WHATWG 解析器的归一化行为。
 */
describe('redirect utils', () => {
  it('接受站内绝对路径（含查询串与锚点）', () => {
    expect(isSafeRedirectTarget('/administration')).toBe(true);
    expect(isSafeRedirectTarget('/users/roles?next=1#frag')).toBe(true);
  });

  it.each([
    ['协议相对', '//evil.com'],
    ['反斜杠相对（WHATWG 归一成 //）', '/\\evil.com'],
    ['制表符走私', '/\t/evil.com'],
    ['换行走私', '/\n/evil.com'],
    ['回车走私', '/\r/evil.com'],
    ['绝对 URL', 'https://evil.com'],
    ['同为斜杠开头的他站形态', '///evil.com'],
    ['非斜杠开头', 'evil.com'],
    ['空串', ''],
  ])('拒绝 %s：%j', (_label, value) => {
    expect(isSafeRedirectTarget(value)).toBe(false);
  });

  it('null/undefined 拒绝', () => {
    expect(isSafeRedirectTarget(null)).toBe(false);
    expect(isSafeRedirectTarget(undefined)).toBe(false);
  });

  it('storeIntendedRedirect 只存合法目标；consumeIntendedRedirect 读后即清、非法值落回默认', () => {
    storeIntendedRedirect('/\t/evil.com');
    expect(sessionStorage.getItem(REDIRECT_STORAGE_KEY)).toBeNull();

    storeIntendedRedirect('/users/roles');
    expect(consumeIntendedRedirect()).toBe('/users/roles');
    expect(sessionStorage.getItem(REDIRECT_STORAGE_KEY)).toBeNull();

    // 其它入口写入的脏值：读侧同样校验，不因写过就信任
    sessionStorage.setItem(REDIRECT_STORAGE_KEY, '/\\evil.com');
    expect(consumeIntendedRedirect()).toBe('/administration');
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
