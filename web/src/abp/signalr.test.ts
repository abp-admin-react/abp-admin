import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * web/src/abp/signalr.ts 的连接管理测试。
 * @microsoft/signalr 与 ./oidc 全部 mock 掉：这里验证的是我们自己的
 * 单例/重连/降级/定时重建逻辑，不是 SignalR 库本身。
 * 模块内部状态是模块级单例，每个用例都要 vi.resetModules() + 动态导入拿到全新状态。
 */

const h = vi.hoisted(() => {
  type Handler = (...args: any[]) => void;

  const flags = {
    failNextStart: false,
  };

  class FakeConnection {
    state = 'Disconnected';
    handlers = new Map<string, Handler>();
    oncloseCb: (() => void) | null = null;
    onreconnectingCb: (() => void) | null = null;
    onreconnectedCb: (() => void) | null = null;
    startCalls = 0;
    stopCalls = 0;

    async start() {
      this.startCalls += 1;
      // 让调用方有机会先设置 failNextStart（真实 start 也是异步 resolve 的）
      await Promise.resolve();
      if (flags.failNextStart) {
        flags.failNextStart = false;
        throw new Error('connect failed');
      }
      this.state = 'Connected';
    }

    async stop() {
      this.stopCalls += 1;
      this.state = 'Disconnected';
      this.oncloseCb?.();
    }

    on(name: string, cb: Handler) {
      this.handlers.set(name, cb);
    }

    onreconnecting(cb: () => void) {
      this.onreconnectingCb = cb;
    }

    onreconnected(cb: () => void) {
      this.onreconnectedCb = cb;
    }

    onclose(cb: () => void) {
      this.oncloseCb = cb;
    }
  }

  return {
    FakeConnection,
    flags,
    created: [] as InstanceType<typeof FakeConnection>[],
    url: undefined as string | undefined,
    accessTokenFactory: undefined as (() => Promise<string>) | undefined,
    reconnectDelays: undefined as number[] | undefined,
    getAccessToken: vi.fn<() => Promise<string | undefined>>(),
    signinSilent: vi.fn(),
  };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Reconnecting: 'Reconnecting',
  },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl(
      url: string,
      options: { accessTokenFactory?: () => Promise<string> },
    ) {
      h.url = url;
      h.accessTokenFactory = options?.accessTokenFactory;
      return this;
    }

    withAutomaticReconnect(delays: number[]) {
      h.reconnectDelays = delays;
      return this;
    }

    configureLogging() {
      return this;
    }

    build() {
      const connection = new h.FakeConnection();
      h.created.push(connection);
      return connection;
    }
  },
}));

vi.mock('./oidc', () => ({
  getAccessToken: () => h.getAccessToken(),
  getUserManager: () => ({ signinSilent: () => h.signinSilent() }),
}));

async function importFresh() {
  return import('./signalr');
}

/** 调用被 withUrl 捕获的 accessTokenFactory；没被捕获说明 build() 根本没跑，直接失败 */
function invokeAccessTokenFactory(): Promise<string> {
  if (!h.accessTokenFactory) {
    throw new Error('accessTokenFactory 未被 withUrl 捕获');
  }
  return h.accessTokenFactory();
}

