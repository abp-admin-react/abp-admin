import { describe, expect, it } from 'vitest';
import { isSafeMenuPath } from './redirect';

describe('isSafeMenuPath', () => {
  it('接受站内相对路径（含查询与锚点）', () => {
    expect(isSafeMenuPath('/administration')).toBe(true);
    expect(isSafeMenuPath('/users/roles?next=1#frag')).toBe(true);
  });

  it.each([
    ['协议相对', '//evil.com'],
    ['反斜杠相对', '/\\evil.com'],
    ['制表走私', '/\t/evil.com'],
    ['换行走私', '/\n/evil.com'],
    ['回车走私', '/\r/evil.com'],
    ['绝对 URL', 'https://evil.com'],
    ['三条斜杠', '///evil.com'],
    ['无斜杠开头', 'evil.com'],
    ['空串', ''],
    ['null', null],
    ['undefined', undefined],
  ])('拒绝 %s：%j', (_label, value) => {
    expect(isSafeMenuPath(value)).toBe(false);
  });
});
