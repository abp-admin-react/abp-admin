import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OrganizationUnitsPage from './index';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  getAllRoles: vi.fn(),
  getUsers: vi.fn(),
  getOrganizationUnits: vi.fn(),
  getOrganizationUnitMembers: vi.fn(),
  getOrganizationUnitRoles: vi.fn(),
  createOrganizationUnit: vi.fn(),
  updateOrganizationUnit: vi.fn(),
  deleteOrganizationUnit: vi.fn(),
  moveOrganizationUnit: vi.fn(),
  addOrganizationUnitMembers: vi.fn(),
  removeOrganizationUnitMember: vi.fn(),
  addOrganizationUnitRoles: vi.fn(),
  removeOrganizationUnitRole: vi.fn(),
}));

vi.mock('ag-grid-react', () => import('../../../../tests/gridTestDouble'));
vi.mock('@/abp/gridQuery', () => ({
  postGridQuery: (url: string, grid: unknown) =>
    url.endsWith('/query-members')
      ? mocks.getOrganizationUnitMembers(url, grid)
      : mocks.getOrganizationUnitRoles(url, grid),
}));
vi.mock('@umijs/max', () => ({ useAccess: mocks.useAccess }));
vi.mock('@/abp/identityAdmin', async (actual) => ({
  ...(await actual<typeof import('@/abp/identityAdmin')>()),
  ...mocks,
}));
vi.mock('@/abp/identity', () => ({
  getAllRoles: mocks.getAllRoles,
  getUsers: mocks.getUsers,
}));

const permissions = {
  canCreateOrganizationUnits: true,
  canUpdateOrganizationUnits: true,
  canDeleteOrganizationUnits: true,
  canManageOrganizationUnitMembers: true,
  canManageOrganizationUnitRoles: true,
};

const units = [
  { id: 'root-1', parentId: null, displayName: '研发部', code: '00001' },
  { id: 'root-2', parentId: null, displayName: '财务部', code: '00002' },
];

const knownId = '6d07b1ca-a17f-4936-89ac-b4edaffcea4c';
const secondId = 'fc0e1663-e36b-479a-8d13-e6725b283a2c';

function renderPage() {
  return render(
    <AntdApp>
      <OrganizationUnitsPage />
    </AntdApp>,
  );
}

async function selectDepartment() {
  fireEvent.click(await screen.findByText('研发部'));
  await waitFor(() =>
    expect(mocks.getOrganizationUnitMembers).toHaveBeenCalledWith(
      '/api/app/organization-unit/root-1/query-members',
      expect.objectContaining({ skipCount: 0, maxResultCount: 10 }),
    ),
  );
}

