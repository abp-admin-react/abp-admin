import { describe, expect, it, vi } from 'vitest';

/**
 * web/src/abp/settingUi.ts 纯函数（fromSettingString / toSettingString）的往返测试。
 * 服务层（@/services/abpadmin/settingUi，依赖 @umijs/max）mock 掉——这里只验证
 * string ⇄ 表单值 的类型转换契约，重点钉住 Number 空串守卫：
 * Number('') === 0，不拦会把"清空"渲染成 0 并被保存回写（真实回归过）。
 */

vi.mock('@umijs/max', () => ({ request: vi.fn() }));
vi.mock('@/services/abpadmin/settingUi', () => ({
  getApiSettingUi: vi.fn(),
  putApiSettingUiResetSettingValues: vi.fn(),
  putApiSettingUiSetSettingValues: vi.fn(),
}));

const { fromSettingString, toSettingString, SettingUiComponentTypes } = await import(
  './settingUi'
);

describe('fromSettingString', () => {
  it('空串/纯空白数字值应转 undefined（而不是 0）', () => {
    expect(fromSettingString('', SettingUiComponentTypes.Number)).toBeUndefined();
    expect(fromSettingString('  ', SettingUiComponentTypes.Number)).toBeUndefined();
  });

  it('非数字数字值应转 undefined，合法数字正常转换', () => {
    expect(fromSettingString('abc', SettingUiComponentTypes.Number)).toBeUndefined();
    expect(fromSettingString('42', SettingUiComponentTypes.Number)).toBe(42);
    // 合法存储的 "0" 仍渲染为 0——与"清空"区分
    expect(fromSettingString('0', SettingUiComponentTypes.Number)).toBe(0);
  });

  it('checkbox 按布尔转换', () => {
    expect(fromSettingString('true', SettingUiComponentTypes.Checkbox)).toBe(true);
    expect(fromSettingString('False', SettingUiComponentTypes.Checkbox)).toBe(false);
  });

  it('text 原样透传，undefined/null 输入返回 undefined', () => {
    expect(fromSettingString('plain', SettingUiComponentTypes.Text)).toBe('plain');
    expect(fromSettingString(undefined, SettingUiComponentTypes.Text)).toBeUndefined();
  });
});

describe('toSettingString', () => {
  it('布尔转 true/false，undefined/null 转空串，数字转十进制串', () => {
    expect(toSettingString(true)).toBe('true');
    expect(toSettingString(false)).toBe('false');
    expect(toSettingString(undefined)).toBe('');
    expect(toSettingString(null)).toBe('');
    expect(toSettingString(42)).toBe('42');
  });

  it('往返：合法数字与布尔应稳定', () => {
    expect(fromSettingString(toSettingString(7), SettingUiComponentTypes.Number)).toBe(7);
    expect(fromSettingString(toSettingString(true), SettingUiComponentTypes.Checkbox)).toBe(true);
  });
});
