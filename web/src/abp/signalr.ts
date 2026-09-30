import {
  type HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  type IRetryPolicy,
  LogLevel,
  type RetryContext,
} from '@microsoft/signalr';
import { getAccessToken, getUserManager } from './oidc';

/**
 * T3.2 SignalR 单例连接管理。
 * 只交付连接层：自动重连、token 续期、租户切换重连、登出断开、可用性探测。
 *
 * 处理器注册分两层（属主规则）：
 * - 静态 handlers（startRealTime 的可选参数）：应用级注册，唯一属主是 RealTimeConnection
 *   挂载的铃铛处理器；传参语义是「合并」而非「替换」，任何调用都不得清空既有注册——
 *   restartRealTime({}) 一类调用曾把静态注册整表清掉，推送静默丢失直到整页刷新。
 * - 动态订阅（onRealTimeMessage/offRealTimeMessage）：页面级注册，挂载时订阅、卸载时
 *   退订；连接建立/重建时统一重挂（SignalR 同名多处理器，与静态注册互不覆盖）。
 *
 * 降级三层：
 * 1. 传输层（SignalR 自带）：不用 skipNegotiation，WebSockets → SSE → LongPolling 自动降级；
 * 2. 连接层（本模块）：首连失败或 onclose 时 setAvailable(false)，
 *    上层用 isRealTimeAvailabilityChange 切轮询；
 * 3. 服务端开关：SignalR:Enabled=false 时前端不启动连接（见 app.tsx 的 RealTimeConnection）。
 */

const HUB_URL = '/signalr-hubs/notification';

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;
let handlers: Record<string, (...args: any[]) => void> = {};

/**
 * 页面级动态处理器（页面挂载时注册）：注册时连接可能尚未建立
 * （startRealTime 在 app.tsx 全局挂载），故先存表，连接建立/重建时统一挂载。
 * SignalR 的 on 支持同名多处理器，与 startRealTime 的静态 handlers 互不覆盖。
 */
const dynamicHandlers = new Map<string, Set<(...args: any[]) => void>>();

/** 订阅实时消息（页面挂载时调用；连接已建立则立即生效，否则在下次连接生效）。返回退订函数。 */
export function onRealTimeMessage(
  name: string,
  handler: (...args: any[]) => void,
): () => void {
  let set = dynamicHandlers.get(name);
  if (!set) {
    set = new Set();
    dynamicHandlers.set(name, set);
  }
  set.add(handler);
  connection?.on(name, handler);
  return () => offRealTimeMessage(name, handler);
}

export function offRealTimeMessage(
  name: string,
  handler: (...args: any[]) => void,
): void {
  const set = dynamicHandlers.get(name);
  if (!set) return;
  set.delete(handler);
  // 最后一个订阅者退订时连键一起清掉：事件名残留会让每次连接重建都空转遍历死集合
  if (set.size === 0) {
    dynamicHandlers.delete(name);
  }
  connection?.off(name, handler);
}

/** 是否被有意断开（从未启动 / stopRealTime）。有意断开时 onclose 不再安排定时重建。 */
let intentionalClose = true;

/**
 * 时间预算式无限重连（微软官方 IRetryPolicy 示例即用 RetryContext.elapsedMilliseconds
 * 做预算调节）：断流初期 0/1/2/5s 快节奏——实时页面没有轮询兜底，"断网恢复 5 秒内必回"；
 * 持续失败超过 2 分钟（服务端长故障/重启窗口）退到 30s 长尾，把 negotiate 流量降到 1/6，
 * 不至于多标签页长时间打死协商端点；永不返回 null（管理端不放弃重连，onclose 的
 * 60 秒兜底重建仍在）。后台标签页的定时器本就被浏览器节流（rAF 停、setTimeout 拖延，
 * 仅 WebSocket 豁免），长尾段与节流天然叠加。
 */
const FAST_RECONNECT_DELAYS_MS = [0, 1000, 2000, 5000];
const FAST_RECONNECT_WINDOW_MS = 2 * 60 * 1000;
const SLOW_RECONNECT_DELAY_MS = 30_000;

