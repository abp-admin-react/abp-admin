import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { IdentityRoleDto } from '@/abp/identity';
import type { PermissionGroup, PermissionListResult } from '@/abp/permissions';
import { RequestSessionContextChangedError } from '@/requestErrorConfig';
import RolePermissionEditor from './RolePermissionEditor';

const api = vi.hoisted(() => ({
  getRolePermissions: vi.fn(),
  updateRolePermissions: vi.fn(),
  block: vi.fn(),
}));
vi.mock('@umijs/max', () => ({ history: { block: api.block } }));
vi.mock('@/abp/permissions', () => ({
  getRolePermissions: api.getRolePermissions,
  updateRolePermissions: api.updateRolePermissions,
}));

const role: IdentityRoleDto = { id: 'role-1', name: '订单专员' };
const requestContext = { userId: 'operator-1', tenantId: undefined };

const groups: PermissionGroup[] = [
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
        name: 'Orders.Delete',
        displayName: '删除订单',
        parentName: 'Orders',
        isGranted: false,
      },
      {
        name: 'Orders.Export',
        displayName: '导出订单',
        parentName: 'Orders',
        isGranted: true,
        isEditable: false,
      },
      {
        name: 'Orders.Refund',
        displayName: '退款',
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

const ordersEdit = { name: 'Orders.Edit', isGranted: true };
const settingsView = { name: 'Settings.View', isGranted: false };
const renamed = {
  response: {
    status: 409,
    data: { error: { code: 'AbpAdmin:RolePermissionTargetChanged' } },
  },
};

function snapshot(
  changes: { name: string; isGranted: boolean }[] = [],
): PermissionListResult {
  return {
    entityDisplayName: role.name,
    groups: groups.map((group) => ({
      ...group,
      permissions: group.permissions.map((permission) => ({
        ...permission,
        isGranted:
          changes.find((change) => change.name === permission.name)
            ?.isGranted ?? permission.isGranted,
      })),
    })),
  };
}

function renderEditor() {
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const onStaleRole = vi.fn();
  render(
    <ConfigProvider theme={{ token: { motion: false } }}>
      <App>
        <RolePermissionEditor
          role={role}
          userId={requestContext.userId}
          tenantName="华东"
          onClose={onClose}
          onSaved={onSaved}
          onStaleRole={onStaleRole}
        />
      </App>
    </ConfigProvider>,
  );
  return { onClose, onSaved, onStaleRole };
}

const permission = (name: string) => screen.getByRole('checkbox', { name });
const saveButton = () => screen.getByRole('button', { name: '保存变更' });

describe('角色功能权限编辑', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.getRolePermissions.mockResolvedValue(snapshot());
    api.updateRolePermissions.mockResolvedValue(undefined);
  });

  it('精确勾选保留父项、其他子项和锁定项，搜索隐藏的草稿只按差量提交', async () => {
    api.updateRolePermissions.mockImplementationOnce(async () => {
      api.getRolePermissions.mockResolvedValue(
        snapshot([ordersEdit, settingsView]),
      );
    });
    const { onClose, onSaved } = renderEditor();
    const edit = await screen.findByRole('checkbox', { name: '编辑订单' });
    expect(saveButton()).toBeDisabled();

    fireEvent.click(edit);
    expect(edit).toBeChecked();
    expect(permission('查看订单')).not.toBeChecked();
    expect(permission('删除订单')).not.toBeChecked();
    expect(permission('导出订单')).toBeChecked();
    expect(permission('导出订单')).toBeDisabled();
    expect(permission('退款')).not.toBeChecked();
    expect(permission('退款')).toBeDisabled();

    fireEvent.change(screen.getByLabelText('搜索权限'), {
      target: { value: '查看设置' },
    });
    const settings = await screen.findByRole('checkbox', { name: '查看设置' });
    expect(screen.queryByRole('checkbox', { name: '编辑订单' })).toBeNull();
    expect(settings).toBeChecked();
    fireEvent.click(settings);
    expect(settings).not.toBeChecked();
    expect(
      screen.getByText('待保存 2 项，新增 1 项，撤销 1 项'),
    ).toBeInTheDocument();

    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(api.updateRolePermissions).toHaveBeenCalledTimes(1);
    expect(api.updateRolePermissions).toHaveBeenCalledWith(
      role,
      [ordersEdit, settingsView],
      requestContext,
    );
    expect(api.getRolePermissions.mock.lastCall).toEqual([
      role,
      requestContext,
    ]);
    expect(
      api.getRolePermissions.mock.invocationCallOrder.at(-1),
    ).toBeGreaterThan(api.updateRolePermissions.mock.invocationCallOrder[0]);
  });

  it('角色改名后阻止使用首次打开时的名称写入', async () => {
    const { onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    api.getRolePermissions.mockRejectedValueOnce(renamed);

    fireEvent.click(saveButton());
    expect(await screen.findByText(/角色名称已变更/)).toBeInTheDocument();
    expect(api.updateRolePermissions).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('另一管理员在权限读取后改名时，PUT 拒绝后禁止继续保存并刷新列表', async () => {
    const { onSaved, onStaleRole } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    api.updateRolePermissions.mockRejectedValueOnce(renamed);
    fireEvent.click(saveButton());
    expect(await screen.findByText(/角色名称已变更/)).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
    expect(api.updateRolePermissions).toHaveBeenCalledExactlyOnceWith(
      role,
      [ordersEdit],
      requestContext,
    );
    expect(onStaleRole).toHaveBeenCalledOnce();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('提交成功后才改名时保留回执，避免误导操作者再次写入', async () => {
    const { onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    api.updateRolePermissions.mockImplementationOnce(async () => {
      api.getRolePermissions.mockRejectedValueOnce(renamed);
    });
    fireEvent.click(saveButton());
    expect(
      await screen.findByText(/已提交，无法核对。角色名称已变更/),
    ).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
    expect(api.updateRolePermissions).toHaveBeenCalledOnce();
    expect(onSaved).toHaveBeenCalledOnce();
  });

  it.each([
    [{ response: { status: 404 } }, /角色已删除或不可见/],
    [
      {
        response: {
          status: 409,
          data: { error: { code: 'AbpAdmin:RolePermissionNameAmbiguous' } },
        },
      },
      /当前租户存在同名角色/,
    ],
  ] as const)(
    '读取的角色不可唯一识别时不提供写入入口',
    async (error, notice) => {
      api.getRolePermissions.mockRejectedValueOnce(error);
      renderEditor();
      expect(await screen.findByText(notice)).toBeInTheDocument();
      expect(saveButton()).toBeDisabled();
      expect(api.updateRolePermissions).not.toHaveBeenCalled();
    },
  );

  it('写请求结果不明确时重新核对只读，确认已应用后才回报成功', async () => {
    api.updateRolePermissions.mockRejectedValueOnce(
      new Error('network failure'),
    );
    const { onClose, onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));

    fireEvent.click(saveButton());
    const verifyButton = await screen.findByRole('button', {
      name: '重新核对',
    });
    expect(screen.getByText(/提交结果待核实/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '保存变更' })).toBeNull();
    expect(onSaved).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();

    api.getRolePermissions.mockResolvedValue(snapshot([ordersEdit]));
    fireEvent.click(verifyButton);
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(api.updateRolePermissions).toHaveBeenCalledTimes(1);
  });

  it('服务端只应用部分变更时保留尚未生效的可编辑草稿', async () => {
    api.updateRolePermissions.mockImplementationOnce(async () => {
      api.getRolePermissions.mockResolvedValue(snapshot([ordersEdit]));
    });
    const { onClose, onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    fireEvent.change(screen.getByLabelText('搜索权限'), {
      target: { value: '查看设置' },
    });
    fireEvent.click(await screen.findByRole('checkbox', { name: '查看设置' }));

    fireEvent.click(saveButton());
    expect(await screen.findByText(/核对发现 1 项未生效/)).toBeInTheDocument();
    expect(
      screen.getByText('待保存 1 项，新增 0 项，撤销 1 项'),
    ).toBeInTheDocument();
    expect(permission('查看设置')).not.toBeChecked();
    expect(saveButton()).toBeEnabled();
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
    expect(api.updateRolePermissions).toHaveBeenCalledTimes(1);
  });

  it('写入有回执但核对返回 403 时保留已提交状态和核对入口', async () => {
    api.updateRolePermissions.mockImplementationOnce(async () => {
      api.getRolePermissions.mockRejectedValueOnce({
        response: { status: 403 },
      });
    });
    const { onClose, onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));

    fireEvent.click(saveButton());
    expect(
      await screen.findByText(/已提交，无法核对角色权限/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重新核对' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: '保存变更' })).toBeNull();
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
    expect(api.updateRolePermissions).toHaveBeenCalledTimes(1);
  });

  it('关闭含未保存变更的编辑器前需要确认放弃', async () => {
    const { onClose, onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));

    fireEvent.click(screen.getByRole('button', { name: /关\s*闭/ }));
    const discardButton = await screen.findByRole('button', {
      name: '放弃变更',
    });
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(discardButton);
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(api.updateRolePermissions).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('其他管理员已完成相同变更时刷新当前会话，省去重复写入', async () => {
    const { onSaved, onClose } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    api.getRolePermissions.mockResolvedValue(snapshot([ordersEdit]));
    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(api.updateRolePermissions).not.toHaveBeenCalled();
  });

  it('发送前发现同租户已换账号，结束编辑并明确提示', async () => {
    const { onSaved } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    api.getRolePermissions.mockRejectedValueOnce(
      new RequestSessionContextChangedError('user'),
    );
    fireEvent.click(saveButton());
    await screen.findByText(/登录账号已更换/);
    expect(saveButton()).toBeDisabled();
    expect(api.updateRolePermissions).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('同页导航取消时保留草稿，确认离开时关闭编辑器', async () => {
    const { onClose } = renderEditor();
    fireEvent.click(await screen.findByRole('checkbox', { name: '编辑订单' }));
    const navigate = api.block.mock.lastCall?.[0];
    const transition = {
      location: {
        pathname: '/administration/identity/roles',
        search: '?page=2',
      },
      retry: vi.fn(),
    };
    vi.stubGlobal(
      'confirm',
      vi.fn().mockReturnValueOnce(false).mockReturnValueOnce(true),
    );
    try {
      act(() => navigate(transition));
      expect(onClose).not.toHaveBeenCalled();
      expect(transition.retry).not.toHaveBeenCalled();
      act(() => navigate(transition));
      expect(onClose).toHaveBeenCalledTimes(1);
      expect(transition.retry).toHaveBeenCalledTimes(1);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
