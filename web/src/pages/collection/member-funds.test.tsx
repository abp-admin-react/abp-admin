import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import type { ColDef, GridApi } from 'ag-grid-community';
import type { AgGridReactProps } from 'ag-grid-react';
import {
  type ComponentPropsWithoutRef,
  type ReactNode,
  useEffect,
  useState,
} from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { MemberBalanceGridRowDto } from '@/abp/collectionAdmin';
import MemberFundsPage from './member-funds';

const mocks = vi.hoisted(() => ({
  users: vi.fn(),
  accounts: vi.fn(),
  balances: vi.fn(),
  transactions: vi.fn(),
  access: vi.fn(),
  search: vi.fn(),
  model: vi.fn(),
  push: vi.fn(),
  replace: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useLocation: () => ({ search: mocks.search() }),
  useModel: mocks.model,
  history: { push: mocks.push, replace: mocks.replace },
  Link: ({ to, ...props }: ComponentPropsWithoutRef<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('@/abp/identity', () => ({
  getUsers: mocks.users,
}));
vi.mock('@/abp/collectionAdmin', () => ({
  BALANCE_ACTION_LABELS: {},
  queryMemberBalanceAccounts: mocks.accounts,
  getMemberBalances: mocks.balances,
  getMemberTxns: mocks.transactions,
}));
vi.mock('ag-grid-react', () => ({
  AgGridReact: ({
    datasource,
    columnDefs,
  }: AgGridReactProps<MemberBalanceGridRowDto>) => {
    const [rows, setRows] = useState<MemberBalanceGridRowDto[]>([]);
    useEffect(() => {
      let destroyed = false;
      void datasource?.getRows({
        startRow: 0,
        endRow: 20,
        context: {},
        filterModel: {},
        sortModel: [{ colId: 'updatedAt', sort: 'desc' }],
        api: {
          isDestroyed: () => destroyed,
          setGridOption: vi.fn(),
          updateGridOptions: vi.fn(),
        } as unknown as GridApi<MemberBalanceGridRowDto>,
        successCallback: setRows,
        failCallback: vi.fn(),
      });
      return () => {
        destroyed = true;
      };
    }, [datasource]);
    const columns =
      columnDefs?.filter(
        (column): column is ColDef<MemberBalanceGridRowDto> =>
          !('children' in column),
      ) ?? [];
    return (
      <table>
        <tbody>
          {rows.map((row) => (
            <tr key={row.userId}>
              {columns.map((column) => (
                <td key={column.field ?? column.colId}>
                  {column.cellRenderer
                    ? column.cellRenderer({ data: row })
                    : column.field
                      ? row[column.field]
                      : null}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    );
  },
}));

function balance(value: string) {
  return {
    items: [{ currency: 'Usdt', balance: value, frozen: '0', total: value }],
  };
}

const userId = '74326c91-6b75-4ef0-9429-3f6e8af7c65f';
const alice = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const betty = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const txnId = '50326c91-6b75-4ef0-9429-3f6e8af7c65f';
const relatedId = '26326c91-6b75-4ef0-9429-3f6e8af7c65f';
const withdrawalId = '38326c91-6b75-4ef0-9429-3f6e8af7c65f';
const row = {
  id: txnId,
  action: 'DepositCredit',
  currency: 'Usdt',
  amount: '99.18',
  balanceAfter: '115.987654321',
  creationTime: '2026-11-17T01:00:00Z',
  businessType: 'TopUp',
  depositOrderId: relatedId,
  txHash: '0x8fb7ca3b15d1f596fd6b1e3d99edcbd4202e2d11bfd8a45469db50b11612e05a',
};
const withdrawalTxn = {
  ...row,
  action: 'WithdrawFreeze',
  businessId: withdrawalId,
  businessType: 'Withdraw',
  depositOrderId: null,
};

async function selectUser(keyword: string) {
  const input = screen.getByRole('combobox');
  fireEvent.mouseDown(input);
  fireEvent.change(input, { target: { value: keyword } });
  fireEvent.click(await screen.findByRole('option', { name: keyword }));
}

function renderPage() {
  const view = render(<MemberFundsPage />);
  const go = (url: string) => {
    mocks.search.mockReturnValue(
      url.includes('?') ? url.slice(url.indexOf('?')) : '',
    );
    view.rerender(<MemberFundsPage />);
  };
  mocks.push.mockImplementation(go);
  mocks.replace.mockImplementation(go);
  return view;
}

describe('用户余额查询', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.access.mockReturnValue({
      canManageUsers: true,
      canCreateBalanceAdjustments: true,
    });
    mocks.search.mockReturnValue('');
    mocks.model.mockReturnValue({});
    mocks.users.mockImplementation(({ filter }) =>
      Promise.resolve({
        items: [
          {
            id:
              ({ Alice: alice, Betty: betty } as Record<string, string>)[
                filter
              ] ?? filter,
            userName: filter,
          },
        ],
      }),
    );
    mocks.accounts.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.transactions.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('列出已有余额账户，流水链接进入该用户', async () => {
    mocks.accounts.mockResolvedValue({
      totalCount: 1,
      items: [
        {
          userId,
          usdtBalance: '1.5',
          usdtFrozen: '0.25',
          usdcBalance: '0',
          usdcFrozen: '0',
          updatedAt: '2026-11-17T01:00:00Z',
          userName: 'alice',
          email: 'a@x.com',
        },
      ],
    });
    renderPage();
    expect(await screen.findByText('alice (a@x.com)')).toBeTruthy();
    expect(screen.getByText('1.5')).toBeTruthy();
    expect(screen.getByText('0.25')).toBeTruthy();
    expect(screen.getByRole('link', { name: '流水' })).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}`,
    );
    expect(mocks.balances).not.toHaveBeenCalled();
    expect(mocks.accounts).toHaveBeenCalledWith(
      {
        skipCount: 0,
        maxResultCount: 20,
        filters: [],
        sort: [{ field: 'updatedAt', direction: 'desc' }],
      },
      expect.any(AbortSignal),
    );
  });

  it('余额账户关键词交给资金接口，用户目录和流水不受影响', async () => {
    renderPage();
    await waitFor(() => expect(mocks.accounts).toHaveBeenCalled());
    const input = screen.getByLabelText('搜索余额账户');
    fireEvent.change(input, { target: { value: '  member  ' } });
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 13 });
    await waitFor(() =>
      expect(mocks.accounts).toHaveBeenCalledWith(
        expect.objectContaining({ keyword: 'member', skipCount: 0 }),
        expect.any(AbortSignal),
      ),
    );
    expect(mocks.users).not.toHaveBeenCalled();
    expect(mocks.transactions).not.toHaveBeenCalled();
  });

  it('更换用户后忽略旧余额查询，并保留精确金额和调账入口', async () => {
    let resolveOld!: (value: ReturnType<typeof balance>) => void;
    mocks.balances.mockImplementation((id) =>
      id === alice
        ? new Promise((resolve) => {
            resolveOld = resolve;
          })
        : Promise.resolve(balance('3.000000000000000001')),
    );
    renderPage();
    await selectUser('Alice');
    await waitFor(() => expect(mocks.balances).toHaveBeenCalledWith(alice));
    await selectUser('Betty');
    await waitFor(() =>
      expect(screen.getAllByText('3.000000000000000001')).toHaveLength(2),
    );
    await act(async () => resolveOld(balance('42.99')));
    expect(screen.queryByText('42.99')).toBeNull();
    expect(screen.getByRole('link', { name: '申请余额调整' })).toHaveAttribute(
      'href',
      `/finance/balance-adjustments?createFor=${betty}`,
    );
  });

  it('余额查询失败时可以重试，查看权限不会提供调整入口', async () => {
    mocks.access.mockReturnValue({ canManageUsers: true });
    mocks.balances
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValue(balance('7.123'));
    renderPage();
    await selectUser('Alice');
    expect(await screen.findByText('加载余额和流水失败')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    await waitFor(() => expect(screen.getAllByText('7.123')).toHaveLength(2));
    expect(screen.queryByRole('link', { name: '申请余额调整' })).toBeNull();
  });

  it('独立资金查看权限可以按用户 ID 查询，不访问用户目录', async () => {
    mocks.access.mockReturnValue({ canViewMemberFunds: true });
    mocks.balances.mockResolvedValue(balance('5.00'));
    renderPage();
    expect(screen.queryByRole('combobox')).toBeNull();
    const input = screen.getByLabelText('用户 ID');
    const query = screen.getByRole('button', { name: '查询余额与流水' });
    fireEvent.change(input, { target: { value: 'not-an-id' } });
    fireEvent.click(query);
    expect(await screen.findByText('请输入带连字符的完整用户 ID')).toBeTruthy();
    expect(mocks.balances).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: userId } });
    fireEvent.click(query);
    await waitFor(() => expect(mocks.balances).toHaveBeenCalledWith(userId));
    expect(mocks.transactions).toHaveBeenCalledWith(
      expect.objectContaining({ userId }),
    );
    expect(mocks.users).not.toHaveBeenCalled();
  });

  it('刷新后用用户 ID 和流水 ID 精确查询，充值链接使用充值单 ID', async () => {
    mocks.access.mockReturnValue({
      canViewMemberFunds: true,
      canViewDepositRecords: true,
    });
    mocks.search.mockReturnValue(`?userId=${userId}&txnId=${txnId}`);
    mocks.balances.mockResolvedValue(balance('8.13'));
    mocks.transactions.mockResolvedValue({ items: [row], totalCount: 1 });
    renderPage();
    expect(
      await screen.findByRole('link', { name: `查看账户充值 ${relatedId}` }),
    ).toHaveAttribute(
      'href',
      `/operations/payments?view=deposits&depositId=${relatedId}`,
    );
    expect(
      screen.getByRole('link', { name: '查看该用户全部流水' }),
    ).toHaveAttribute('href', `/collection/member-funds?userId=${userId}`);
    expect(mocks.transactions).toHaveBeenCalledExactlyOnceWith({
      userId,
      txnId,
      skipCount: 0,
      maxResultCount: 1,
    });
    expect(mocks.users).not.toHaveBeenCalled();
  });

  it('目标流水缺失时不会误展示返回的其他流水', async () => {
    mocks.access.mockReturnValue({ canViewMemberFunds: true });
    mocks.search.mockReturnValue(`?userId=${userId}&txnId=${txnId}`);
    mocks.balances.mockResolvedValue(balance('8.13'));
    mocks.transactions.mockResolvedValue({
      items: [{ ...row, id: relatedId }],
      totalCount: 1,
    });
    renderPage();
    expect(await screen.findByText('未找到该余额流水')).toBeTruthy();
    expect(screen.queryByText('115.987654321')).toBeNull();
  });

  it('仅可看本人订单及提现设置时，查看他人资金只显示订单和提现单据编号', async () => {
    mocks.access.mockReturnValue({
      canViewMemberFunds: true,
      canViewOwnOrders: true,
      canOpenWithdrawalWorkspace: true,
    });
    mocks.model.mockReturnValue({
      initialState: { currentUser: { userid: relatedId } },
    });
    mocks.search.mockReturnValue(`?userId=${userId}`);
    mocks.balances.mockResolvedValue(balance('8.13'));
    mocks.transactions.mockResolvedValue({
      items: [
        {
          ...row,
          action: 'BusinessDebit',
          businessId: relatedId,
          businessType: 'Order',
          depositOrderId: null,
        },
        {
          ...row,
          id: relatedId,
          action: 'WithdrawFreeze',
          businessId: txnId,
          businessType: 'Withdraw',
          depositOrderId: null,
        },
      ],
      totalCount: 2,
    });
    renderPage();
    expect(await screen.findByText(`业务订单 ${relatedId}`)).toBeTruthy();
    expect(screen.getByText(`提现单 ${txnId}`)).toBeTruthy();
    expect(
      screen.queryByRole('link', { name: /查看业务订单|查看提现单/ }),
    ).toBeNull();
  });

  it('按提现单展示同一用户的多条账变，并提供保留该提现范围的流水精确链接', async () => {
    mocks.access.mockReturnValue({
      canViewMemberFunds: true,
      canAdminWithdrawals: true,
    });
    mocks.search.mockReturnValue(
      `?userId=${userId}&withdrawalId=${withdrawalId}`,
    );
    mocks.balances.mockResolvedValue(balance('8.13'));
    mocks.transactions.mockResolvedValue({
      items: [
        withdrawalTxn,
        { ...withdrawalTxn, id: relatedId, action: 'WithdrawSettle' },
      ],
      totalCount: 2,
    });
    renderPage();
    expect(await screen.findByText('WithdrawFreeze')).toBeTruthy();
    expect(screen.getByText('WithdrawSettle')).toBeTruthy();
    expect(screen.getByText('提现关联流水')).toBeTruthy();
    expect(
      screen.getByRole('link', { name: `查看余额流水 ${txnId}` }),
    ).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}&withdrawalId=${withdrawalId}&txnId=${txnId}`,
    );
    expect(mocks.transactions).toHaveBeenCalledExactlyOnceWith({
      userId,
      withdrawalId,
      skipCount: 0,
      maxResultCount: 20,
    });
    expect(mocks.users).not.toHaveBeenCalled();
  });

  it('同时加载提现和流水深链会提交两项筛选条件', async () => {
    mocks.access.mockReturnValue({ canViewMemberFunds: true });
    mocks.search.mockReturnValue(
      `?userId=${userId}&withdrawalId=${withdrawalId}&txnId=${txnId}`,
    );
    mocks.balances.mockResolvedValue(balance('8.13'));
    mocks.transactions.mockResolvedValue({
      items: [withdrawalTxn],
      totalCount: 1,
    });
    renderPage();
    expect(await screen.findByText(`已定位余额流水 ${txnId}`)).toBeTruthy();
    expect(
      screen.getByRole('link', { name: '查看该提现的全部流水' }),
    ).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}&withdrawalId=${withdrawalId}`,
    );
    expect(mocks.transactions).toHaveBeenCalledExactlyOnceWith({
      userId,
      withdrawalId,
      txnId,
      skipCount: 0,
      maxResultCount: 1,
    });
  });

  it.each([
    `?withdrawalId=${withdrawalId}`,
    `?userId=${userId}&withdrawalId=not-a-guid`,
    `?userId=${userId}&withdrawalId=${withdrawalId}&txnId=not-a-guid`,
  ])('无效的提现流水链接不读取账户或流水 (%s)', async (search) => {
    mocks.access.mockReturnValue({ canViewMemberFunds: true });
    mocks.search.mockReturnValue(search);
    renderPage();
    expect(await screen.findByText('余额流水链接无效')).toBeTruthy();
    expect(mocks.balances).not.toHaveBeenCalled();
    expect(mocks.transactions).not.toHaveBeenCalled();
  });

  it('从用户 A 的深链换到用户 B 后，查询失败也不会显示旧余额或流水', async () => {
    mocks.access.mockReturnValue({ canViewMemberFunds: true });
    mocks.search.mockReturnValue(
      `?userId=${userId}&withdrawalId=${withdrawalId}`,
    );
    mocks.balances
      .mockResolvedValueOnce(balance('8.13'))
      .mockRejectedValue(new Error('network'));
    mocks.transactions
      .mockResolvedValueOnce({ items: [withdrawalTxn], totalCount: 1 })
      .mockResolvedValue({ items: [], totalCount: 0 });
    const view = renderPage();
    expect(await screen.findByText('115.987654321')).toBeTruthy();
    expect(screen.getAllByText('8.13')).toHaveLength(2);
    mocks.search.mockReturnValue(
      `?userId=${relatedId}&withdrawalId=${withdrawalId}`,
    );
    view.rerender(<MemberFundsPage />);
    expect(await screen.findByText('加载余额和流水失败')).toBeTruthy();
    expect(screen.queryByText('115.987654321')).toBeNull();
    expect(screen.queryByText('8.13')).toBeNull();
  });
});