class InfiniteQuickRetryPolicy implements IRetryPolicy {
  nextRetryDelayInMilliseconds(context: RetryContext): number | null {
    if (context.elapsedMilliseconds >= FAST_RECONNECT_WINDOW_MS) {
      return SLOW_RECONNECT_DELAY_MS;
    }
    const index = Math.min(
      context.previousRetryCount,
      FAST_RECONNECT_DELAYS_MS.length - 1,
    );
    return FAST_RECONNECT_DELAYS_MS[index];
  }
}

/** 兜底重建间隔（仅不可自动重试的 onclose 终态会走到——无限重连策略下极少触发） */
const RETRY_AFTER_CLOSE_MS = 60_000;
let retryTimer: ReturnType<typeof setTimeout> | null = null;

/** SignalR 是否可用。false 时上层应回落到轮询。 */
let available = true;
const availabilityListeners = new Set<(ok: boolean) => void>();

function setAvailable(ok: boolean) {
  if (available === ok) return;
  available = ok;
  availabilityListeners.forEach((fn) => {
    fn(ok);
  });
}

export function isRealTimeAvailable() {
  return available;
}

/**
 * 是否已建立连接对象（生命周期判据）：stopRealTime 后为 false，首次 start 前也为 false。
 * 与 isRealTimeAvailable()（可用性提示：乐观默认 true，供页面在推送/轮询间选择）是两个
 * 概念——「已有连接才重建」一类的门闸必须用本判据：用可用性判据会把从未连接的访客
 * （初值 true）也放进重建分支，得到一次注定失败的建连外加 60 秒重建循环。
 */
export function hasRealTimeConnection() {
  return connection != null;
}

export function onRealTimeAvailabilityChange(fn: (ok: boolean) => void) {
  availabilityListeners.add(fn);
  return () => availabilityListeners.delete(fn);
}

/**
 * oidc.ts 的 getAccessToken() 在 token 过期时返回 undefined（automaticSilentRenew 未开启），
 * 所以这里必须自己触发一次静默续期，否则重连会一直拿不到 token。
 * accessTokenFactory 在每次（重）连接时都会被调用，是 token 续期的天然挂钩点。
 */
async function resolveAccessToken(): Promise<string> {
  let token = await getAccessToken();
  if (token) return token;

  try {
    const user = await getUserManager().signinSilent();
    token = user?.access_token;
  } catch {
    // 静默续期失败说明会话真的没了，交给上层的 401 处理逻辑跳登录
  }

  return token ?? '';
}

function build(): HubConnection {
  // 不用 skipNegotiation：它锁死 WebSockets 且跳过协商，失去传输降级能力
  return new HubConnectionBuilder()
    .withUrl(HUB_URL, {
      accessTokenFactory: resolveAccessToken,
    })
    .withAutomaticReconnect(new InfiniteQuickRetryPolicy())
    .configureLogging(LogLevel.Warning)
    .build();
}

function scheduleRetryAfterClose() {
  if (retryTimer || intentionalClose) return;
  // 无限重连策略下 onclose 基本只剩不可自动重试的终态（服务端下发 Close 等）。
  // 60 秒后整体重建连接兜底，不需要用户手动刷新页面
  retryTimer = setTimeout(() => {
    retryTimer = null;
    if (!intentionalClose) {
      // 不传 handlers：重建保持模块级现有注册（静态 + 动态两层都在 startRealTime 内重挂）
      void startRealTime();
    }
  }, RETRY_AFTER_CLOSE_MS);
}

function clearRetryTimer() {
  if (retryTimer) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
}

