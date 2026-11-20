import { actionCell, fetchGridBlock } from '@root/tests/gridInteraction';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App } from 'antd';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ModuleAvailabilityContext } from '@/hooks/useModuleAvailability';
import VouchersPage from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  location: vi.fn(),
  table: vi.fn(),
  getAdjusts: vi.fn(),
  getVoucher: vi.fn(),
  getVouchers: vi.fn(),
  approveAdjust: vi.fn(),
  updateAdjust: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useLocation: mocks.location,
  useModel: () => ({
    initialState: { currentUser: { userid: 'operator', name: '审批员' } },
  }),
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('ag-grid-react', () => ({
  AgGridReact: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/finance', async (original) => ({
  ...(await original<typeof import('@/abp/finance')>()),
  getAdjusts: mocks.getAdjusts,
  getVoucher: mocks.getVoucher,
  getVouchers: mocks.getVouchers,
  financeGridQueries: {
    vouchers: mocks.getVouchers,
    adjustments: mocks.getAdjusts,
  },
  approveAdjust: mocks.approveAdjust,
  updateAdjust: mocks.updateAdjust,
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));

const withdrawalId = '21a49a4f-d42e-4fc1-8665-75f0cce722a9';
const withdrawalPath = `/collection/withdrawals?status=all&withdrawalId=${withdrawalId}`;

function vouchers() {
  return (
    <App>
      <VouchersPage />
    </App>
  );
}

describe('会计凭证的提现单链接', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.access.mockReturnValue({ canViewFinanceVouchers: true });
    mocks.getVouchers.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getAdjusts.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('使用提现单业务类型和单号精确过滤凭证，允许返回提现或查看全部凭证', async () => {
    mocks.access.mockReturnValue({
      canViewFinanceVouchers: true,
      canViewWeb3Operations: true,
      canAdminWithdrawals: true,
    });
    mocks.location.mockReturnValue({
      search: `?bizType=withdraw&bizRefId=${withdrawalId.toUpperCase()}`,
    });
    const view = render(vouchers());
    const table = mocks.table.mock.lastCall?.[0];
    await fetchGridBlock(table);
    expect(mocks.getVouchers).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ skipCount: 0, maxResultCount: 20 }),
      expect.any(AbortSignal),
      `?bizType=withdraw&bizRefId=${withdrawalId}`,
    );
    expect(screen.getByRole('link', { name: '返回提现单' })).toHaveAttribute(
      'href',
      withdrawalPath,
    );
    expect(screen.getByRole('link', { name: '查看全部凭证' })).toHaveAttribute(
      'href',
      '/finance/vouchers',
    );

    mocks.location.mockReturnValue({ search: '' });
    view.rerender(vouchers());
    const unfilteredTable = mocks.table.mock.lastCall?.[0];
    expect(screen.queryByRole('link', { name: '返回提现单' })).toBeNull();
    await fetchGridBlock(unfilteredTable, {
      filterModel: {
        bizRefId: { filterType: 'text', type: 'equals', filter: 'order' },
      },
    });
    expect(mocks.getVouchers).toHaveBeenLastCalledWith(
      expect.objectContaining({
        filters: [
          {
            field: 'bizRefId',
            join: 'and',
            conditions: [{ op: 'eq', value: 'order' }],
          },
        ],
      }),
      expect.any(AbortSignal),
      '',
    );
  });

  it('凭证查看权限不会额外提供提现单入口', () => {
    mocks.location.mockReturnValue({
      search: `?bizType=withdraw&bizRefId=${withdrawalId}`,
    });
    render(vouchers());
    expect(screen.getByText(`提现单 ${withdrawalId} 的会计凭证`)).toBeTruthy();
    expect(screen.queryByRole('link', { name: '返回提现单' })).toBeNull();
  });

  it('充值单入口只查询该单的充值凭证，并可返回原充值单', async () => {
    mocks.access.mockReturnValue({
      canViewFinanceVouchers: true,
      canViewWeb3Operations: true,
      canViewCollections: true,
    });
    mocks.location.mockReturnValue({
      search: `?bizType=deposit&bizRefId=${withdrawalId.toUpperCase()}`,
    });
    render(vouchers());
    const table = mocks.table.mock.lastCall?.[0];
    await fetchGridBlock(table);
    expect(mocks.getVouchers).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ skipCount: 0, maxResultCount: 20 }),
      expect.any(AbortSignal),
      `?bizType=deposit&bizRefId=${withdrawalId}`,
    );
    expect(screen.getByRole('link', { name: '返回充值单' })).toHaveAttribute(
      'href',
      `/operations/payments?view=deposits&depositId=${withdrawalId}`,
    );
  });

  it('无效单号不显示全部凭证，也不请求凭证详情', () => {
    mocks.location.mockReturnValue({
      search: '?bizType=withdraw&bizRefId=bad-id&voucherId=bogus',
    });
    render(vouchers());
    expect(screen.getByText('提现凭证链接无效')).toBeTruthy();
    expect(screen.queryByRole('link', { name: '返回提现单' })).toBeNull();
    expect(mocks.table).not.toHaveBeenCalled();
    expect(mocks.getVoucher).not.toHaveBeenCalled();
  });

  it('单张提现凭证可返回对应提现单，保留凭证来源查询', async () => {
    mocks.access.mockReturnValue({
      canViewFinanceVouchers: true,
      canViewFinanceBizDocs: true,
      canViewWeb3Operations: true,
      canAdminWithdrawals: true,
    });
    mocks.location.mockReturnValue({ search: '?voucherId=voucher' });
    mocks.getVoucher.mockResolvedValue({
      id: 'voucher',
      voucherNo: 'V-1',
      bizType: 'withdraw',
      bizRefId: withdrawalId,
    });
    render(vouchers());
    expect(mocks.getVoucher).toHaveBeenCalledWith('voucher');
    expect(
      await screen.findByRole('link', { name: '返回提现单' }),
    ).toHaveAttribute('href', withdrawalPath);
    expect(screen.getByRole('link', { name: '查询凭证来源' })).toHaveAttribute(
      'href',
      `/finance/biz-docs?bizRefId=${withdrawalId}`,
    );
  });

  it.each([
    ['canApproveFinanceAdjust', '账簿调账待审批', 'Pending'],
    ['canRejectFinanceAdjust', '账簿调账待审批', 'Pending'],
    ['canSubmitFinanceAdjust', '账簿调账草稿', 'Draft,Rejected'],
    ['canReverseFinanceAdjust', '已过账调账', 'Posted,Reversed'],
  ])(
    '独立的 %s 角色只加载已有权限的调账列表',
    async (permission, label, status) => {
      mocks.access.mockReturnValue({ [permission]: true });
      mocks.location.mockReturnValue({ search: '?voucherId=voucher' });
      render(vouchers());

      expect(
        screen.getByRole('tab', { name: label, selected: true }),
      ).toBeTruthy();
      expect(screen.queryByRole('tab', { name: '凭证列表' })).toBeNull();
      const table = mocks.table.mock.lastCall?.[0];
      await fetchGridBlock(table);
      expect(mocks.getAdjusts).toHaveBeenCalledExactlyOnceWith(
        expect.objectContaining({ skipCount: 0, maxResultCount: 20 }),
        expect.any(AbortSignal),
        `?status=${encodeURIComponent(status)}`,
      );
      expect(mocks.getVouchers).not.toHaveBeenCalled();
      expect(mocks.getVoucher).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['canReverseFinanceAdjust', false],
    ['canReverseFinanceVouchers', true],
  ])('凭证红冲动作按 %s 对应的服务端权限显示', (permission, visible) => {
    mocks.access.mockReturnValue({
      canViewFinanceVouchers: true,
      [permission]: true,
    });
    mocks.location.mockReturnValue({ search: '' });
    render(vouchers());

    const table = mocks.table.mock.lastCall?.[0];
    const actions = actionCell(table, {
      id: 'voucher',
      status: 'Posted',
      bizType: 'manual-adjust',
    });
    const row = render(actions);
    expect(!!row.queryByText('红冲')).toBe(visible);
  });

  it('停用财务后仍可审批他人的待审调账，且不需要凭证查看授权', async () => {
    mocks.access.mockReturnValue({ canApproveFinanceAdjust: true });
    mocks.location.mockReturnValue({ search: '' });
    render(
      <ModuleAvailabilityContext.Provider
        value={{ version: 1, initialized: true, states: { Finance: false } }}
      >
        {vouchers()}
      </ModuleAvailabilityContext.Provider>,
    );
    expect(screen.getByText('财务工作区已停止受理新调账')).toBeTruthy();
    const table = mocks.table.mock.lastCall?.[0];
    const row = render(
      <App>
        {actionCell(table, {
          id: 'adjustment',
          status: 'Pending',
          applicantId: 'other',
        })}
      </App>,
    );
    fireEvent.click(row.getByText('审批'));
    const confirmation = await screen.findByRole('tooltip');
    fireEvent.click(within(confirmation).getByRole('button', { name: 'OK' }));
    await waitFor(() =>
      expect(mocks.approveAdjust).toHaveBeenCalledExactlyOnceWith(
        'adjustment',
        { id: 'operator', name: '审批员' },
      ),
    );
    expect(mocks.getVouchers).not.toHaveBeenCalled();
  });

  it('编辑已存在的账簿调账会保留借贷科目和归属', async () => {
    mocks.access.mockReturnValue({ canCreateFinanceAdjust: true });
    mocks.location.mockReturnValue({ search: '' });
    render(vouchers());
    const table = mocks.table.mock.lastCall?.[0];
    const row = render(
      actionCell(table, {
        id: 'adjustment',
        adjustNo: 'ADJ-1',
        status: 'Draft',
        applicantId: 'operator',
        amount: 15,
        currency: 'USD',
        summary: '原摘要',
        debitOwnerType: 'Platform',
        debitOwnerId: '',
        creditOwnerType: 'User',
        creditOwnerId: 'customer',
        lines: [{ accountCode: '1002' }, { accountCode: '2002' }],
      }),
    );
    fireEvent.click(row.getByText('编辑'));
    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByLabelText('贷方科目编码（可空，默认 3101）'),
    ).toHaveValue('2002');
    const ownerId = within(dialog).getByLabelText('借方 OwnerId');
    expect(ownerId).toHaveValue('');
    const form = ownerId.closest('form');
    if (form) fireEvent.submit(form);
    await waitFor(() =>
      expect(mocks.updateAdjust).toHaveBeenCalledWith(
        'adjustment',
        expect.objectContaining({
          debitAccountCode: '1002',
          debitOwnerType: 'Platform',
          debitOwnerId: '',
          creditAccountCode: '2002',
          creditOwnerType: 'User',
          creditOwnerId: 'customer',
        }),
      ),
    );
  });
});
