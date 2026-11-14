import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { App } from 'antd';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import WithdrawalControls from './withdrawal-controls';
import WithdrawalsPage from './withdrawals';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  getWithdrawStatus: vi.fn(),
  getWithdrawals: vi.fn(),
  getHotWallet: vi.fn(),
  setWithdrawEnabled: vi.fn(),
  setWithdrawHalt: vi.fn(),
  backfillOpening: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/abp/withdrawal', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/withdrawal')>()),
  ...mocks,
}));
vi.mock('@/abp/finance', () => ({
  financeErrorText: (error: Error) => error.message,
}));

function show(content: ReactNode = <WithdrawalControls />) {
  render(<App>{content}</App>);
}

describe('提现启停与开账', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({ canAdminWithdrawals: true });
    mocks.getWithdrawStatus.mockResolvedValue({
      enabled: true,
      halt: false,
      feeRate: '0.03',
      pendingCount: 3,
      processingCount: 5,
      broadcastingCount: 2,
      broadcastFailedCount: 1,
    });
    mocks.getWithdrawals.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getHotWallet.mockResolvedValue({
      configured: true,
      address: 'test-wallet',
      tokenBalances: { USDT: '100' },
      bnb: '1',
      breakerOpen: false,
    });
    mocks.backfillOpening.mockResolvedValue({
      users: 2,
      posted: 1,
      skipped: 1,
    });
  });

  it.each([
    ['提现功能', '关闭提现功能？', 'setWithdrawEnabled', false],
    ['紧急停机', '紧急停止提现？', 'setWithdrawHalt', true],
  ] as const)(
    '确认后才更新%s并读取服务端结果',
    async (label, prompt, action, value) => {
      show();
      const toggle = await screen.findByRole('switch', { name: label });
      fireEvent.click(toggle);
      expect(await screen.findByText(prompt)).toBeTruthy();
      expect(mocks[action]).not.toHaveBeenCalled();
      fireEvent.click(screen.getByRole('button', { name: /取\s*消/ }));
      expect(mocks[action]).not.toHaveBeenCalled();

      mocks.getWithdrawStatus.mockResolvedValue({
        enabled: label !== '提现功能',
        halt: label === '紧急停机',
      });
      fireEvent.click(toggle);
      fireEvent.click(await screen.findByRole('button', { name: /^确\s*认$/ }));
      await waitFor(() =>
        expect(mocks[action]).toHaveBeenCalledExactlyOnceWith(value),
      );
      await waitFor(() =>
        expect(toggle).toHaveAttribute('aria-checked', String(value)),
      );
      expect(mocks.getWithdrawStatus).toHaveBeenCalledTimes(2);
    },
  );

  it('确认后执行存量余额开账并显示处理结果', async () => {
    show();
    await screen.findByRole('switch', { name: '提现功能' });
    fireEvent.click(screen.getByRole('button', { name: '存量余额开账' }));
    expect(mocks.backfillOpening).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: '确认开账' }));
    expect(
      await screen.findByText('开账完成：用户 2，过账 1，跳过 1'),
    ).toBeTruthy();
    expect(mocks.backfillOpening).toHaveBeenCalledTimes(1);
  });

  it('确认启停后锁住其他配置操作，直到读回状态', async () => {
    let finish: (() => void) | undefined;
    mocks.setWithdrawEnabled.mockReturnValue(
      new Promise<void>((resolve) => {
        finish = resolve;
      }),
    );
    show();
    const toggle = await screen.findByRole('switch', { name: '提现功能' });
    fireEvent.click(toggle);
    const confirm = await screen.findByRole('button', { name: /^确\s*认$/ });
    fireEvent.click(confirm);
    fireEvent.click(confirm);
    expect(mocks.setWithdrawEnabled).toHaveBeenCalledExactlyOnceWith(false);
    expect(screen.getByRole('switch', { name: '紧急停机' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '存量余额开账' })).toBeDisabled();

    mocks.getWithdrawStatus.mockResolvedValue({ enabled: false, halt: false });
    await act(async () => finish?.());
    expect(toggle).toBeEnabled();
    expect(toggle).not.toBeChecked();
  });

  it('提现状态读取失败时提供重试入口', async () => {
    mocks.getWithdrawStatus.mockRejectedValueOnce(new Error('offline'));
    show();
    expect(await screen.findByText('加载提现状态失败')).toBeTruthy();
    expect(screen.queryByRole('switch')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    expect(
      await screen.findByRole('switch', { name: '提现功能' }),
    ).toBeChecked();
  });

  it('通用设置专员不能读取或变更提现启停', () => {
    mocks.useAccess.mockReturnValue({ canManageSettings: true });
    show();
    expect(screen.queryByRole('switch')).toBeNull();
    expect(screen.queryByRole('button', { name: '存量余额开账' })).toBeNull();
    expect(mocks.getWithdrawStatus).not.toHaveBeenCalled();
  });

  it('提现运营页仅显示运行状态、余额和单据操作，并可直达规则', async () => {
    show(<WithdrawalsPage embedded />);
    expect(await screen.findByText('提现已开启')).toBeTruthy();
    expect(await screen.findByText('USDT 100')).toBeTruthy();
    expect(screen.getByText('提现单')).toBeTruthy();
    expect(
      screen.getByRole('link', { name: '提现规则与启停' }),
    ).toHaveAttribute('href', '/web3/config?view=withdraw');
    expect(screen.queryByRole('switch')).toBeNull();
    expect(screen.queryByRole('button', { name: '存量余额开账' })).toBeNull();
    expect(screen.queryByText('费率 0.03')).toBeNull();
    expect(screen.queryByText('提现审核与打款')).toBeNull();
  });

  it('提现钱包查询失败可重试，并保留提现单可查', async () => {
    mocks.getHotWallet.mockRejectedValueOnce(new Error('offline'));
    show(<WithdrawalsPage embedded />);
    expect(await screen.findByText('加载提现钱包余额失败')).toBeTruthy();
    expect(screen.getByText('提现单')).toBeTruthy();
    expect(screen.queryByText(/热钱包未配置/)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    expect(await screen.findByText('USDT 100')).toBeTruthy();
  });
});
