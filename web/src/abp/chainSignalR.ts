import * as SignalR from '@microsoft/signalr';

/**
 * 收付款模块 SignalR 客户端（四个管理页面共用）。
 *
 * 事件：
 * - ChainScanProgress  代币通道扫链进度（扫链 worker 轮末推送）
 * - ChainDepositChanged 进款单新增/变更
 * - ChainSweepChanged  归集任务状态变更
 * - ChainVaultCheck    金库核对留痕
 *
 * 可靠性：与 exchange 项目同款双轨重连（SignalR 自动 5s×12 + 指数退避 1s→10s 无限），
 * 连接状态暴露给页面状态条。
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

export interface ChainVaultCheckPayload {
  checkId: string;
  symbol: string;
  vaultBalance: string;
  userTotal: string;
  difference: string;
  checkedAt: string;
}

export type ChainConnState = 'connecting' | 'connected' | 'disconnected';

type ProgressListener = (item: ChainScanProgressPayload) => void;
type DepositListener = (item: ChainDepositChangedPayload) => void;
type SweepListener = (item: ChainSweepChangedPayload) => void;
type VaultListener = (item: ChainVaultCheckPayload) => void;
type ConnListener = (state: ChainConnState) => void;

let connection: SignalR.HubConnection | null = null;
const progressListeners = new Set<ProgressListener>();
const depositListeners = new Set<DepositListener>();
const sweepListeners = new Set<SweepListener>();
const vaultListeners = new Set<VaultListener>();
const connListeners = new Set<ConnListener>();

let connState: ChainConnState = 'disconnected';
let startRetry = 0;
let retryTimer: ReturnType<typeof setTimeout> | null = null;

/** 最近一轮进度快照（晚订阅页面回放） */
const lastProgress = new Map<string, ChainScanProgressPayload>();

const setConn = (s: ChainConnState) => {
  if (connState === s) return;
  connState = s;
  connListeners.forEach((fn) => {
    fn(s);
  });
};

const scheduleRetry = () => {
  if (retryTimer != null) return;
  const delay = Math.min(1000 * 2 ** startRetry, 10_000);
  startRetry += 1;
  retryTimer = setTimeout(() => {
    retryTimer = null;
    startConn();
  }, delay);
};

const getToken = (): string => {
  try {
    const raw = localStorage.getItem('oidc.user:http://localhost:8000:AbpAdmin_App');
    return raw ? JSON.parse(raw).access_token ?? '' : '';
  } catch {
    return '';
  }
};

const ensureConn = () => {
  if (connection) return connection;

  connection = new SignalR.HubConnectionBuilder()
    .configureLogging(SignalR.LogLevel.Warning)
    .withUrl('/signalr-hubs/chain-status', {
      accessTokenFactory: getToken,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => (ctx.previousRetryCount < 12 ? 5000 : null),
    })
    .build();

  connection.keepAliveIntervalInMilliseconds = 15_000;
  connection.serverTimeoutInMilliseconds = 30_000;

  connection.onclose(() => {
    setConn('disconnected');
    scheduleRetry();
  });
  connection.onreconnecting(() => setConn('connecting'));
  connection.onreconnected(() => setConn('connected'));

  connection.on('ChainScanProgress', (item: ChainScanProgressPayload) => {
    if (!item?.symbol) return;
    lastProgress.set(item.symbol, item);
    progressListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainDepositChanged', (item: ChainDepositChangedPayload) => {
    if (!item) return;
    depositListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainSweepChanged', (item: ChainSweepChangedPayload) => {
    if (!item) return;
    sweepListeners.forEach((fn) => {
      fn(item);
    });
  });
  connection.on('ChainVaultCheck', (item: ChainVaultCheckPayload) => {
    if (!item) return;
    vaultListeners.forEach((fn) => {
      fn(item);
    });
  });

  return connection;
};

const startConn = () => {
  const conn = ensureConn();
  if (conn.state === SignalR.HubConnectionState.Disconnected) {
    setConn('connecting');
    conn.start().then(() => {
      startRetry = 0;
      setConn('connected');
    }).catch(() => {
      setConn('disconnected');
      scheduleRetry();
    });
  } else if (conn.state === SignalR.HubConnectionState.Connected) {
    setConn('connected');
  }
  return conn;
};

export const startChainSignalR = () => startConn();

export const getChainProgressReplay = (): ChainScanProgressPayload[] =>
  [...lastProgress.values()];

export const onChainConnState = (fn: ConnListener) => {
  connListeners.add(fn);
  fn(connState);
  return () => connListeners.delete(fn);
};

export const onChainScanProgress = (fn: ProgressListener) => {
  progressListeners.add(fn);
  return () => progressListeners.delete(fn);
};

export const onChainDepositChanged = (fn: DepositListener) => {
  depositListeners.add(fn);
  return () => depositListeners.delete(fn);
};

export const onChainSweepChanged = (fn: SweepListener) => {
  sweepListeners.add(fn);
  return () => sweepListeners.delete(fn);
};

export const onChainVaultCheck = (fn: VaultListener) => {
  vaultListeners.add(fn);
  return () => vaultListeners.delete(fn);
};
