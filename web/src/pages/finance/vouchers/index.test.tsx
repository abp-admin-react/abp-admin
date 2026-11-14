import { render, screen } from '@testing-library/react';
import { App } from 'antd';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import VouchersPage from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  location: vi.fn(),
  table: vi.fn(),
  getVoucher: vi.fn(),
  getVouchers: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useLocation: mocks.location,
  useModel: () => ({}),
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/finance', async (original) => ({
  ...(await original<typeof import('@/abp/finance')>()),
  getVoucher: mocks.getVoucher,
  getVouchers: mocks.getVouchers,
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));

const withdrawalId = '21a49a4f-d42e-4fc1-8665-75f0cce722a9';
const withdrawalPath = `/web3/operations?view=withdrawals&status=all&withdrawalId=${withdrawalId}`;

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
    expect(table.search).toBe(false);
    await table.request({ current: 1, pageSize: 20 });
    expect(mocks.getVouchers).toHaveBeenCalledExactlyOnceWith({
      skipCount: 0,
      maxResultCount: 20,
      bizType: 'withdraw',
      bizRefId: withdrawalId,
      filter: undefined,
    });
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
    expect(unfilteredTable.search).toEqual({ labelWidth: 'auto' });
    expect(screen.queryByRole('link', { name: '返回提现单' })).toBeNull();
    await unfilteredTable.request({ bizType: 'payment', bizRefId: 'order' });
    expect(mocks.getVouchers).toHaveBeenLastCalledWith(
      expect.objectContaining({ bizType: 'payment', bizRefId: 'order' }),
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
    await table.request({ current: 1, pageSize: 20, bizType: 'withdraw' });
    expect(mocks.getVouchers).toHaveBeenCalledExactlyOnceWith({
      skipCount: 0,
      maxResultCount: 20,
      bizType: 'deposit',
      bizRefId: withdrawalId,
      filter: undefined,
    });
    expect(screen.getByRole('link', { name: '返回充值单' })).toHaveAttribute(
      'href',
      `/web3/operations?view=deposits&depositId=${withdrawalId}`,
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
});
