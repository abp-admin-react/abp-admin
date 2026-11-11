import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as identity from '@/abp/identity';

// T2.6 验收：「补充导入导出按钮的权限控制断言」。
// 权限可变的 mock：默认全量权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = {
  canImportUsers: true,
  canExportUsers: true,
};
const fullAccess = {
  canImportUsers: true,
  canExportUsers: true,
  canCreateUsers: true,
  canUpdateUsers: true,
  canDeleteUsers: true,
  canManageUserRoles: true,
  canManageUserPermissions: true,
};
let mockRow: identity.IdentityUserDto | undefined;
let mockColumnsState: any;

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useModel: () => ({ initialState: { currentUser: { userid: 'u-admin' } } }),
}));

// Mock 服务层：页面不直接依赖生成客户端，mock 这一层即可
vi.mock('@/abp/identity', async () => {
  const actual =
    await vi.importActual<typeof import('@/abp/identity')>('@/abp/identity');
  return {
    ...actual,
    getUsers: vi.fn(),
    getAssignableRoles: vi.fn(),
    exportUsers: vi.fn(),
    enqueueExportUsers: vi.fn(),
    importUsers: vi.fn(),
    downloadImportTemplate: vi.fn(),
    downloadImportFailureReport: vi.fn(),
    getTwoFactorStatuses: vi.fn(),
    setUserTwoFactorEnabled: vi.fn(),
  };
});

vi.mock('@/abp/account', () => ({ impersonateUser: vi.fn() }));
vi.mock('@/abp/oidc', () => ({ applyImpersonatedTokens: vi.fn() }));
vi.mock('@/components/ClaimModal', () => ({ default: () => null }));
vi.mock('@/components/PermissionModal', () => ({ default: () => null }));

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProTable: ({ columns, toolBarRender, request, columnsState }: any) => {
    mockColumnsState = columnsState;
    request?.({ current: 1, pageSize: 10 }, {}, {});
    return (
      <div data-testid="pro-table">
        <div data-testid="table-columns">
          {columns?.map((col: any, i: number) => (
            <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
              {col.title}
            </div>
          ))}
        </div>
        {toolBarRender && <div data-testid="toolbar">{toolBarRender()}</div>}
        {mockRow && (
          <>
            <div data-testid="row-actions">
              {columns
                .find((col: any) => col.valueType === 'option')
                .render(undefined, mockRow)}
            </div>
            <div data-testid="two-factor-status">
              {columns
                .find((col: any) => col.dataIndex === 'twoFactorEnabled')
                .render(undefined, mockRow)}
            </div>
          </>
        )}
      </div>
    );
  },
  ModalForm: ({ children, title, trigger }: any) => (
    <div data-testid="modal-form" data-title={title}>
      {trigger}
      {children}
    </div>
  ),
  ProFormText: Object.assign(({ label }: any) => <input aria-label={label} />, {
    Password: ({ label }: any) => <input type="password" aria-label={label} />,
  }),
  ProFormSelect: ({ label }: any) => <select aria-label={label} />,
  ProFormSwitch: ({ label }: any) => (
    <input type="checkbox" aria-label={label} />
  ),
}));

