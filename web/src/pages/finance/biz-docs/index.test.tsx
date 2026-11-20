import { actionCell, fetchGridBlock } from '@root/tests/gridInteraction';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { FinBizDocDto } from '@/abp/finance';
import BizDocsPage from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  location: vi.fn(),
  list: vi.fn(),
  mockMode: vi.fn(),
  table: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
  useAccess: mocks.access,
  useLocation: mocks.location,
}));
vi.mock('@/abp/finance', () => ({
  getBizDocs: mocks.list,
  isMock: mocks.mockMode,
}));
vi.mock('@/abp/businessGrid', async (original) => ({
  ...(await original<typeof import('@/abp/businessGrid')>()),
  businessGridQueries: { bizDocs: mocks.list },
}));
vi.mock('ag-grid-react', () => ({
  AgGridReact: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));

const sourceRow: FinBizDocDto = {
  id: 'voucher-1',
  bizType: 'wallet-adjust',
  bizRefId: 'adjustment-1',
  phase: 'increase',
  amount: 5,
  currency: 'USDT',
  status: 'Posted',
  isDemo: false,
  creationTime: '2026-11-13T00:00:00Z',
};

function renderRowActions(row: FinBizDocDto) {
  render(actionCell(mocks.table.mock.lastCall?.[0], row));
}

describe('凭证来源查询', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.access.mockReturnValue({ canViewFinanceVouchers: true });
    mocks.location.mockReturnValue({ search: '' });
    mocks.list.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.mockMode.mockReturnValue(false);
  });

  it('正式记录用凭证主键打开关联凭证', () => {
    render(<BizDocsPage />);
    renderRowActions(sourceRow);

    expect(screen.getByRole('link', { name: '查看会计凭证' })).toHaveAttribute(
      'href',
      '/finance/vouchers?voucherId=voucher-1',
    );
  });

  it('仅有凭证来源查询权限时隐藏凭证页入口', () => {
    mocks.access.mockReturnValue({ canViewFinanceBizDocs: true });
    render(<BizDocsPage />);
    renderRowActions(sourceRow);

    expect(screen.queryByRole('link', { name: '查看会计凭证' })).toBeNull();
    expect(screen.queryByRole('link', { name: '会计凭证' })).toBeNull();
  });

  it('将凭证详情带来的单号预填到查询，单号和业务类型分别传给服务', async () => {
    mocks.location.mockReturnValue({ search: '?bizRefId=payment%2F1' });
    render(<BizDocsPage />);

    const [props] = mocks.table.mock.calls[0];
    await fetchGridBlock(props, {
      filterModel: {
        bizType: { filterType: 'text', type: 'equals', filter: 'payment' },
      },
    });
    const [input, , scope] = mocks.list.mock.lastCall ?? [];
    expect(input.filters).toContainEqual({
      field: 'bizType',
      join: 'and',
      conditions: [{ op: 'eq', value: 'payment' }],
    });
    expect(scope + JSON.stringify(input)).toMatch(/payment(%2F|\/)1/);
  });
});
