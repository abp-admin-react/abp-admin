import * as SignalR from '@microsoft/signalr';
import type { VaultCheckLatestDto } from './collectionAdmin';
import { getAccessToken, onSessionChanged } from './oidc';

/**
 * 收付款模块 SignalR 客户端（管理页面共用）。
 *
 * 事件：
 * - ChainScanProgress  代币通道扫链进度（扫链 worker 轮末推送）
 * - ChainDepositChanged 进款单新增/变更
 * - ChainSweepChanged  归集任务状态变更
 * - ChainVaultCheck    金库核对留痕
 * - ChainHotWalletChanged 热钱包监控流水新增/异常
 *
 * 各页面共用当前会话的连接。首个订阅启动，最后一个退订停连并清空进度。
 * 有订阅时，启动失败或自动重连耗尽会继续退避重试；换账号或租户后重新取令牌建连。
 */

export interface ChainScanProgressPayload {
  symbol: string;
  lastScannedBlock?: number;
  latestChainHead?: number;
  blocksBehind?: number;
  lastScanAt?: string;
  isEnabled: boolean;
}

export interface ChainDepositChangedPayload {
  depositOrderId: string;
  txHash: string;
  symbol: string;
  userId: string;
  status: string;
  amount?: string;
  blockNumber: number;
  creationTime: string;
}

export interface ChainSweepChangedPayload {
  sweepTaskId: string;
  symbol: string;
  userId: string;
  status: string;
  amount: string;
  statusChangedAt: string;
}

export interface ChainVaultCheckPayload extends VaultCheckLatestDto {
  checkId: string;
}

export interface ChainHotWalletChangedPayload {
  txnId: string;
  symbol: string;
  status: string;
  direction: string;
  txHash: string;
  amount: string;
  to: string;
  blockNumber: number;
}

export type ChainConnState = 'connecting' | 'connected' | 'disconnected';

type ProgressListener = (item: ChainScanProgressPayload) => void;
type DepositListener = (item: ChainDepositChangedPayload) => void;
type SweepListener = (item: ChainSweepChangedPayload) => void;
type VaultListener = (item: ChainVaultCheckPayload) => void;
type HotWalletListener = (item: ChainHotWalletChangedPayload) => void;
type ConnListener = (state: ChainConnState) => void;

let connection: SignalR.HubConnection | null = null;
const progressListeners = new Set<ProgressListener>();
const depositListeners = new Set<DepositListener>();
const sweepListeners = new Set<SweepListener>();
const vaultListeners = new Set<VaultListener>();
const hotWalletListeners = new Set<HotWalletListener>();
const connListeners = new Set<ConnListener>();

let connState: ChainConnState = 'disconnected';
let startRetry = 0;
let retryTimer: ReturnType<typeof setTimeout> | null = null;
let subscriptions = 0;
let offSession: (() => void) | undefined;

/** 当前会话中每个代币最近一次进度，可供后打开的页面回放。 */
const lastProgress = new Map<string, ChainScanProgressPayload>();

const setConn = (s: ChainConnState) => {
  if (connState === s) return;
  connState = s;
  connListeners.forEach((fn) => {
    fn(s);
  });
};

const scheduleRetry = () => {
  if (retryTimer != null || !subscriptions) return;
  const delay = Math.min(1000 * 2 ** startRetry, 10_000);
  startRetry += 1;
  retryTimer = setTimeout(() => {
    retryTimer = null;
    startConn();
  }, delay);
};

// 令牌统一走 oidc.ts getAccessToken()（键带 abpadmin: 前缀、随 authority 变化），
// 不再手拼 localStorage 键（旧代码写死 localhost:8000，换端口/生产即匿名连接失败）。
const getToken = async (): Promise<string> => (await getAccessToken()) ?? '';

const ensureConn = () => {
  if (connection) return connection;

  const conn = new SignalR.HubConnectionBuilder()
    .configureLogging(SignalR.LogLevel.Warning)
    .withUrl('/signalr-hubs/chain-status', {
      accessTokenFactory: getToken,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) =>
        ctx.previousRetryCount < 12 ? 5000 : null,
    })
    .build();

  connection = conn;
  connection.keepAliveIntervalInMilliseconds = 15_000;
  connection.serverTimeoutInMilliseconds = 30_000;

  connection.onclose(() => {
    if (connection !== conn) return;
    setConn('disconnected');
    scheduleRetry();
  });
  connection.onreconnecting(() => {
    if (connection === conn) setConn('connecting');
  });
  connection.onreconnected(() => {
    if (connection === conn) setConn('connected');
  });

  connection.on('ChainScanProgress', (item: ChainScanProgressPayload) => {
    if (connection !== conn || !item?.symbol) return;
    lastProgress.set(item.symbol, item);
    progressListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainDepositChanged', (item: ChainDepositChangedPayload) => {
    if (connection !== conn || !item) return;
    depositListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainSweepChanged', (item: ChainSweepChangedPayload) => {
    if (connection !== conn || !item) return;
    sweepListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainVaultCheck', (item: ChainVaultCheckPayload) => {
    if (connection !== conn || !item) return;
    vaultListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on(
    'ChainHotWalletChanged',
    (item: ChainHotWalletChangedPayload) => {
      if (connection !== conn || !item) return;
      hotWalletListeners.forEach((fn) => {
        fn(item);
      });
    },
  );

  return connection;
};

const startConn = () => {
  const conn = ensureConn();
  if (conn.state === SignalR.HubConnectionState.Disconnected) {
    setConn('connecting');
    conn
      .start()
      .then(() => {
        if (connection !== conn) return;
        startRetry = 0;
        setConn('connected');
      })
      .catch(() => {
        if (connection !== conn) return;
        setConn('disconnected');
        scheduleRetry();
      });
  } else if (conn.state === SignalR.HubConnectionState.Connected) {
    setConn('connected');
  }
  return conn;
};

const resetConnection = () => {
  if (retryTimer !== null) clearTimeout(retryTimer);
  retryTimer = null;
  startRetry = 0;
  const old = connection;
  // 先摘掉旧连接，让异步 stop 触发的断线回调和迟到事件无法重试或覆盖新会话。
  connection = null;
  lastProgress.clear();
  setConn('disconnected');
  void old?.stop().catch(() => {});
};

function subscribe<T>(
  listeners: Set<(value: T) => void>,
  fn: (value: T) => void,
) {
  const listener = (value: T) => fn(value);
  listeners.add(listener);
  if (++subscriptions === 1) {
    offSession = onSessionChanged(() => {
      resetConnection();
      if (subscriptions) startConn();
    });
    startConn();
  }
  return () => {
    if (!listeners.delete(listener) || --subscriptions > 0) return;
    offSession?.();
    offSession = undefined;
    resetConnection();
  };
}

export const getChainProgressReplay = (): ChainScanProgressPayload[] => [
  ...lastProgress.values(),
];

export const onChainConnState = (fn: ConnListener) => {
  fn(connState);
  return subscribe(connListeners, fn);
};

export const onChainScanProgress = (fn: ProgressListener) =>
  subscribe(progressListeners, fn);
export const onChainDepositChanged = (fn: DepositListener) =>
  subscribe(depositListeners, fn);
export const onChainSweepChanged = (fn: SweepListener) =>
  subscribe(sweepListeners, fn);
export const onChainVaultCheck = (fn: VaultListener) =>
  subscribe(vaultListeners, fn);
export const onChainHotWalletChanged = (fn: HotWalletListener) =>
  subscribe(hotWalletListeners, fn);
