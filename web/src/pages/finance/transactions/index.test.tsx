import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import TransactionsPage from './index';

const mocks = vi.hoisted(() => ({
  table: vi.fn(),
  list: vi.fn(),
  detail: vi.fn(),
  location: vi.fn(),
  access: vi.fn(),
}));
vi.mock('@umijs/max', () => ({
  Link: ({ to, children }: { to: string; children: string }) => (
    <a href={to}>{children}</a>
  ),
  useAccess: mocks.access,
  useLocation: mocks.location,
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/finance', () => ({
  getVouchers: mocks.list,
  getVoucher: mocks.detail,
  financeErrorText: () => '加载失败',
  isFinanceForbidden: () => false,
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));

describe('分录查询页', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.access.mockReturnValue({ canViewFinanceVouchers: true });
    mocks.location.mockReturnValue({ search: '' });
    mocks.list.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('按用户、邮箱、事务和业务条件查询', async () => {
    render(
      <AntdApp>
        <TransactionsPage />
      </AntdApp>,
    );
    const table = mocks.table.mock.calls[0][0];
    await table.request({
      current: 2,
      pageSize: 10,
      voucherId: 'voucher',
      userId: 'user',
      email: 'user@example.test',
      bizType: 'deposit',
      bizRefId: 'deposit-id',
      phase: 'pay',
      dateRange: ['2026-11-01T00:00:00Z', '2026-11-02T00:00:00Z'],
    });
    expect(mocks.list).toHaveBeenCalledWith({
      skipCount: 10,
      maxResultCount: 10,
      voucherId: 'voucher',
      userId: 'user',
      email: 'user@example.test',
      bizType: 'deposit',
      bizRefId: 'deposit-id',
      phase: 'pay',
      startTime: '2026-11-01T00:00:00.000Z',
      endTime: '2026-11-02T00:00:00.000Z',
    });
    expect(screen.queryByRole('button', { name: '新建账簿调账' })).toBeNull();
    expect(
      screen.getByRole('link', { name: '返回会计凭证' }).getAttribute('href'),
    ).toBe('/finance/vouchers');
  });

  it('凭证链接打开账户、余额桶及精确余额明细', async () => {
    mocks.location.mockReturnValue({ search: '?voucherId=linked' });
    mocks.access.mockReturnValue({
      canViewFinanceVouchers: true,
      canViewBalanceAdjustments: true,
    });
    mocks.detail.mockResolvedValue({
      id: 'linked',
      voucherNo: 'V-LINKED',
      bizType: 'wallet-adjust',
      bizRefId: 'adjustment',
      phase: 'post',
      creationTime: '2026-11-13T00:00:00Z',
      entries: [
        {
          seq: 1,
          accountCode: '2201',
          ownerId: 'user',
          bucket: '',
          debitText: '0',
          creditText: '0.000000000000000001',
          balanceAfterText: '10.000000000000000001',
        },
      ],
    });
    render(
      <AntdApp>
        <TransactionsPage />
      </AntdApp>,
    );
    await waitFor(() =>
      expect(
        mocks.table.mock.calls.some(
          ([props]) =>
            props.dataSource?.[0]?.balanceAfterText === '10.000000000000000001',
        ),
      ).toBe(true),
    );
    expect(mocks.detail).toHaveBeenCalledWith('linked');
    expect(screen.getByRole('link', { name: 'linked' })).toHaveAttribute(
      'href',
      '/finance/vouchers?voucherId=linked',
    );
    expect(screen.getByRole('link', { name: 'adjustment' })).toHaveAttribute(
      'href',
      '/finance/balance-adjustments?adjustmentId=adjustment',
    );
    const detailTable = mocks.table.mock.calls.find(
      ([props]) => props.dataSource,
    )?.[0];
    expect(
      detailTable.columns.map(
        (column: { dataIndex: string }) => column.dataIndex,
      ),
    ).toContain('bucket');
    expect(
      detailTable.columns.map(
        (column: { dataIndex: string }) => column.dataIndex,
      ),
    ).toContain('balanceAfterText');
  });

  it('只展示最后选中的流水，忽略迟到的链接或表格请求', async () => {
    mocks.location.mockReturnValue({ search: '?voucherId=linked' });
    const replies: Record<string, (value: unknown) => void> = {};
    mocks.detail.mockImplementation((id: string) => new Promise((resolve) => { replies[id] = resolve; }));
    render(<AntdApp><TransactionsPage /></AntdApp>);
    await waitFor(() => expect(replies.linked).toBeTruthy());
    const table = mocks.table.mock.calls[0][0];
    const operation = table.columns.find((column: { valueType?: string }) => column.valueType === 'option');
    const first = render(operation.render(null, { id: 'first' }));
    fireEvent.click(first.getByRole('button', { name: '查看分录' }));
    first.unmount();
    const second = render(operation.render(null, { id: 'second' }));
    fireEvent.click(second.getByRole('button', { name: '查看分录' }));
    second.unmount();
    const result = (id: string) => ({ id, bizType: 'wallet-adjust', creationTime: '2026-11-13T00:00:00Z', entries: [] });
    await act(async () => replies.second(result('second')));
    expect(await screen.findByText('second')).toBeTruthy();
    await act(async () => { replies.first(result('first')); replies.linked(result('linked')); });
    expect(screen.getByText('second')).toBeTruthy();
    expect(screen.queryByText('first')).toBeNull();
    expect(screen.queryByText('linked')).toBeNull();
  });
});
