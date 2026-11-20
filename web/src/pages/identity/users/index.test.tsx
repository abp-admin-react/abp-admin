import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import type {
  ColDef,
  ColumnState,
  FilterModel,
  GridApi,
  GridReadyEvent,
  IGetRowsParams,
} from 'ag-grid-community';
import type { AgGridReactProps } from 'ag-grid-react';
import { App as AntdApp } from 'antd';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as identity from '@/abp/identity';
import Page from './index';

const fullAccess = {
  canExportUsers: true,
  canCreateUsers: true,
  canUpdateUsers: true,
  canDeleteUsers: true,
  canManageUserRoles: true,
  canManageUserPermissions: true,
};
let access: Record<string, boolean>;
let grid: AgGridReactProps<identity.IdentityUserGridRowDto>;
let row: identity.IdentityUserGridRowDto;
const viewer = { initialState: { currentUser: { userid: 'u-admin' } } };

vi.mock('@umijs/max', () => ({
  useAccess: () => access,
  useModel: () => viewer,
}));
vi.mock('@/abp/identity', async (original) => ({
  ...(await original<typeof import('@/abp/identity')>()),
  queryIdentityUsers: vi.fn(),
  exportIdentityUserQuery: vi.fn(),
  getAssignableRoles: vi.fn(),
  getTwoFactorStatuses: vi.fn(),
  deleteUser: vi.fn(),
}));
vi.mock('@/abp/account', () => ({ impersonateUser: vi.fn() }));
vi.mock('@/abp/oidc', () => ({ applyImpersonatedTokens: vi.fn() }));
vi.mock('@/components/ClaimModal', () => ({ default: () => null }));
vi.mock('@/components/PermissionModal', () => ({ default: () => null }));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: ReactNode }) => (
    <main>{children}</main>
  ),
}));
vi.mock('./components/UserForms', () => ({
  UserForm: ({ trigger }: { trigger: ReactNode }) => trigger,
  ResetPasswordForm: () => null,
}));

vi.mock('ag-grid-react', () => ({
  AgGridReact: (props: AgGridReactProps<identity.IdentityUserGridRowDto>) => {
    grid = props;
    const Actions = columns().find(
      (column) => column.headerName === '操作',
    )?.cellRenderer;
    const TwoFactor = columns().find(
      (column) => column.field === 'twoFactorEnabled',
    )?.cellRenderer;
    return (
      <section aria-label="表格">
        <div data-testid="actions">{Actions && <Actions data={row} />}</div>
        <div data-testid="two-factor">
          {TwoFactor && <TwoFactor data={row} value={row.twoFactorEnabled} />}
        </div>
      </section>
    );
  },
}));

function columns(): ColDef<identity.IdentityUserGridRowDto>[] {
  return (grid.columnDefs ?? []) as ColDef<identity.IdentityUserGridRowDto>[];
}

function renderPage() {
  return render(
    <AntdApp>
      <Page />
    </AntdApp>,
  );
}

const api = {
  isDestroyed: () => false,
  setGridOption: vi.fn(),
  paginationGoToFirstPage: vi.fn(),
  updateGridOptions: vi.fn(),
  purgeInfiniteCache: vi.fn(),
  setGridAriaProperty: vi.fn(),
  setColumnsVisible: vi.fn(),
  getColumnState: vi.fn<() => ColumnState[]>(() => []),
  getFilterModel: vi.fn<() => FilterModel>(() => ({})),
};
function readyGrid() {
  act(() => {
    grid.onGridReady?.({
      api,
    } as unknown as GridReadyEvent<identity.IdentityUserGridRowDto>);
  });
}
async function requestBlock(
  overrides: Partial<IGetRowsParams<identity.IdentityUserGridRowDto>> = {},
) {
  const params: IGetRowsParams<identity.IdentityUserGridRowDto> = {
    startRow: 0,
    endRow: 20,
    filterModel: {},
    sortModel: [],
    context: {},
    api: api as unknown as GridApi<identity.IdentityUserGridRowDto>,
    successCallback: vi.fn(),
    failCallback: vi.fn(),
    ...overrides,
  };
  const source = grid.datasource;
  if (!source) throw new Error('用户列表缺少远程数据源');
  await act(async () => {
    await source.getRows(params);
  });
  return params;
}

