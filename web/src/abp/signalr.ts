import {
  type HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { getAccessToken, getUserManager } from './oidc';

/**
 * T3.2 SignalR 单例连接管理。
 * 只交付连接层：自动重连、token 续期、租户切换重连、登出断开、可用性探测。
 * 铃铛 UI 与具体推送处理器在 T3.5 注册（startRealTime({}) 先挂空 handlers）。
 *
 * 降级三层：
 * 1. 传输层（SignalR 自带）：不用 skipNegotiation，WebSockets → SSE → LongPolling 自动降级；
 * 2. 连接层（本模块）：首连失败或 onclose 时 setAvailable(false)，
 *    上层（T3.5 铃铛）用 isRealTimeAvailable()/onRealTimeAvailabilityChange 切轮询；
 * 3. 服务端开关：SignalR:Enabled=false 时前端不启动连接（见 app.tsx 的 RealTimeConnection）。
 */

const HUB_URL = '/signalr-hubs/notification';

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;
let handlers: Record<string, (...args: any[]) => void> = {};

/** 是否被有意断开（从未启动 / stopRealTime）。有意断开时 onclose 不再安排定时重建。 */
let intentionalClose = true;

/** withAutomaticReconnect 重试序列（约 47 秒）耗尽后的兜底重建间隔 */
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
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();
}

function scheduleRetryAfterClose() {
  if (retryTimer || intentionalClose) return;
  // 自动重连序列耗尽后 onclose 触发。"断网几分钟再恢复"是常态，
  // 60 秒后自动重建，不需要用户手动刷新页面
  retryTimer = setTimeout(() => {
    retryTimer = null;
    if (!intentionalClose) {
      void startRealTime(handlers);
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
  nextHandlers: Record<string, (...args: any[]) => void>,
): Promise<void> {
  if (connection?.state === HubConnectionState.Connected) return;
  // React StrictMode 开发环境会双调用 effect，starting 守卫保证只建一条连接
  if (starting) return starting;

  intentionalClose = false;
  handlers = nextHandlers;
  const nextConnection = build();
  connection = nextConnection;

  Object.entries(handlers).forEach(([name, handler]) => {
    nextConnection.on(name, handler);
  });

  nextConnection.onreconnecting(() => setAvailable(false));
  nextConnection.onreconnected(() => setAvailable(true));
  nextConnection.onclose(() => {
    setAvailable(false);
    scheduleRetryAfterClose();
  });

  starting = nextConnection
    .start()
    .then(() => setAvailable(true))
    .catch((err) => {
      // 首次连接就失败：反向代理没开 WebSocket、网络策略拦截、或服务端未启用。
      // 不抛出，让上层走轮询。
      console.warn('[signalr] connect failed, falling back to polling', err);
      setAvailable(false);
    })
    .finally(() => {
      starting = null;
    });

  return starting;
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

/** 租户切换后调用：断开重连，让新 token 的租户上下文生效。 */
export async function restartRealTime(
  nextHandlers: Record<string, (...args: any[]) => void>,
): Promise<void> {
  await stopRealTime();
  await startRealTime(nextHandlers);
}
