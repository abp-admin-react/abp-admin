import { act, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, expect, it, vi } from 'vitest';
import HotWalletWatchPage from './hot-wallet';

const mocks = vi.hoisted(() => ({
  wallet: vi.fn(),
  watch: vi.fn(),
  status: vi.fn(),
  changed: vi.fn<(listener: () => void) => () => void>(),
}));
vi.mock('@umijs/max', () => ({ useAccess: () => ({}), Link: () => null }));
vi.mock('ag-grid-react', () => ({ AgGridReact: () => null }));
vi.mock('@/components/remoteGrid', () => ({
  toGridColumns: () => [],
  useSearchableRemoteGrid: () => ({ gridProps: {}, refresh: vi.fn() }),
}));
vi.mock('@/abp/withdrawal', async (original) => ({
  ...(await original<typeof import('@/abp/withdrawal')>()),
  getHotWallet: mocks.wallet, getHotWalletWatchStatus: mocks.watch, getWithdrawStatus: mocks.status,
}));
vi.mock('@/abp/chainSignalR', () => ({
  onChainConnState: () => () => {},
  onChainHotWalletChanged: mocks.changed,
}));

afterEach(() => { vi.useRealTimers(); vi.resetAllMocks(); });

it('only shows unconfigured after an explicit response and ignores stale cards after a refresh', async () => {
  vi.useFakeTimers();
  let publish!: () => void;
  mocks.changed.mockImplementation((fn) => { publish = fn; return () => {}; });
  const old = Promise.withResolvers<unknown>();
  mocks.wallet.mockReturnValueOnce(old.promise)
    .mockRejectedValueOnce(new Error('unavailable')).mockResolvedValueOnce({ configured: false });
  mocks.watch.mockResolvedValue({ tokens: [], anomalyOpenCount: 0 });
  mocks.status.mockResolvedValue({ pendingCount: 0, processingCount: 0 });
  const view = render(<App><HotWalletWatchPage embedded /></App>);
  try {
    expect(screen.queryByText('提现钱包未配置')).toBeNull();
    const firstSignal = mocks.wallet.mock.calls[0][0] as AbortSignal;
    await act(async () => { publish(); await vi.advanceTimersByTimeAsync(3000); });
    expect(firstSignal.aborted).toBe(false);
    expect(mocks.wallet).toHaveBeenCalledTimes(1);
    await act(async () => { publish(); await vi.advanceTimersByTimeAsync(3000); });
    expect(mocks.wallet).toHaveBeenCalledTimes(1);
    await act(async () => { old.resolve({ configured: false }); await old.promise; });
    expect(mocks.wallet).toHaveBeenCalledTimes(2);
    expect(screen.getByText('提现钱包状态读取失败')).toBeTruthy();
    expect(screen.queryByText('提现钱包未配置')).toBeNull();
    await act(async () => { publish(); await vi.advanceTimersByTimeAsync(3000); });
    expect(screen.getByText('提现钱包未配置')).toBeTruthy();
  } finally {
    view.unmount();
  }
  const calls = mocks.wallet.mock.calls;
  expect((calls[calls.length - 1][0] as AbortSignal).aborted).toBe(true);
});
