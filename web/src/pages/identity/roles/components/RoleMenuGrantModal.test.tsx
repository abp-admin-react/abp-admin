import { render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type * as menusApi from '@/abp/menus';

// 角色侧「菜单权限」弹窗的两条数据安全契约（OCR function 镜头钉住的防线）：
// 1. 读取失败必须关闭弹窗——防「空勾选 + 保存」静默清空该角色全部授权；
// 2. 保存 payload = 勾选 id 精确集（checkStrictly 逐节点语义，无父子推导）。

const { default: RoleMenuGrantModal } = await import('./RoleMenuGrantModal');

const role = { id: 'role-1', name: 'viewer' } as any;

const viewItem = (over: Partial<menusApi.RoleMenuGrantItemDto>) => ({
  id: 'm-1',
  parentId: null,
  type: 2,
  title: '菜单1',
  orderNo: 10,
  isEnabled: true,
  isHide: false,
  isGranted: false,
  isControlled: false,
  ...over,
});

const mocks = vi.hoisted(() => ({
  getRoleMenuGrantView: vi.fn(),
  updateRoleMenuGrants: vi.fn(),
}));

vi.mock('@/abp/menus', async () => {
  const actual =
    await vi.importActual<typeof import('@/abp/menus')>('@/abp/menus');
  return {
    ...actual,
    getRoleMenuGrantView: mocks.getRoleMenuGrantView,
    updateRoleMenuGrants: mocks.updateRoleMenuGrants,
  };
});

const renderModal = (onClose: () => void) =>
  render(
    <AntdApp>
      <RoleMenuGrantModal role={role} onClose={onClose} />
    </AntdApp>,
  );

describe('RoleMenuGrantModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('读取失败时关闭弹窗且绝不触发保存（防空勾选覆盖授权）', async () => {
    mocks.getRoleMenuGrantView.mockRejectedValue(
      Object.assign(new Error('boom'), { response: { status: 500 } }),
    );
    const onClose = vi.fn();
    renderModal(onClose);

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(mocks.updateRoleMenuGrants).not.toHaveBeenCalled();
  });

  it('保存把勾选集精确映射为 payload（checkStrictly 逐节点语义）', async () => {
    // 视图 3 节点：父目录未授权、m-1 已授权（isControlled）、m-2 未授权——
    // 回显勾选集应精确为 [m-1]，父节点不被推导勾选
    mocks.getRoleMenuGrantView.mockResolvedValue({
      items: [
        viewItem({ id: 'm-parent', title: '目录X', type: 1 }),
        viewItem({
          id: 'm-1',
          parentId: 'm-parent',
          isGranted: true,
          isControlled: true,
        }),
        viewItem({ id: 'm-2', parentId: 'm-parent' }),
      ],
    });
    mocks.updateRoleMenuGrants.mockResolvedValue(undefined);
    const onClose = vi.fn();
    renderModal(onClose);

    // 等视图数据渲染成树（节点标题出现）
    await waitFor(() => expect(screen.getByText('目录X')).toBeTruthy());

    const saveBtn = screen
      .getAllByRole('button')
      .find((b) => b.textContent?.replace(/\s/g, '') === '保存');
    expect(saveBtn).toBeTruthy();
    if (!saveBtn) return;
    saveBtn.click();

    await waitFor(() =>
      expect(mocks.updateRoleMenuGrants).toHaveBeenCalledWith('role-1', [
        'm-1',
      ]),
    );
  });
});
