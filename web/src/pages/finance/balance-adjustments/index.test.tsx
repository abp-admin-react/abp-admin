import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import BalanceAdjustmentsPage, { isValidAdjustmentAmount } from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  table: vi.fn(),
  create: vi.fn(),
  approve: vi.fn(),
  reject: vi.fn(),
  list: vi.fn(),
  getUser: vi.fn(),
  location: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useLocation: mocks.location,
  useModel: () => ({ initialState: { currentUser: { userid: 'reviewer' } } }),
  history: { push: vi.fn(), replace: vi.fn() },
}));
vi.mock('@/abp/identity', () => ({ getUser: mocks.getUser }));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/balanceAdjustments', () => ({
  createBalanceAdjustment: mocks.create,
  approveBalanceAdjustment: mocks.approve,
  rejectBalanceAdjustment: mocks.reject,
  getBalanceAdjustments: mocks.list,
}));

function page() {
  return (
    <AntdApp>
      <BalanceAdjustmentsPage />
    </AntdApp>
  );
}

vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));

describe('用户余额调账', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.access.mockReturnValue({
      canCreateBalanceAdjustments: true,
      canApproveBalanceAdjustments: true,
      canRejectBalanceAdjustments: true,
    });
    mocks.list.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.create.mockResolvedValue({});
    mocks.location.mockReturnValue({ pathname: '/finance/balance-adjustments', search: '' });
  });

  it.each([
    '0',
    '-0',
    '0.000',
    '1e2',
    'Infinity',
    '01',
    '1.0000000000000000001',
    '10000000000',
    '9999999999.000000000000000001',
  ])('拒绝非正常金额 %s', (value) => {
    expect(isValidAdjustmentAmount(value)).toBe(false);
  });

  it.each([
    '1',
    '-10.23',
    '0.000000000000000001',
    '9999999999',
    '-9999999998.123',
  ])('接受并保留十进制金额 %s', (value) => {
    expect(isValidAdjustmentAmount(value)).toBe(true);
  });

  it('重复失败提交沿用同一个请求号且保留负数精度', async () => {
    mocks.create
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({});
    render(page());
    fireEvent.click(screen.getByRole('button', { name: '新建调账申请' }));
    fireEvent.change(await screen.findByLabelText('用户 ID'), {
      target: { value: '11111111-1111-4111-8111-111111111111' },
    });
    fireEvent.change(screen.getByLabelText('调整金额'), {
      target: { value: '-0.000000000000000001' },
    });
    fireEvent.change(screen.getByLabelText('调账原因'), {
      target: { value: '更正余额' },
    });
    const form = screen.getByLabelText('调账原因').closest('form');
    expect(form).toBeTruthy();
    if (form) fireEvent.submit(form);
    await waitFor(() => expect(mocks.create).toHaveBeenCalledTimes(1));
    await screen.findByRole('button', { name: '重试原申请' });
    fireEvent.click(screen.getByRole('button', { name: '关闭' }));
    fireEvent.click(screen.getByRole('button', { name: '新建调账申请' }));
    const retainedAmount = await screen.findByDisplayValue(
      '-0.000000000000000001',
    );
    expect(retainedAmount).toBeDisabled();
    const retryForm = retainedAmount.closest('form');
    if (retryForm) fireEvent.submit(retryForm);
    await waitFor(() => expect(mocks.create).toHaveBeenCalledTimes(2));
    expect(mocks.create.mock.calls[0][0].requestId).toBe(
      mocks.create.mock.calls[1][0].requestId,
    );
    expect(mocks.create.mock.calls[1][0]).toMatchObject({
      amount: '-0.000000000000000001',
      currency: 3,
    });
  });

  it('缺少创建权限时隐藏申请入口', () => {
    mocks.access.mockReturnValue({});
    render(page());
    expect(screen.queryByRole('button', { name: '新建调账申请' })).toBeNull();
  });

  it('从余额页发起时锁定目标用户，并显示已核实的账号', async () => {
    const userId = '11111111-1111-4111-8111-111111111111';
    mocks.location.mockReturnValue({
      pathname: '/finance/balance-adjustments',
      search: `?createFor=${userId}`,
    });
    mocks.access.mockReturnValue({ canCreateBalanceAdjustments: true, canManageUsers: true });
    mocks.getUser.mockResolvedValue({ id: userId, userName: 'target-user', email: 'target@example.test' });
    render(page());
    expect(await screen.findByText('调整对象：target-user（target@example.test）')).toBeTruthy();
    const userField = screen.getByLabelText('用户 ID');
    expect(userField).toBeDisabled();
    expect(userField).toHaveValue(userId);
    fireEvent.change(screen.getByLabelText('调整金额'), { target: { value: '0.000000000000000001' } });
    const reason = screen.getByLabelText('调账原因');
    fireEvent.change(reason, { target: { value: '核对后的调整' } });
    const form = reason.closest('form');
    if (form) fireEvent.submit(form);
    await waitFor(() => expect(mocks.create).toHaveBeenCalledWith(
      expect.objectContaining({ userId, currency: 3, amount: '0.000000000000000001' }),
    ));
  });

  it('本人申请需要自批权限才显示审批操作', () => {
    render(page());
    const props = mocks.table.mock.calls.at(-1)?.[0];
    const renderActions = props.columns.find(
      (column: { valueType: string }) => column.valueType === 'option',
    ).render;
    const { unmount } = render(
      renderActions(null, { id: 'id', status: 0, applicantId: 'reviewer' }),
    );
    expect(screen.queryByRole('button', { name: '通过' })).toBeNull();
    expect(screen.queryByRole('button', { name: '拒绝' })).toBeNull();
    unmount();
  });
});
