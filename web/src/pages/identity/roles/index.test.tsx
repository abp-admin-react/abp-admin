import { act, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createRole, type IdentityRoleDto, updateRole } from '@/abp/identity';

const harness = vi.hoisted(() => ({ form: vi.fn(), table: vi.fn() }));
let access: Record<string, boolean>;
let role: IdentityRoleDto;
const regularRole: IdentityRoleDto = {
  id: 'r-1',
  name: 'manager',
  isDefault: true,
  isPublic: true,
  concurrencyStamp: 'stamp',
};

vi.mock('@umijs/max', () => ({ useAccess: () => access }));
vi.mock('@/abp/identity', () => ({
  createRole: vi.fn(),
  updateRole: vi.fn(),
  deleteRole: vi.fn(),
  getRoles: vi.fn(),
}));
vi.mock('@/components/IdentityOrgHelp', () => ({
  identityOrgHelpHeader: () => ({}),
}));
vi.mock('@/components/PermissionModal', () => ({ default: () => null }));
vi.mock('@/components/ClaimModal', () => ({ default: () => null }));
vi.mock('./components/RoleUsersDrawer', () => ({ default: () => null }));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: any) => {
    harness.table(props);
    return (
      <>
        <div data-testid="actions">
          {props.columns
            .find((col: any) => col.valueType === 'option')
            .render(undefined, role)}
        </div>
        {props.toolBarRender()}
      </>
    );
  },
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => children,
  ModalForm: (props: any) => {
    harness.form(props);
    return (
      <>
        {props.trigger}
        {props.children}
      </>
    );
  },
  ProFormText: ({ label, disabled }: any) => (
    <input aria-label={label} disabled={disabled} />
  ),
  ProFormTextArea: ({ label }: any) => <textarea aria-label={label} />,
  ProFormSwitch: ({ label }: any) => (
    <input type="checkbox" aria-label={label} />
  ),
}));

describe('角色管理常规流程', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    role = { ...regularRole };
    access = {
      canCreateRoles: true,
      canUpdateRoles: true,
      canDeleteRoles: true,
      canManageRolePermissions: true,
      canManageClaimTypes: true,
    };
  });

  it('主操作集中在编辑、功能权限与用户，声明保留在更多', async () => {
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    expect(screen.getByTestId('actions').textContent).toBe(
      '编辑功能权限用户更多',
    );
    expect(screen.queryByText('数据范围')).toBeNull();
    expect(screen.queryByText('菜单权限')).toBeNull();
    fireEvent.click(screen.getByText('更多'));
    expect(await screen.findByText('声明（高级）')).toBeTruthy();
    expect(screen.getByText('删除角色')).toBeTruthy();
  });

  it('没有声明类型查询权限时不显示声明入口', async () => {
    access.canManageClaimTypes = false;
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    fireEvent.click(screen.getByText('更多'));
    expect(await screen.findByText('删除角色')).toBeTruthy();
    expect(screen.queryByText('声明（高级）')).toBeNull();
  });

  it('普通表单不暴露公开角色身份，改名仍保留原有默认和公开设置', async () => {
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    const form = harness.form.mock.calls
      .map(([props]) => props)
      .find((props) => props.title === '编辑角色');
    await act(() => form.onFinish({ name: 'renamed' }));
    expect(screen.queryByLabelText('公开角色身份')).toBeNull();
    expect(screen.getAllByLabelText('注册时默认分配')).toHaveLength(2);
    expect(updateRole).toHaveBeenCalledWith('r-1', {
      name: 'renamed',
      isDefault: true,
      isPublic: true,
      concurrencyStamp: 'stamp',
      extraProperties: { Remark: '' },
    });
  });

  it('新角色缺省不开公开身份，取消注册默认不会改写隐藏的公开属性', async () => {
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    const forms = harness.form.mock.calls.map(([props]) => props);
    await act(() =>
      forms
        .find((props) => props.title === '新建角色')
        .onFinish({ name: 'staff', remark: '  查看报表  ' }),
    );
    expect(createRole).toHaveBeenCalledWith({
      name: 'staff',
      isDefault: false,
      isPublic: false,
      extraProperties: { Remark: '查看报表' },
    });
    await act(() =>
      forms
        .find((props) => props.title === '编辑角色')
        .onFinish({ name: 'manager', isDefault: false, isPublic: false }),
    );
    expect(updateRole).toHaveBeenLastCalledWith(
      'r-1',
      expect.objectContaining({ isDefault: false, isPublic: true }),
    );
  });

  it('系统角色只可编辑备注，清空备注时保留原有角色设置', async () => {
    role = {
      ...role,
      name: 'admin',
      isStatic: true,
      extraProperties: { Remark: '系统管理员' },
    };
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    expect(screen.getByTestId('actions').textContent).toContain('编辑备注');
    expect(screen.getAllByLabelText('名称')[0]).toHaveProperty(
      'disabled',
      true,
    );
    expect(screen.getAllByLabelText('注册时默认分配')).toHaveLength(1);
    const form = harness.form.mock.calls
      .map(([props]) => props)
      .find((props) => props.title === '编辑备注');
    expect(form.initialValues.remark).toBe('系统管理员');
    await act(() =>
      form.onFinish({ name: 'ignored', isDefault: false, remark: '   ' }),
    );
    expect(updateRole).toHaveBeenCalledWith('r-1', {
      name: 'admin',
      isDefault: true,
      isPublic: true,
      concurrencyStamp: 'stamp',
      extraProperties: { Remark: '' },
    });
  });

  it('功能权限与角色更新独立控制，查看者不显示写入口', async () => {
    access = { canManageRolePermissions: true };
    const { default: Page } = await import('./index');
    render(
      <App>
        <Page />
      </App>,
    );
    expect(screen.getByTestId('actions').textContent).toBe('功能权限用户');
    expect(screen.queryByText('新建角色')).toBeNull();
  });
});
