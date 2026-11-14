import { render } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PaymentsPage from './index';

const mocks = vi.hoisted(() => ({
  table: vi.fn(),
  getPayments: vi.fn(),
  useLocation: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: () => ({}),
  useLocation: mocks.useLocation,
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/collectionAdmin', () => ({
  getCollectionPayments: mocks.getPayments,
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));

describe('渠道收款记录', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getPayments.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '',
    });
  });

  it.each([
    ['alipay', 'AlipayPrecreate'],
    ['wechat', 'WechatNative'],
    ['stripe', 'StripeCheckout'],
  ] as const)('%s 页签固定查询本渠道', async (view, channel) => {
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: `?view=${view}`,
    });
    render(<PaymentsPage />);
    const props = mocks.table.mock.calls.at(-1)?.[0];
    await props.request({
      current: 2,
      pageSize: 10,
      channel: 'Other',
      status: 'Completed',
    });
    expect(mocks.getPayments).toHaveBeenCalledWith({
      skipCount: 10,
      maxResultCount: 10,
      filter: undefined,
      status: 'Completed',
      channel,
    });
    expect(
      props.columns.find(
        (column: { dataIndex: string }) => column.dataIndex === 'channel',
      ),
    ).toMatchObject({ search: false, hideInTable: true });
  });

  it('跨渠道收款与退款可选择渠道', async () => {
    render(<PaymentsPage />);
    const props = mocks.table.mock.calls.at(-1)?.[0];
    await props.request({ channel: 'WechatNative' });
    expect(mocks.getPayments).toHaveBeenCalledWith(
      expect.objectContaining({ channel: 'WechatNative' }),
    );
    expect(
      props.columns.find(
        (column: { dataIndex: string }) => column.dataIndex === 'channel',
      ),
    ).toMatchObject({ search: true, hideInTable: false });
  });
});
