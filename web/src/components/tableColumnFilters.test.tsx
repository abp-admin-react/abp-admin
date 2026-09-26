import { fireEvent, render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { enumFilters, textFilter } from './tableColumnFilters';

describe('enumFilters', () => {
  it('映射为 { text, value }，值统一字符串化', () => {
    expect(enumFilters({ 有异常: 'true', 无异常: 'false', 数量: 3 })).toEqual([
      { text: '有异常', value: 'true' },
      { text: '无异常', value: 'false' },
      { text: '数量', value: '3' },
    ]);
  });

  it('空映射返回空数组', () => {
    expect(enumFilters({})).toEqual([]);
  });
});

describe('textFilter', () => {
  it('返回 filterDropdown + filterIcon 结构', () => {
    const col = textFilter();
    expect(typeof col.filterDropdown).toBe('function');
    expect(col.filterIcon).toBeDefined();
  });

  function openPanel() {
    const props = {
      setSelectedKeys: vi.fn(),
      selectedKeys: [] as unknown[],
      confirm: vi.fn(),
      clearFilters: vi.fn(),
      close: vi.fn(),
      visible: true,
    } as any;
    const view = render((textFilter() as any).filterDropdown(props));
    // 面板按钮：[筛选, 重置]
    const buttons = view.getAllByRole('button');
    const ok = buttons.find((b) => b.textContent?.includes('筛'));
    const reset = buttons.find((b) => b.textContent?.includes('重'));
    return { props, view, ok: ok as HTMLElement, reset: reset as HTMLElement };
  }

  it('面板「筛选」= confirm + close', () => {
    const { props, ok } = openPanel();
    fireEvent.click(ok);
    expect(props.confirm).toHaveBeenCalled();
    expect(props.close).toHaveBeenCalled();
    expect(props.clearFilters).not.toHaveBeenCalled();
    expect(props.setSelectedKeys).not.toHaveBeenCalled();
  });

  it('面板「重置」= clearFilters + confirm + close', () => {
    const { props, reset } = openPanel();
    fireEvent.click(reset);
    expect(props.clearFilters).toHaveBeenCalled();
    expect(props.confirm).toHaveBeenCalled();
    expect(props.close).toHaveBeenCalled();
  });

  it('面板输入框变化写 selectedKeys', () => {
    const { props, view } = openPanel();
    const input = view.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'alice' } });
    expect(props.setSelectedKeys).toHaveBeenCalledWith(['alice']);
  });
});
