import { fireEvent, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { describe, expect, it, vi } from 'vitest';
import type { PermissionGroup } from '@/abp/permissions';
import RolePermissionEditorBody from './RolePermissionEditorBody';

const orderGroups: PermissionGroup[] = [
  {
    name: 'Orders',
    displayName: '订单管理',
    permissions: [
      { name: 'Orders', displayName: '查看订单', isGranted: false },
      {
        name: 'Orders.Edit',
        displayName: '编辑订单',
        parentName: 'Orders',
        isGranted: false,
      },
      {
        name: 'Orders.Details',
        displayName: '查看订单明细',
        parentName: 'Orders',
        isGranted: true,
      },
      {
        name: 'Orders.Export',
        displayName: '导出订单',
        parentName: 'Orders',
        isGranted: true,
        isEditable: false,
      },
      {
        name: 'Orders.Cancel',
        displayName: '取消订单',
        parentName: 'Orders',
        isGranted: false,
        isEditable: false,
      },
    ],
  },
  {
    name: 'Settings',
    displayName: '系统设置',
    permissions: [
      { name: 'Settings.View', displayName: '查看设置', isGranted: true },
    ],
  },
];

const finance = (
  viewGranted: boolean,
  viewEditable = true,
): PermissionGroup[] => [
  {
    name: 'Finance',
    displayName: '台账',
    permissions: [
      {
        name: 'Finance.Voucher.View',
        displayName: '查看凭证',
        isGranted: viewGranted,
        isEditable: viewEditable,
      },
      {
        name: 'Finance.Voucher.Reverse',
        displayName: '红冲凭证',
        parentName: 'Finance.Voucher.View',
        isGranted: false,
      },
    ],
  },
];

const checkbox = (name: string) => screen.getByRole('checkbox', { name });
const button = (name: string) => screen.getByRole('button', { name });

function renderBody(
  groups = orderGroups,
  initialChanges = new Map<string, boolean>(),
) {
  const onChange = vi.fn();
  const editor = (changes: ReadonlyMap<string, boolean>) => (
    <ConfigProvider theme={{ token: { motion: false } }}>
      <RolePermissionEditorBody
        groups={groups}
        changes={changes}
        disabled={false}
        onChange={onChange}
      />
    </ConfigProvider>
  );
  const view = render(editor(initialChanges));
  return {
    onChange,
    setChanges: (changes: ReadonlyMap<string, boolean>) =>
      view.rerender(editor(changes)),
  };
}

describe('角色功能权限编辑区', () => {
  it('提交搜索只保留当前筛选和草稿', () => {
    const { onChange } = renderBody();
    const search = screen.getByRole('form', { name: '功能权限筛选' });
    fireEvent.change(screen.getByRole('searchbox'), {
      target: { value: 'Orders.Edit' },
    });
    expect(fireEvent.submit(search)).toBe(false);
    expect(screen.getByRole('searchbox')).toHaveValue('Orders.Edit');
    expect(checkbox('编辑订单')).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });
  it('批量操作只提交本组可编辑且状态变化的权限', () => {
    const { onChange } = renderBody(
      orderGroups,
      new Map([
        ['Orders.Edit', true],
        ['Settings.View', false],
      ]),
    );

    fireEvent.click(button('授予本组 1 项'));
    expect(onChange).toHaveBeenLastCalledWith([
      { name: 'Orders', isGranted: true },
    ]);

    fireEvent.click(button('撤销本组 2 项'));
    expect(onChange).toHaveBeenLastCalledWith([
      { name: 'Orders.Edit', isGranted: false },
      { name: 'Orders.Details', isGranted: false },
    ]);
    expect(checkbox('导出订单')).toBeChecked();
    expect(checkbox('取消订单')).not.toBeChecked();
  });

  it('权限展开和折叠只改变可见项', () => {
    const { onChange } = renderBody();
    expect(checkbox('编辑订单')).toBeInTheDocument();

    fireEvent.click(button('折叠查看订单子权限'));
    expect(button('展开查看订单子权限')).toHaveAttribute(
      'aria-expanded',
      'false',
    );
    expect(screen.queryByRole('checkbox', { name: '编辑订单' })).toBeNull();
    expect(checkbox('查看订单')).not.toBeChecked();

    fireEvent.click(button('展开查看订单子权限'));
    expect(checkbox('编辑订单')).not.toBeChecked();
    expect(checkbox('查看订单明细')).toBeChecked();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('已授权筛选采用保存状态，保留待撤销及锁定项', () => {
    renderBody(
      orderGroups,
      new Map([
        ['Orders', true],
        ['Orders.Details', false],
      ]),
    );

    fireEvent.click(screen.getByRole('radio', { name: '已授权' }));
    expect(screen.queryByRole('checkbox', { name: '查看订单' })).toBeNull();
    expect(checkbox('查看订单明细')).not.toBeChecked();
    expect(screen.getByText('待撤销')).toBeVisible();
    expect(checkbox('导出订单')).toBeDisabled();
  });

  it('红冲需要查看凭证，只有点击补选才修改查看权限', () => {
    const { onChange, setChanges } = renderBody(finance(false));

    fireEvent.click(checkbox('红冲凭证'));
    expect(onChange).toHaveBeenCalledExactlyOnceWith([
      { name: 'Finance.Voucher.Reverse', isGranted: true },
    ]);
    setChanges(new Map([['Finance.Voucher.Reverse', true]]));
    expect(screen.getByRole('alert')).toHaveTextContent('Finance.Voucher.View');
    expect(checkbox('查看凭证')).not.toBeChecked();

    fireEvent.click(button('同时授予查看凭证'));
    expect(onChange).toHaveBeenLastCalledWith([
      { name: 'Finance.Voucher.View', isGranted: true },
    ]);
  });

  it.each([
    {
      description: '角色已有查看权限',
      granted: true,
      editable: true,
      pendingView: false,
    },
    {
      description: '查看权限已在草稿里',
      granted: false,
      editable: true,
      pendingView: true,
    },
    {
      description: '查看权限被锁定',
      granted: false,
      editable: false,
      pendingView: false,
    },
  ])('$description时隐藏补选操作', ({ granted, editable, pendingView }) => {
    const changes = new Map([['Finance.Voucher.Reverse', true]]);
    if (pendingView) changes.set('Finance.Voucher.View', true);
    const { onChange } = renderBody(finance(granted, editable), changes);

    expect(screen.getByRole('alert')).toHaveTextContent('Finance.Voucher.View');
    expect(
      screen.queryByRole('button', { name: '同时授予查看凭证' }),
    ).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });
});