describe('signalr', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.restoreAllMocks();
    h.created.length = 0;
    h.flags.failNextStart = false;
    h.url = undefined;
    h.accessTokenFactory = undefined;
    h.reconnectDelays = undefined;
    h.getAccessToken.mockReset().mockResolvedValue('token-1');
    h.signinSilent.mockReset();
    vi.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('startRealTime 建立连接并置为可用，handlers 逐个注册', async () => {
    const { startRealTime, isRealTimeAvailable, onRealTimeAvailabilityChange } =
      await importFresh();
    const received: boolean[] = [];
    onRealTimeAvailabilityChange((ok) => received.push(ok));

    const handler = () => {};
    await startRealTime({ ReceiveNotification: handler });

    expect(h.url).toBe('/signalr-hubs/notification');
    expect(h.reconnectDelays).toEqual([0, 2000, 5000, 10000, 30000]);
    expect(h.created).toHaveLength(1);
    expect(h.created[0].startCalls).toBe(1);
    expect(h.created[0].handlers.get('ReceiveNotification')).toBe(handler);
    expect(isRealTimeAvailable()).toBe(true);
    // 可用性初值即 true，成功启动没有发生"变化"，监听者不应被触发
    expect(received).toEqual([]);
  });

  it('并发 startRealTime 只建一条连接（StrictMode 双调用守卫）', async () => {
    const { startRealTime } = await importFresh();

    await Promise.all([startRealTime({}), startRealTime({})]);

    expect(h.created).toHaveLength(1);
    expect(h.created[0].startCalls).toBe(1);
  });

  it('首次连接失败不抛出，置为不可用（上层降级轮询）', async () => {
    const { startRealTime, isRealTimeAvailable, onRealTimeAvailabilityChange } =
      await importFresh();
    const received: boolean[] = [];
    onRealTimeAvailabilityChange((ok) => received.push(ok));

    h.flags.failNextStart = true;
    await startRealTime({});

    expect(isRealTimeAvailable()).toBe(false);
    // 可用性 true → false 是一次变化，监听者必须被通知到
    expect(received).toEqual([false]);
    expect(console.warn).toHaveBeenCalledWith(
      '[signalr] connect failed, falling back to polling',
      expect.any(Error),
    );
  });

  it('stopRealTime 停止连接、置为不可用，且不安排定时重建', async () => {
    vi.useFakeTimers();
    const { startRealTime, stopRealTime, isRealTimeAvailable } =
      await importFresh();

    await startRealTime({});
    await stopRealTime();

    expect(h.created[0].stopCalls).toBe(1);
    expect(isRealTimeAvailable()).toBe(false);

    // stop() 触发 onclose，但这是有意断开，60 秒后不应出现新连接
    await vi.advanceTimersByTimeAsync(60_000);
    expect(h.created).toHaveLength(1);
  });

  it('自动重连序列耗尽（onclose）后置为不可用，60 秒后自动重建', async () => {
    vi.useFakeTimers();
    const { startRealTime, isRealTimeAvailable } = await importFresh();

    await startRealTime({});
    expect(h.created).toHaveLength(1);

    // 模拟 withAutomaticReconnect 重试序列耗尽：SignalR 先置 Disconnected 再触发 onclose
    h.created[0].state = 'Disconnected';
    h.created[0].oncloseCb?.();
    expect(isRealTimeAvailable()).toBe(false);

    await vi.advanceTimersByTimeAsync(60_000);

    expect(h.created).toHaveLength(2);
    expect(h.created[1].startCalls).toBe(1);
    expect(isRealTimeAvailable()).toBe(true);
  });

  it('onreconnecting / onreconnected 切换可用性并通知监听者', async () => {
    const { startRealTime, onRealTimeAvailabilityChange, isRealTimeAvailable } =
      await importFresh();
    const received: boolean[] = [];
    onRealTimeAvailabilityChange((ok) => received.push(ok));

    await startRealTime({});
    received.length = 0;

    h.created[0].onreconnectingCb?.();
    expect(isRealTimeAvailable()).toBe(false);
    h.created[0].onreconnectedCb?.();
    expect(isRealTimeAvailable()).toBe(true);
    expect(received).toEqual([false, true]);
  });

  it('restartRealTime 丢弃旧连接并建立新连接', async () => {
    const { startRealTime, restartRealTime } = await importFresh();

    await startRealTime({});
    await restartRealTime({});

    expect(h.created).toHaveLength(2);
    expect(h.created[0].stopCalls).toBe(1);
    expect(h.created[1].startCalls).toBe(1);
  });

  it('accessTokenFactory：token 有效时直接返回', async () => {
    const { startRealTime } = await importFresh();
    h.getAccessToken.mockResolvedValue('token-valid');

    await startRealTime({});

    await expect(invokeAccessTokenFactory()).resolves.toBe('token-valid');
    expect(h.signinSilent).not.toHaveBeenCalled();
  });

  it('accessTokenFactory：token 过期时走 signinSilent 静默续期', async () => {
    const { startRealTime } = await importFresh();
    h.getAccessToken.mockResolvedValue(undefined);
    h.signinSilent.mockResolvedValue({ access_token: 'token-renewed' });

    await startRealTime({});

    await expect(invokeAccessTokenFactory()).resolves.toBe('token-renewed');
    expect(h.signinSilent).toHaveBeenCalledTimes(1);
  });

  it('accessTokenFactory：静默续期也失败时返回空串（由服务端 401 拒绝）', async () => {
    const { startRealTime } = await importFresh();
    h.getAccessToken.mockResolvedValue(undefined);
    h.signinSilent.mockRejectedValue(new Error('no session'));

    await startRealTime({});

    await expect(invokeAccessTokenFactory()).resolves.toBe('');
  });
});