export async function startRealTime(
  nextHandlers?: Record<string, (...args: any[]) => void>,
): Promise<void> {
  // 注册语义：不传 = 保持现有注册（兜底重建/租户切换重连的用法）；传入 = 合并进模块级
  // 注册表（同名后者胜），绝不清空既有注册（属主规则见模块头注释）。
  // 同名替换在活连接上必须先 off 旧 handler 再 on 新的——SignalR 的 on 是追加语义，
  // 不 off 会让新旧两个 handler 同时收到推送（重复失效查询/重复渲染）。
  const replaced = nextHandlers
    ? Object.fromEntries(
        Object.entries(handlers).filter(([name]) => name in nextHandlers),
      )
    : {};
  if (nextHandlers) {
    handlers = { ...handlers, ...nextHandlers };
  }
  const attachNext = (conn: HubConnection) => {
    Object.entries(nextHandlers ?? {}).forEach(([name, handler]) => {
      const previous = replaced[name];
      if (previous) conn.off(name, previous);
      conn.on(name, handler);
    });
  };
  // Connected/Reconnecting 都算"活着"：无限重连策略下 Reconnecting 态会自愈恢复——
  // 此时重建会留下两条并行连接同时收推送（重复流量/资源），只应等它自己恢复。
  // 活连接下本轮合并进来的新 handlers 直接挂到当前连接即生效。
  if (
    connection?.state === HubConnectionState.Connected ||
    connection?.state === HubConnectionState.Reconnecting
  ) {
    if (nextHandlers && connection) attachNext(connection);
    return;
  }
  // React StrictMode 开发环境会双调用 effect，starting 守卫保证只建一条连接。
  // 建连窗口（Connecting 态）里传入的新 handlers 也要立即挂上：build 时的挂载用的是
  // 当时快照，不补挂会漏到下一次重建（最长 60 秒）——on() 在 start() 完成前后都合法。
  if (starting) {
    if (nextHandlers && connection) attachNext(connection);
    return starting;
  }

  intentionalClose = false;
  const nextConnection = build();
  connection = nextConnection;

  Object.entries(handlers).forEach(([name, handler]) => {
    nextConnection.on(name, handler);
  });

  dynamicHandlers.forEach((set, name) => {
    set.forEach((handler) => {
      nextConnection.on(name, handler);
    });
  });

  nextConnection.onreconnecting(() => setAvailable(false));
  nextConnection.onreconnected(() => setAvailable(true));
  nextConnection.onclose(() => {
    setAvailable(false);
    scheduleRetryAfterClose();
  });

  const thisStart: Promise<void> = nextConnection
    .start()
    .then(() => setAvailable(true))
    .catch((err) => {
      // 首次连接就失败：反向代理没开 WebSocket、网络策略拦截、或服务端未启用。
      // start() 失败时 onclose 不会触发（连接从未建立）——若不在此安排重建，
      // 初始失败即永久离线（实时页面没有轮询兜底，只能整页刷新）。60 秒后整体重建，
      // 恢复后由各页面的可用性边沿钩子自动补拉数据。
      console.warn('[signalr] connect failed, schedule rebuild', err);
      setAvailable(false);
      scheduleRetryAfterClose();
    })
    .finally(() => {
      // 只有仍是本轮 start 占着槽位才清空守卫：stopRealTime → startRealTime 与在途
      // start 交错时（stop 先清空 starting、新 start 占位，随后旧轮收尾），
      // 旧轮 finally 若无条件清空会误清新轮的守卫——连接窗口内再次 start 会建出
      // 第二条并行连接（重复推送直到整页刷新）
      if (starting === thisStart) {
        starting = null;
      }
    });

  starting = thisStart;

  return thisStart;
}

export async function stopRealTime(): Promise<void> {
  intentionalClose = true;
  clearRetryTimer();

  const current = connection;
  connection = null;
  starting = null;
  setAvailable(false);

  if (current) {
    // 连接对象整体丢弃，不需要逐个解绑事件。
    await current.stop().catch(() => undefined);
  }
}

/** 租户切换后调用：断开重连，让新 token 的租户上下文生效。不传 handlers 时保持现有注册。 */
export async function restartRealTime(
  nextHandlers?: Record<string, (...args: any[]) => void>,
): Promise<void> {
  await stopRealTime();
  await startRealTime(nextHandlers);
}
