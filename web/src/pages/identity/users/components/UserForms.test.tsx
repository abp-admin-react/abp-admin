import type { ProFormInstance } from '@ant-design/pro-components';
import { describe, expect, it, vi } from 'vitest';
import { fillRandomPassword, generateRandomPassword } from './UserForms';

/**
 * 随机密码生成的回归锚：重置密码弹窗的「随机密码」按钮依赖它产出可直接提交的口令——
 * 弱口令会通过 UI 直达后端（本仓库默认密码策略只校验长度，复杂度由生成端保证），
 * 或在生产调回强策略后必被拒。fillRandomPassword 锚定按钮→表单的接线（字段名 + formRef）。
 *
 * 字符集断言与 UserForms.tsx 的 PASSWORD_CHARSETS 是刻意的双处维护：ABP 官方（10.7.0）
 * 没有可对齐的前端密码生成器实现，复杂度契约只存在于本仓库前端，断言必须独立复述而非
 * 从被测常量派生——派生版与被测物同源即恒真，改坏字符集时不会红。
 */
describe('generateRandomPassword', () => {
  const SAMPLES = 50;

  it('长度固定 16，且只含白名单字符', () => {
    for (let i = 0; i < SAMPLES; i++) {
      expect(generateRandomPassword()).toMatch(
        /^[A-Za-z0-9!@#$%^&*\-_=+]{16}$/,
      );
    }
  });

  it('每条都含小写/大写/数字/特殊字符各至少 1 位', () => {
    for (let i = 0; i < SAMPLES; i++) {
      const password = generateRandomPassword();
      expect(password).toMatch(/[a-z]/);
      expect(password).toMatch(/[A-Z]/);
      expect(password).toMatch(/[0-9]/);
      expect(password).toMatch(/[!@#$%^&*\-_=+]/);
    }
  });

  it('不包含易混淆字符（l / I / O / 0 / 1）', () => {
    for (let i = 0; i < SAMPLES; i++) {
      expect(generateRandomPassword()).not.toMatch(/[lIO01]/);
    }
  });

  it('多次调用不产出重复值（随机源有效）', () => {
    const samples = new Set(
      Array.from({ length: 20 }, () => generateRandomPassword()),
    );
    expect(samples.size).toBe(20);
  });

  it('随机源必须走 crypto.getRandomValues（密码材料不落 Math.random）', () => {
    const spy = vi.spyOn(crypto, 'getRandomValues');
    try {
      generateRandomPassword();
      expect(spy).toHaveBeenCalled();
    } finally {
      spy.mockRestore();
    }
  });
});

describe('fillRandomPassword', () => {
  it('把生成的密码写入表单的 password 字段', () => {
    const setFieldsValue = vi.fn();
    fillRandomPassword({ setFieldsValue } as unknown as ProFormInstance);
    expect(setFieldsValue).toHaveBeenCalledTimes(1);
    expect(setFieldsValue).toHaveBeenCalledWith({
      password: expect.stringMatching(/^[A-Za-z0-9!@#$%^&*\-_=+]{16}$/),
    });
  });

  it('表单引用为空时静默安全（弹窗未挂载时按钮不可达，防御可选链语义）', () => {
    expect(() => fillRandomPassword(undefined)).not.toThrow();
  });
});
