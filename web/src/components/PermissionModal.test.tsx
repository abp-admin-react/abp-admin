import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PermissionGroup, PermissionListResult } from '@/abp/permissions';
import PermissionModal from './PermissionModal';

const mocks = vi.hoisted(() => ({
  getPermissions: vi.fn(),
  updatePermissions: vi.fn(),
}));
vi.mock('@/abp/permissions', () => mocks);

const groups: PermissionGroup[] = [
  {
    name: 'Orders',
    displayName: '订单',
    permissions: [
      { name: 'Orders', displayName: '查看订单', isGranted: false },
      {
        name: 'Orders.Edit',
        displayName: '编辑订单',
        parentName: 'Orders',
        isGranted: false,
      },
    ],
  },
  {
    name: 'Settings',
    displayName: '设置',
    permissions: [
      { name: 'Settings', displayName: '系统设置', isGranted: true },
    ],
  },
];

function result(items = groups): PermissionListResult {
  return { entityDisplayName: '测试角色', groups: items };
}

function modal(providerName = 'R', providerKey = 'role-1') {
  return (
    <AntdApp>
      <PermissionModal
        open
        title="功能权限"
        providerName={providerName}
        providerKey={providerKey}
        onClose={vi.fn()}
      />
    </AntdApp>
  );
}

const saveButton = () => screen.getByRole('button', { name: 'OK' });
const checkbox = (name: string) =>
  within(screen.getByRole('treeitem', { name })).getByRole('checkbox');

describe('按组管理权限', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.getPermissions.mockResolvedValue(result());
    mocks.updatePermissions.mockResolvedValue(undefined);
  });

  it('跨组勾选保留，当前组清空不会清掉其他组，保存包含各组修改', async () => {
    render(modal());
    await waitFor(() => expect(saveButton()).toBeEnabled());
    expect(screen.getByRole('tab', { name: '订单' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    fireEvent.click(screen.getByRole('button', { name: /全\s*选/ }));
    fireEvent.click(screen.getByRole('tab', { name: '设置' }));
    expect(checkbox('系统设置')).toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: /清\s*空/ }));
    fireEvent.click(screen.getByRole('tab', { name: '订单' }));
    expect(checkbox('编辑订单')).toBeChecked();
    expect(screen.getByText('仅当前组')).toBeTruthy();
    fireEvent.click(saveButton());
    await waitFor(() =>
      expect(mocks.updatePermissions).toHaveBeenCalledWith('R', 'role-1', [
        { name: 'Orders', isGranted: true },
        { name: 'Orders.Edit', isGranted: true },
        { name: 'Settings', isGranted: false },
      ]),
    );
  });

  it('全选和清空保持不可编辑的已授权与未授权状态', async () => {
    mocks.getPermissions.mockResolvedValueOnce(
      result([
        {
          name: 'Protected',
          displayName: '受保护权限',
          permissions: [
            {
              name: 'Locked.On',
              displayName: '锁定已授权',
              isGranted: true,
              isEditable: false,
            },
            {
              name: 'Locked.Off',
              displayName: '锁定未授权',
              isGranted: false,
              isEditable: false,
            },
            { name: 'Editable', displayName: '可编辑权限', isGranted: false },
          ],
        },
      ]),
    );
    render(modal());
    await waitFor(() => expect(saveButton()).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /全\s*选/ }));
    expect(checkbox('可编辑权限')).toBeChecked();
    expect(checkbox('锁定已授权')).toBeChecked();
    expect(checkbox('锁定未授权')).not.toBeChecked();
    expect(checkbox('锁定已授权')).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(screen.getByRole('button', { name: /清\s*空/ }));
    expect(checkbox('可编辑权限')).not.toBeChecked();
    expect(checkbox('锁定已授权')).toBeChecked();
    fireEvent.click(saveButton());
    await waitFor(() =>
      expect(mocks.updatePermissions).toHaveBeenCalledWith('R', 'role-1', [
        { name: 'Locked.On', isGranted: true },
        { name: 'Locked.Off', isGranted: false },
        { name: 'Editable', isGranted: false },
      ]),
    );
  });

  it('切换目标读取中及失败后禁止保存，旧目标迟到的响应不能恢复保存', async () => {
    const view = render(modal());
    await waitFor(() => expect(saveButton()).toBeEnabled());
    let finishOldRead!: (value: PermissionListResult) => void;
    mocks.getPermissions.mockImplementationOnce(
      () =>
        new Promise<PermissionListResult>((resolve) => {
          finishOldRead = resolve;
        }),
    );
    view.rerender(modal('R', 'pending-role'));
    expect(saveButton()).toBeDisabled();
    fireEvent.click(saveButton());
    expect(mocks.updatePermissions).not.toHaveBeenCalled();
    mocks.getPermissions.mockRejectedValueOnce(new Error('read failed'));
    view.rerender(modal('U', 'failed-user'));
    expect(await screen.findByText('加载权限失败，请关闭后重试')).toBeTruthy();
    expect(saveButton()).toBeDisabled();
    await act(async () => finishOldRead(result()));
    expect(screen.queryByRole('tab', { name: '订单' })).toBeNull();
    expect(saveButton()).toBeDisabled();
    fireEvent.click(saveButton());
    expect(mocks.updatePermissions).not.toHaveBeenCalled();
  });
});