describe('UsersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccess = { ...fullAccess };
    mockRow = undefined;
    vi.mocked(identity.getUsers).mockResolvedValue({
      items: [],
      totalCount: 0,
    } as never);
    vi.mocked(identity.getAssignableRoles).mockResolvedValue({
      items: [],
    } as never);
    vi.mocked(identity.getTwoFactorStatuses).mockResolvedValue([] as never);
  });

  it('挂载即请求用户列表与角色', async () => {
    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );

    await waitFor(() => {
      expect(identity.getUsers).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 10 }),
      );
    });
    await waitFor(() => {
      expect(identity.getAssignableRoles).toHaveBeenCalled();
    });
  });

  it('有导入导出权限时显示 导出/导入/下载模板 按钮', async () => {
    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );

    const toolbar = await screen.findByTestId('toolbar');
    expect(toolbar.textContent).toContain('导出');
    expect(toolbar.textContent).toContain('导入');
    expect(toolbar.textContent).toContain('下载模板');
  });

  it('无导入导出权限时按钮隐藏', async () => {
    mockAccess = {
      ...fullAccess,
      canImportUsers: false,
      canExportUsers: false,
    };
    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );

    const toolbar = await screen.findByTestId('toolbar');
    expect(toolbar.textContent).not.toContain('导出');
    expect(toolbar.textContent).not.toContain('导入');
    expect(toolbar.textContent).not.toContain('下载模板');
    // 与权限无关的入口仍在
    expect(toolbar.textContent).toContain('新建用户');
  });

  it('列表含「双因素」列，且按当前页用户批量补齐 2FA 状态', async () => {
    // Volo 用户列表契约不含 twoFactorEnabled：页面必须调批量端点补齐，
    // 缺失时 2FA 列恒为「否」、开关恒发 true（六透镜审查 function-High 的回归锚）
    vi.mocked(identity.getUsers).mockResolvedValue({
      items: [
        { id: 'u-1', userName: 'alice', email: 'a@t.com', isActive: true },
        { id: 'u-2', userName: 'bob', email: 'b@t.com', isActive: true },
      ],
      totalCount: 2,
    } as never);
    vi.mocked(identity.getTwoFactorStatuses).mockResolvedValue([
      { userId: 'u-1', twoFactorEnabled: true },
      { userId: 'u-2', twoFactorEnabled: false },
    ] as never);

    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );

    const columns = await screen.findByTestId('table-columns');
    expect(columns.textContent).toContain('双因素');
    expect(mockColumnsState.defaultValue).toMatchObject({
      emailConfirmed: { show: false },
      twoFactorEnabled: { show: false },
      accessFailedCount: { show: false },
    });

    await waitFor(() => {
      expect(identity.getTwoFactorStatuses).toHaveBeenCalledWith([
        'u-1',
        'u-2',
      ]);
    });
  });

  it('常用操作只展示编辑和重置密码，额外权限及账号安全操作收进更多', async () => {
    mockRow = {
      id: 'u-1',
      userName: 'alice',
      email: 'a@t.com',
      isActive: true,
      lockoutEnabled: true,
    };
    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );
    const actions = screen.getByTestId('row-actions');
    expect(actions.textContent).toContain('编辑');
    expect(actions.textContent).toContain('重置密码');
    expect(actions.textContent).not.toContain('额外权限');
    expect(actions.textContent).not.toContain('冻结');
    fireEvent.click(screen.getByText('更多'));
    expect(await screen.findByText('额外权限')).toBeTruthy();
    expect(screen.getByText('通常通过角色授权')).toBeTruthy();
    expect(screen.getByText('删除用户')).toBeTruthy();
    fireEvent.click(screen.getByText('删除用户'));
    expect((await screen.findByRole('dialog')).textContent).toContain(
      '确认删除该用户？',
    );
  });

  it('只有查看权限时不显示写操作，也不读取角色候选和2FA管理端点', async () => {
    mockAccess = {};
    mockRow = {
      id: 'u-1',
      userName: 'alice',
      email: 'a@t.com',
      isActive: true,
      lockoutEnabled: true,
    };
    vi.mocked(identity.getUsers).mockResolvedValue({
      items: [mockRow],
      totalCount: 1,
    });
    const { default: Page } = await import('./index');
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );
    expect(screen.getByTestId('row-actions').textContent).toBe('');
    expect(screen.getByTestId('two-factor-status').textContent).toBe('—');
    expect(screen.getByTestId('toolbar').textContent).not.toContain('新建用户');
    await waitFor(() => expect(identity.getUsers).toHaveBeenCalled());
    expect(identity.getAssignableRoles).not.toHaveBeenCalled();
    expect(identity.getTwoFactorStatuses).not.toHaveBeenCalled();
  });
});
