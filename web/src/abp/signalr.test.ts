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
    holdStart: false,
  };

  class FakeConnection {
    state = 'Disconnected';
    // 真实 SignalR 的 on(name, cb) 是同名多处理器追加、off(name, cb) 只移那一个；
    // 测试替身必须如实模拟，否则「静态 + 动态同名共存」契约根本无法表达
    handlers = new Map<string, Set<Handler>>();
    oncloseCb: (() => void) | null = null;
    onreconnectingCb: (() => void) | null = null;
    onreconnectedCb: (() => void) | null = null;
    startCalls = 0;
    stopCalls = 0;
    /** holdStart 期间挂起的 start() 的放行句柄（建连窗口测试用） */
    releaseStart: (() => void) | null = null;

    async start() {
      this.startCalls += 1;
      // 让调用方有机会先设置 failNextStart（真实 start 也是异步 resolve 的）
      await Promise.resolve();
      if (flags.failNextStart) {
        flags.failNextStart = false;
        throw new Error('connect failed');
      }
      if (flags.holdStart) {
        await new Promise<void>((resolve) => {
          this.releaseStart = resolve;
        });
      }
      this.state = 'Connected';
    }

    async stop() {
      this.stopCalls += 1;
      this.state = 'Disconnected';
      this.oncloseCb?.();
    }

    on(name: string, cb: Handler) {
      let set = this.handlers.get(name);
      if (!set) {
        set = new Set();
        this.handlers.set(name, set);
      }
      set.add(cb);
    }

    off(name: string, cb: Handler) {
      this.handlers.get(name)?.delete(cb);
    }

    has(name: string, cb: Handler) {
      return this.handlers.get(name)?.has(cb) ?? false;
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
    reconnectPolicy: undefined as unknown,
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

    withAutomaticReconnect(policy: unknown) {
      // 生产传 IRetryPolicy 实例（InfiniteQuickRetryPolicy）——记录对象供契约断言
      h.reconnectPolicy = policy;
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
    h.flags.holdStart = false;
    h.url = undefined;
    h.accessTokenFactory = undefined;
    h.reconnectPolicy = undefined;
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
    // 重连策略是 IRetryPolicy 实例：时间预算式契约——断流初期 0/1s/2s/5s（"5 秒内必回"），
    // 持续失败超 2 分钟退 30s 长尾（negotiate 流量降 1/6），永不返回 null（返回 null = 放弃重连）
    const policy = h.reconnectPolicy as
      | {
          nextRetryDelayInMilliseconds(c: {
            previousRetryCount: number;
            elapsedMilliseconds: number;
          }): number | null;
        }
      | undefined;
    if (!policy) throw new Error('重连策略未被 withAutomaticReconnect 记录');
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 0,
        elapsedMilliseconds: 0,
      }),
    ).toBe(0);
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 1,
        elapsedMilliseconds: 1000,
      }),
    ).toBe(1000);
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 2,
        elapsedMilliseconds: 3000,
      }),
    ).toBe(2000);
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 3,
        elapsedMilliseconds: 5000,
      }),
    ).toBe(5000);
    // 序列耗尽后钳制在固定 5s：预算窗口内无限重连
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 99,
        elapsedMilliseconds: 60_000,
      }),
    ).toBe(5000);
    // 超过 2 分钟预算窗口：退到 30s 长尾（服务端长故障/重启窗口）
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 99,
        elapsedMilliseconds: 2 * 60 * 1000,
      }),
    ).toBe(30_000);
    expect(h.created).toHaveLength(1);
    expect(h.created[0].startCalls).toBe(1);
    expect(h.created[0].has('ReceiveNotification', handler)).toBe(true);
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

  it('首次连接失败不抛出，置为不可用并安排 60 秒重建', async () => {
    vi.useFakeTimers();
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
      '[signalr] connect failed, schedule rebuild',
      expect.any(Error),
    );

    // start() 失败时 onclose 不触发——初始失败必须自行安排重建，否则永久离线
    await vi.advanceTimersByTimeAsync(60_000);
    expect(h.created).toHaveLength(2);
    expect(h.created[1].startCalls).toBe(1);
    expect(isRealTimeAvailable()).toBe(true);
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

  it('stopRealTime 后不再重试（初始失败安排的重建定时器被取消）', async () => {
    vi.useFakeTimers();
    const { startRealTime, stopRealTime } = await importFresh();

    h.flags.failNextStart = true;
    await startRealTime({});
    await stopRealTime();

    // 初始失败安排了 60 秒重建，但用户已登出/切租户（intentionalClose）——不得再建连
    await vi.advanceTimersByTimeAsync(60_000);
    expect(h.created).toHaveLength(1);
  });

  it('onRealTimeMessage：连接前注册先存表、建连时统一挂载；已连接则立即生效', async () => {
    const { onRealTimeMessage, startRealTime } = await importFresh();

    // 连接尚未建立（startRealTime 由 app.tsx 全局挂载，页面可能先注册）：先存表
    const early = () => {};
    onRealTimeMessage('PredictionTick', early);

    await startRealTime({});
    expect(h.created[0].has('PredictionTick', early)).toBe(true);

    // 连接已建立：注册立即生效；退订函数解绑
    const late = () => {};
    const dispose = onRealTimeMessage('SpotPrice', late);
    expect(h.created[0].has('SpotPrice', late)).toBe(true);
    dispose();
    expect(h.created[0].has('SpotPrice', late)).toBe(false);
  });

  it('Reconnecting 态视为活着：再次 startRealTime 不建第二条连接', async () => {
    const { startRealTime } = await importFresh();

    await startRealTime({});
    h.created[0].state = 'Reconnecting';

    await startRealTime({});

    // 重建会留下两条并行连接同时收推送（重复流量/资源）——只应等它自愈恢复
    expect(h.created).toHaveLength(1);
    expect(h.created[0].startCalls).toBe(1);
  });

  it('活连接下传入新 handlers：合并且直接挂当前连接，不重建', async () => {
    const { startRealTime } = await importFresh();

    const first = () => {};
    await startRealTime({ ReceiveNotification: first });

    const second = () => {};
    await startRealTime({ ReceiveUnreadCount: second });

    expect(h.created).toHaveLength(1);
    expect(h.created[0].has('ReceiveNotification', first)).toBe(true);
    expect(h.created[0].has('ReceiveUnreadCount', second)).toBe(true);
  });

  it('restartRealTime() 不传 handlers：重建后既有注册保持（不得清空）', async () => {
    const { startRealTime, restartRealTime } = await importFresh();

    const handler = () => {};
    await startRealTime({ ReceiveNotification: handler });
    await restartRealTime();

    // 曾有调用方传 restartRealTime({}) 把铃铛的静态注册整表清掉——推送静默丢失直到整页刷新
    expect(h.created).toHaveLength(2);
    expect(h.created[1].has('ReceiveNotification', handler)).toBe(true);
  });

  it('动态订阅跨连接重建保持：新连接自动重挂，退订也作用到新连接', async () => {
    vi.useFakeTimers();
    const { onRealTimeMessage, offRealTimeMessage, startRealTime } =
      await importFresh();

    const handler = () => {};
    onRealTimeMessage('PredictionTick', handler);
    await startRealTime({});
    expect(h.created[0].has('PredictionTick', handler)).toBe(true);

    // onclose → 60 秒兜底重建：动态订阅必须在新建的连接上重挂
    h.created[0].state = 'Disconnected';
    h.created[0].oncloseCb?.();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(h.created).toHaveLength(2);
    expect(h.created[1].has('PredictionTick', handler)).toBe(true);

    offRealTimeMessage('PredictionTick', handler);
    expect(h.created[1].has('PredictionTick', handler)).toBe(false);
  });

  it('静态与动态同名共存：互不覆盖，退订动态不影响静态', async () => {
    const { startRealTime, onRealTimeMessage, offRealTimeMessage } =
      await importFresh();

    const staticHandler = () => {};
    await startRealTime({ ReceiveNotification: staticHandler });

    const dynamicHandler = () => {};
    onRealTimeMessage('ReceiveNotification', dynamicHandler);

    expect(h.created[0].has('ReceiveNotification', staticHandler)).toBe(true);
    expect(h.created[0].has('ReceiveNotification', dynamicHandler)).toBe(true);

    offRealTimeMessage('ReceiveNotification', dynamicHandler);
    expect(h.created[0].has('ReceiveNotification', staticHandler)).toBe(true);
    expect(h.created[0].has('ReceiveNotification', dynamicHandler)).toBe(false);
  });

  it('hasRealTimeConnection：连接对象生命周期判据（start 前 false、建立后 true、stop 后 false）', async () => {
    const { startRealTime, stopRealTime, hasRealTimeConnection } =
      await importFresh();

    expect(hasRealTimeConnection()).toBe(false);
    await startRealTime({});
    expect(hasRealTimeConnection()).toBe(true);
    await stopRealTime();
    expect(hasRealTimeConnection()).toBe(false);
  });

  it('建连窗口（Connecting 态）传入的新 handlers 立即挂到正在建立的连接上', async () => {
    const { startRealTime } = await importFresh();
    h.flags.holdStart = true;
    const first = () => {};
    const firstPromise = startRealTime({ ReceiveNotification: first });

    // 连接尚未 Connected、start 仍在途：第二条调用合并的新 handler 必须当场挂上，
    // 而不是漏到下一次重建（最长 60 秒收不到推送）
    const second = () => {};
    const secondPromise = startRealTime({ ReceiveUnreadCount: second });
    expect(h.created).toHaveLength(1);
    expect(h.created[0].has('ReceiveUnreadCount', second)).toBe(true);

    // start() 内部先过一个微任务才走到挂起点——等两拍确保 releaseStart 已就位再放行
    await Promise.resolve();
    await Promise.resolve();
    h.created[0].releaseStart?.();
    await Promise.all([firstPromise, secondPromise]);
    expect(h.created[0].state).toBe('Connected');
    expect(h.created[0].has('ReceiveNotification', first)).toBe(true);
  });

  it('同名静态 handler 替换：活连接上先摘旧再挂新（后者胜，不双收）', async () => {
    const { startRealTime } = await importFresh();
    const original = () => {};
    await startRealTime({ ReceiveNotification: original });

    const replacement = () => {};
    await startRealTime({ ReceiveNotification: replacement });

    // SignalR 的 on 是追加语义：不先 off 旧 handler，推送会同时命中两个
    expect(h.created).toHaveLength(1);
    expect(h.created[0].has('ReceiveNotification', original)).toBe(false);
    expect(h.created[0].has('ReceiveNotification', replacement)).toBe(true);
  });
});
