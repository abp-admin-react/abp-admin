import { afterEach, expect, it, vi } from 'vitest';
import {
  getChainProgressReplay,
  onChainConnState,
  onChainDepositChanged,
  onChainScanProgress,
} from './chainSignalR';

const fake = vi.hoisted(() => {
  class Hub {
    state = 'Disconnected';
    events = new Map<string, (value: unknown) => void>();
    start = vi.fn(() => Promise.resolve());
    stop = vi.fn(() => Promise.resolve());
    closed = () => {};
    onclose(fn: () => void) { this.closed = fn; }
    onreconnected() {}
    onreconnecting() {}
    on(name: string, fn: (value: unknown) => void) { this.events.set(name, fn); }
  }
  return {
    Hub, hubs: [] as Hub[], session: vi.fn<(listener: () => void) => () => void>(),
  };
});

vi.mock('./oidc', () => ({ getAccessToken: vi.fn(), onSessionChanged: fake.session }));
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    configureLogging() { return this; }
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      const hub = new fake.Hub();
      fake.hubs.push(hub);
      return hub;
    }
  },
}));

afterEach(() => {
  fake.hubs.length = 0;
  fake.session.mockReset();
  vi.useRealTimers();
});

it('shares one connection and cancels replay, pending retries and late callbacks on final unsubscribe', async () => {
  vi.useFakeTimers();
  const offSession = vi.fn();
  fake.session.mockReturnValue(offSession);
  expect(fake.hubs).toHaveLength(0);
  const stopProgress = onChainScanProgress(vi.fn());
  const stopState = onChainConnState(vi.fn());
  const [hub] = fake.hubs;
  expect(fake.hubs).toHaveLength(1);
  expect(hub.start).toHaveBeenCalledTimes(1);
  hub.events.get('ChainScanProgress')?.({ symbol: 'USDT' });
  expect(getChainProgressReplay()).toEqual([{ symbol: 'USDT' }]);
  stopProgress();
  expect(hub.stop).not.toHaveBeenCalled();
  hub.closed();
  stopState();
  stopState();
  expect(hub.stop).toHaveBeenCalledTimes(1);
  expect(offSession).toHaveBeenCalledOnce();
  hub.closed();
  hub.events.get('ChainScanProgress')?.({ symbol: 'OLD' });
  await vi.advanceTimersByTimeAsync(20_000);
  expect(hub.start).toHaveBeenCalledTimes(1);
  expect(getChainProgressReplay()).toEqual([]);
});

it('rotates the connection and discards old-session messages even if the page stays mounted', async () => {
  let changeSession!: () => void;
  fake.session.mockImplementation((fn) => { changeSession = fn; return () => {}; });
  const onDeposit = vi.fn();
  const unsubscribe = onChainDepositChanged(onDeposit);
  try {
    const first = fake.hubs[0];
    first.events.get('ChainScanProgress')?.({ symbol: 'USDT' });
    changeSession();
    expect(first.stop).toHaveBeenCalledOnce();
    expect(getChainProgressReplay()).toEqual([]);
    first.events.get('ChainDepositChanged')?.({ depositOrderId: 'old' });
    expect(onDeposit).not.toHaveBeenCalled();
    fake.hubs[1].events.get('ChainDepositChanged')?.({ depositOrderId: 'new' });
    expect(onDeposit).toHaveBeenCalledWith({ depositOrderId: 'new' });
    await Promise.resolve();
  } finally {
    unsubscribe();
  }
});