describe('部门维护', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.useAccess.mockReturnValue(permissions);
    mocks.getOrganizationUnits.mockResolvedValue(units);
    mocks.getOrganizationUnitMembers.mockResolvedValue({
      items: [{ id: 'user-1', userName: 'alice', email: 'alice@example.test' }],
      totalCount: 1,
    });
    mocks.getOrganizationUnitRoles.mockResolvedValue({
      items: [{ id: 'role-1', name: '业务查看' }],
    });
    mocks.createOrganizationUnit.mockResolvedValue({ id: 'new-department' });
    mocks.getUsers.mockResolvedValue({
      items: [{ id: knownId, userName: '照常选择用户' }],
    });
    mocks.getAllRoles.mockResolvedValue({
      items: [{ id: knownId, name: '照常选择角色' }],
    });
  });

  it.each([
    ['新建根部门', { displayName: '新部门' }],
    ['添加子部门', { displayName: '新部门', parentId: 'root-1' }],
  ])('已选中部门时，%s仍使用明确的父级', async (buttonName, payload) => {
    renderPage();
    await selectDepartment();
    fireEvent.click(screen.getByRole('button', { name: buttonName }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('部门名称'), {
      target: { value: '新部门' },
    });
    const form = dialog.querySelector('form');
    expect(form).toBeTruthy();
    if (form) fireEvent.submit(form);
    await waitFor(() =>
      expect(mocks.createOrganizationUnit).toHaveBeenCalledWith(payload),
    );
  });

  it('以成员为主，部门统一角色默认折叠并按需读取', async () => {
    renderPage();
    await selectDepartment();
    expect(await screen.findByText('alice')).toBeTruthy();
    expect(screen.queryAllByRole('tab')).toHaveLength(0);
    expect(mocks.getOrganizationUnitRoles).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText('统一角色（可选）'));
    expect(await screen.findByText('业务查看')).toBeTruthy();
    expect(mocks.getOrganizationUnitRoles).toHaveBeenCalledWith(
      '/api/app/organization-unit/root-1/query-roles',
      expect.anything(),
    );
    expect(screen.getByText('本部门所有成员自动获得这些角色。')).toBeTruthy();
  });

  it('只读权限仍可查看成员和统一角色，不显示写入操作', async () => {
    mocks.useAccess.mockReturnValue(
      Object.fromEntries(Object.keys(permissions).map((key) => [key, false])),
    );
    renderPage();
    await selectDepartment();
    expect(await screen.findByText('alice')).toBeTruthy();
    fireEvent.click(screen.getByText('统一角色（可选）'));
    expect(await screen.findByText('业务查看')).toBeTruthy();

    for (const name of [
      '新建根部门',
      '添加子部门',
      '重命名',
      '移动部门',
      '删除部门',
      '添加成员',
      '添加角色',
    ]) {
      expect(screen.queryByRole('button', { name })).toBeNull();
    }
    expect(screen.queryByRole('button', { name: /更\s*多/ })).toBeNull();
    expect(screen.queryByText('移除')).toBeNull();
  });

  it('部门管理收进更多，重命名仍回填并更新当前部门', async () => {
    renderPage();
    await selectDepartment();
    for (const name of ['重命名', '移动部门', '删除部门']) {
      expect(screen.queryByRole('button', { name })).toBeNull();
    }
    expect(screen.getByRole('button', { name: '添加成员' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /更\s*多/ }));
    fireEvent.click(await screen.findByRole('menuitem', { name: '重命名' }));
    const dialog = await screen.findByRole('dialog');
    const name = within(dialog).getByLabelText('部门名称');
    expect(name).toHaveValue('研发部');
    fireEvent.change(name, { target: { value: '技术部' } });
    const form = dialog.querySelector('form');
    if (form) fireEvent.submit(form);
    await waitFor(() =>
      expect(mocks.updateOrganizationUnit).toHaveBeenCalledWith('root-1', {
        displayName: '技术部',
      }),
    );
  });

  it('仅有删除权限时更多只提供删除，确认前不执行', async () => {
    mocks.useAccess.mockReturnValue({ canDeleteOrganizationUnits: true });
    renderPage();
    await selectDepartment();
    fireEvent.click(screen.getByRole('button', { name: /更\s*多/ }));
    expect(screen.queryByRole('menuitem', { name: '重命名' })).toBeNull();
    expect(screen.queryByRole('menuitem', { name: '移动部门' })).toBeNull();
    fireEvent.click(await screen.findByRole('menuitem', { name: '删除部门' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveAccessibleName('确认删除部门「研发部」？');
    expect(mocks.deleteOrganizationUnit).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole('button', { name: '删 除' }));
    await waitFor(() =>
      expect(mocks.deleteOrganizationUnit).toHaveBeenCalledWith('root-1'),
    );
  });

  it.each([
    [
      '添加成员',
      '用户 ID',
      'canManageOrganizationUnitMembers',
      'addOrganizationUnitMembers',
    ],
    [
      '添加角色',
      '角色 ID',
      'canManageOrganizationUnitRoles',
      'addOrganizationUnitRoles',
    ],
  ] as const)(
    '仅有部门权限时用明确 ID %s，不调用全局目录',
    async (button, label, permission, endpoint) => {
      mocks.useAccess.mockReturnValue({
        canManageOrganizationUnits: true,
        [permission]: true,
      });
      renderPage();
      await selectDepartment();
      if (button === '添加角色')
        fireEvent.click(screen.getByText('统一角色（可选）'));
      fireEvent.click(await screen.findByRole('button', { name: button }));
      const dialog = await screen.findByRole('dialog');
      const field = within(dialog).getByLabelText(label);
      const form = dialog.querySelector('form');
      if (!form) throw new Error('部门授权弹窗缺少表单');

      fireEvent.change(field, { target: { value: 'not-a-guid' } });
      expect(
        within(dialog).queryByText(`请输入带连字符的完整${label}`),
      ).toBeNull();
      fireEvent.submit(form);
      expect(
        await within(dialog).findByText(`请输入带连字符的完整${label}`),
      ).toBeTruthy();
      expect(mocks[endpoint]).not.toHaveBeenCalled();

      fireEvent.change(field, {
        target: {
          value: `${knownId.toUpperCase()}，\n${secondId}\n${knownId}`,
        },
      });
      fireEvent.submit(form);
      await waitFor(() =>
        expect(mocks[endpoint]).toHaveBeenCalledExactlyOnceWith('root-1', [
          knownId,
          secondId,
        ]),
      );
      expect(mocks.getUsers).not.toHaveBeenCalled();
      expect(mocks.getAllRoles).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['添加成员', '用户', 'canManageUsers', 'getUsers'],
    ['添加角色', '角色', 'canManageRoles', 'getAllRoles'],
  ] as const)(
    '同时持有目录读取权限时仍可用原选择器%s',
    async (button, label, permission, endpoint) => {
      mocks.useAccess.mockReturnValue({ ...permissions, [permission]: true });
      renderPage();
      await selectDepartment();
      if (button === '添加角色')
        fireEvent.click(screen.getByText('统一角色（可选）'));
      fireEvent.click(await screen.findByRole('button', { name: button }));
      const dialog = await screen.findByRole('dialog');
      expect(
        within(dialog).getByRole('combobox', { name: label }),
      ).toBeTruthy();
      await waitFor(() => expect(mocks[endpoint]).toHaveBeenCalled());
    },
  );

  it('提交不存在的用户 ID 时保留弹窗并提示核对', async () => {
    mocks.useAccess.mockReturnValue({ canManageOrganizationUnitMembers: true });
    mocks.addOrganizationUnitMembers.mockRejectedValueOnce({
      response: { status: 404 },
    });
    renderPage();
    await selectDepartment();
    fireEvent.click(screen.getByRole('button', { name: '添加成员' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('用户 ID'), {
      target: { value: knownId },
    });
    const form = dialog.querySelector('form');
    if (!form) throw new Error('添加成员弹窗缺少表单');
    fireEvent.submit(form);
    expect(
      await screen.findByText('找不到指定用户，请核对用户 ID。'),
    ).toBeTruthy();
    expect(within(dialog).getByLabelText('用户 ID')).toHaveValue(knownId);
  });
});