describe('用户管理网格', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    viewer.initialState.currentUser.userid = 'u-admin';
    access = { ...fullAccess };
    row = {
      id: 'u-1',
      userName: 'alice',
      email: 'alice@example.test',
      name: 'Alice',
      surname: null,
      emailConfirmed: false,
      phoneNumber: null,
      phoneNumberConfirmed: false,
      isActive: true,
      lockoutEnabled: true,
      accessFailedCount: 0,
      lockoutEnd: null,
      creationTime: '2026-01-01T00:00:00Z',
      lastPasswordChangeTime: null,
      concurrencyStamp: 'stamp',
      isLocked: false,
      status: 'active',
      twoFactorEnabled: true,
    };
    vi.mocked(identity.queryIdentityUsers).mockResolvedValue({
      items: [row],
      totalCount: 121,
    });
    vi.mocked(identity.getAssignableRoles).mockResolvedValue({ items: [] });
  });

  it('把行区间、多列筛选和排序一并发给后端，并将总数和双因素状态交给网格', async () => {
    renderPage();
    const params = await requestBlock({
      startRow: 40,
      endRow: 60,
      sortModel: [
        { colId: 'creationTime', sort: 'desc' },
        { colId: 'userName', sort: 'asc' },
      ],
      filterModel: {
        userName: { filterType: 'text', type: 'startsWith', filter: 'ali' },
        status: { filterType: 'text', type: 'choice:active' },
      },
    });
    expect(identity.queryIdentityUsers).toHaveBeenCalledWith(
      {
        skipCount: 40,
        maxResultCount: 20,
        filters: [
          {
            field: 'userName',
            join: 'and',
            conditions: [{ op: 'startsWith', value: 'ali' }],
          },
          {
            field: 'status',
            join: 'and',
            conditions: [{ op: 'eq', value: 'active' }],
          },
        ],
        sort: [
          { field: 'creationTime', direction: 'desc' },
          { field: 'userName', direction: 'asc' },
        ],
      },
      expect.any(AbortSignal),
    );
    expect(params.successCallback).toHaveBeenCalledWith([row], 121);
    expect(identity.getAssignableRoles).toHaveBeenCalled();
    expect(identity.getTwoFactorStatuses).not.toHaveBeenCalled();
  });

  it('保留高级列、创建和导出入口', async () => {
    renderPage();
    await requestBlock();
    expect(
      screen.getByRole('button', { name: /新建用户/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /导\s*出/ })).toBeInTheDocument();
    expect(screen.queryByText('导入')).not.toBeInTheDocument();
    const hidden = columns()
      .filter((c) => c.initialHide || c.hide)
      .map((c) => c.colId ?? c.field);
    expect(hidden).toEqual(
      expect.arrayContaining([
        'emailConfirmed',
        'isLocked',
        'twoFactorEnabled',
        'accessFailedCount',
      ]),
    );
  });

  it('列设置调用网格 API，同一用户下次打开仍显示已选列', async () => {
    const page = renderPage();
    readyGrid();
    fireEvent.click(screen.getByRole('button', { name: '列设置' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: '失败次数' }));
    expect(api.setColumnsVisible).toHaveBeenCalledWith(
      ['accessFailedCount'],
      true,
    );
    page.unmount();
    const remembered = renderPage();
    await requestBlock();
    expect(
      columns().find((c) => c.field === 'accessFailedCount')?.initialHide,
    ).toBe(false);
    remembered.unmount();
    viewer.initialState.currentUser.userid = 'another-user';
    renderPage();
    await requestBlock();
    expect(
      columns().find((c) => c.field === 'accessFailedCount')?.initialHide,
    ).toBe(true);
  });

  it('导出使用点击时生效的列组合条件和多列排序，刷新重新读取服务端数据', async () => {
    vi.mocked(identity.exportIdentityUserQuery).mockResolvedValue({
      isQueued: true,
      message: '已排队',
    });
    renderPage();
    readyGrid();
    const search = screen.getByLabelText('搜索用户');
    fireEvent.change(search, { target: { value: '  alice  ' } });
    fireEvent.keyDown(search, { key: 'Enter', keyCode: 13 });
    await requestBlock();
    expect(identity.queryIdentityUsers).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: 'alice' }),
      expect.any(AbortSignal),
    );
    // 草稿还没提交，导出仍应沿用已显示列表的关键词。
    fireEvent.change(search, { target: { value: '未提交的新词' } });
    api.getFilterModel.mockReturnValueOnce({
      name: { filterType: 'text', type: 'contains', filter: 'li' },
      isLocked: { filterType: 'text', type: 'isFalse' },
    });
    api.getColumnState.mockReturnValueOnce([
      { colId: 'creationTime', sort: 'desc', sortIndex: 1 },
      { colId: 'userName', sort: 'asc', sortIndex: 0 },
      { colId: 'email' },
    ]);
    fireEvent.click(screen.getByRole('button', { name: /导\s*出/ }));
    await waitFor(() =>
      expect(identity.exportIdentityUserQuery).toHaveBeenCalledTimes(1),
    );
    expect(identity.exportIdentityUserQuery).toHaveBeenCalledWith(
      expect.objectContaining({
        keyword: 'alice',
        skipCount: 0,
        filters: [
          {
            field: 'name',
            join: 'and',
            conditions: [{ op: 'contains', value: 'li' }],
          },
          {
            field: 'isLocked',
            join: 'and',
            conditions: [{ op: 'eq', value: 'false' }],
          },
        ],
        sort: [
          { field: 'userName', direction: 'asc' },
          { field: 'creationTime', direction: 'desc' },
        ],
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: /刷\s*新/ }));
    expect(api.purgeInfiniteCache).toHaveBeenCalledTimes(1);
  });

  it('额外权限与账号操作在更多菜单，确认后才删除', async () => {
    renderPage();
    const actions = within(screen.getByTestId('actions'));
    expect(actions.getByText('编辑')).toBeInTheDocument();
    expect(actions.getByText('重置密码')).toBeInTheDocument();
    expect(actions.queryByText('额外权限')).not.toBeInTheDocument();
    fireEvent.click(actions.getByText('更多'));
    expect(await screen.findByText('额外权限')).toBeInTheDocument();
    expect(screen.getByText('通常通过角色授权')).toBeInTheDocument();
    expect(screen.getByText('禁用双因素')).toBeInTheDocument();
    fireEvent.click(screen.getByText('删除用户'));
    const confirm = await screen.findByRole('dialog');
    expect(confirm).toHaveTextContent('确认删除该用户？');
    expect(identity.deleteUser).not.toHaveBeenCalled();
  });

  it('当前账号没有删除、模拟登录和切换双因素入口', async () => {
    row = { ...row, id: 'u-admin' };
    access.canImpersonateUsers = true;
    renderPage();
    fireEvent.click(within(screen.getByTestId('actions')).getByText('更多'));
    expect(await screen.findByText('额外权限')).toBeInTheDocument();
    for (const label of ['删除用户', '模拟此用户', '禁用双因素', '要求改密']) {
      expect(screen.queryByText(label)).not.toBeInTheDocument();
    }
  });

  it('只读角色保留列表，不能用敏感字段排序或筛选，也不读取角色和双因素管理端点', async () => {
    access = {};
    row = { ...row, twoFactorEnabled: null };
    renderPage();
    await requestBlock();
    expect(screen.getByTestId('actions')).toBeEmptyDOMElement();
    expect(
      screen.queryByRole('button', { name: /新建用户|导\s*出/ }),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId('two-factor')).toHaveTextContent('—');
    for (const field of ['email', 'phoneNumber', 'twoFactorEnabled']) {
      const column = columns().find((c) => c.field === field);
      if (column)
        expect(column).toMatchObject({ sortable: false, filter: false });
    }
    expect(identity.getAssignableRoles).not.toHaveBeenCalled();
    expect(identity.getTwoFactorStatuses).not.toHaveBeenCalled();
  });
});
