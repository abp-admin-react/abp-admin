import { act, renderHook } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { useChainRecordRefresh } from './use-chain-record-refresh';

it('coalesces a burst and runs one final refresh for events received during a slow request', async () => {
  vi.useFakeTimers();
  let changed!: () => void;
  const off = vi.fn();
  const subscribe = (listener: () => void) => { changed = listener; return off; };
  const first = Promise.withResolvers<void>();
  const reload = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue(undefined);
  const view = renderHook(() => useChainRecordRefresh(reload, subscribe));
  try {
    await act(async () => {
      for (let i = 0; i < 30; i++) changed();
      await vi.advanceTimersByTimeAsync(200);
    });
    expect(reload).toHaveBeenCalledTimes(1);
    await act(async () => { changed(); changed(); await vi.advanceTimersByTimeAsync(200); });
    expect(reload).toHaveBeenCalledTimes(1);
    await act(async () => { first.resolve(); await first.promise; });
    expect(reload.mock.calls).toEqual([[true], [true]]);
    changed();
    view.unmount();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(reload).toHaveBeenCalledTimes(2);
    expect(off).toHaveBeenCalledOnce();
  } finally {
    view.unmount();
    vi.useRealTimers();
  }
});
