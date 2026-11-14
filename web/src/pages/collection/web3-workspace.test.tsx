import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { Web3Workspace } from './web3-workspace';

const router = vi.hoisted(() => ({ useLocation: vi.fn(), push: vi.fn() }));
vi.mock('@umijs/max', () => ({
  useLocation: router.useLocation,
  history: { push: router.push },
}));

const pathname = '/web3/operations';
const deposits = {
  key: 'deposits',
  label: '充值记录',
  render: () => <span>充值列表</span>,
};
const withdrawals = {
  key: 'withdrawals',
  label: '提现审核',
  render: () => <span>提现列表</span>,
};

beforeEach(() => vi.resetAllMocks());

it('深链接只挂载对应内容，点击页签更新地址并保留其余查询参数', () => {
  router.useLocation.mockReturnValue({
    pathname,
    search: '?view=withdrawals&from=notice',
  });
  const workspace = (
    <Web3Workspace title="Web3 运营数据" sections={[deposits, withdrawals]} />
  );
  const { rerender } = render(workspace);
  expect(screen.getByText('提现列表')).toBeVisible();
  expect(screen.queryByText('充值列表')).toBeNull();

  fireEvent.click(screen.getByRole('tab', { name: '充值记录' }));
  expect(router.push).toHaveBeenCalledWith({
    pathname,
    search: '?view=deposits&from=notice',
  });
  router.useLocation.mockReturnValue({
    pathname,
    search: '?view=deposits&from=notice',
  });
  rerender(
    <Web3Workspace title="Web3 运营数据" sections={[deposits, withdrawals]} />,
  );
  expect(screen.getByText('充值列表')).toBeVisible();
  expect(screen.queryByText('提现列表')).toBeNull();
});

it('无权查看深链接的栏目时，打开该角色可访问的首项', () => {
  router.useLocation.mockReturnValue({ pathname, search: '?view=deposits' });
  render(<Web3Workspace title="Web3 运营数据" sections={[withdrawals]} />);
  expect(screen.getByRole('tab', { name: '提现审核' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  expect(screen.queryByRole('tab', { name: '充值记录' })).toBeNull();
});
