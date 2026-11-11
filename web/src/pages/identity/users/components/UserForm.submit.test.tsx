import { act, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createUser,
  getUserRoles,
  type IdentityUserDto,
  updateUser,
} from '@/abp/identity';

const harness = vi.hoisted(() => ({ form: vi.fn() }));
vi.mock('@/abp/identity', () => ({
  createUser: vi.fn(),
  getUserRoles: vi.fn(),
  updateUser: vi.fn(),
  resetUserPassword: vi.fn(),
}));
vi.mock('@ant-design/pro-components', () => ({
  ModalForm: (props: any) => {
    harness.form(props);
    return props.children;
  },
  ProFormText: Object.assign(({ label }: any) => <input aria-label={label} />, {
    Password: () => null,
  }),
  ProFormSwitch: ({ label }: any) => (
    <input aria-label={label} type="checkbox" />
  ),
  ProFormSelect: ({ label, extra }: any) => (
    <>
      <select aria-label={label} />
      <p>{extra}</p>
    </>
  ),
}));

const record: IdentityUserDto = {
  id: 'u-1',
  userName: 'alice',
  email: 'alice@t.com',
  isActive: true,
  lockoutEnabled: true,
  concurrencyStamp: 'stamp',
};

describe('用户表单角色与更多选项', () => {
  beforeEach(() => vi.clearAllMocks());

  it('无角色管理权限不读取或回写角色，未展开更多选项保持原安全开关', async () => {
    const { UserForm } = await import('./UserForms');
    render(
      <App>
        <UserForm
          title="编辑用户"
          trigger={<a>编辑</a>}
          roles={[]}
          record={record}
          onSuccess={vi.fn()}
        />
      </App>,
    );
    const form = harness.form.mock.calls[0][0];
    await form.request();
    await act(() =>
      form.onFinish({
        userName: 'alice',
        email: 'alice@t.com',
        roleNames: ['inherited-role'],
      }),
    );
    expect(getUserRoles).not.toHaveBeenCalled();
    expect(screen.getByText('更多选项')).toBeTruthy();
    expect(screen.queryByText('高级安全选项')).toBeNull();
    expect(screen.queryByLabelText('角色')).toBeNull();
    expect(updateUser).toHaveBeenCalledWith(
      'u-1',
      expect.objectContaining({ isActive: true, lockoutEnabled: true }),
    );
    expect(vi.mocked(updateUser).mock.calls[0][1]).not.toHaveProperty(
      'roleNames',
    );
  });

  it('资料编辑不回写有效角色，修改角色选择时才提交', async () => {
    vi.mocked(getUserRoles).mockResolvedValue({
      items: [{ id: 'ou-role', name: 'ou-role' }],
    });
    const { UserForm } = await import('./UserForms');
    render(
      <App>
        <UserForm
          title="编辑用户"
          trigger={<a>编辑</a>}
          roles={[{ id: 'ou-role', name: 'ou-role' }]}
          record={record}
          canManageRoles
          onSuccess={vi.fn()}
        />
      </App>,
    );
    const form = harness.form.mock.calls[0][0];
    expect(await form.request()).toMatchObject({ roleNames: ['ou-role'] });
    expect(getUserRoles).toHaveBeenCalledWith('u-1');
    expect(screen.getByText(/部门授予的角色自动生效/)).toBeTruthy();
    await act(() =>
      form.onFinish({
        userName: 'alice',
        email: 'alice@t.com',
        roleNames: ['ou-role'],
        isActive: false,
        lockoutEnabled: false,
      }),
    );
    expect(updateUser).toHaveBeenCalledWith(
      'u-1',
      expect.objectContaining({
        isActive: false,
        lockoutEnabled: false,
      }),
    );
    expect(vi.mocked(updateUser).mock.calls[0][1]).not.toHaveProperty(
      'roleNames',
    );
    await act(() =>
      form.onFinish({
        userName: 'alice',
        email: 'alice@t.com',
        roleNames: [],
      }),
    );
    expect(updateUser).toHaveBeenLastCalledWith(
      'u-1',
      expect.objectContaining({ roleNames: [] }),
    );
  });

  it('新建用户无角色管理权限时不提交隐藏的角色字段', async () => {
    const { UserForm } = await import('./UserForms');
    render(
      <App>
        <UserForm
          title="新建用户"
          trigger={<a>新建</a>}
          roles={[]}
          onSuccess={vi.fn()}
        />
      </App>,
    );
    const form = harness.form.mock.calls[0][0];
    expect(await form.request()).toMatchObject({
      isActive: true,
      lockoutEnabled: true,
    });
    await act(() =>
      form.onFinish({
        userName: 'new',
        email: 'new@t.com',
        password: 'strong-pass',
        isActive: true,
        lockoutEnabled: true,
        roleNames: ['hidden'],
      }),
    );
    expect(createUser).toHaveBeenCalled();
    expect(vi.mocked(createUser).mock.calls[0][0]).not.toHaveProperty(
      'roleNames',
    );
  });
});
