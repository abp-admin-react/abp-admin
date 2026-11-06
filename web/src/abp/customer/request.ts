/**
 * 客户请求：transport → 解析/归一化 → 会话动作 → 展示责任 → 始终 reject CustomerError。
 * 不依赖 Umi/axios/antd/react。管理 skip 与客户 skip 语义分离。
 */
import type {
  CustomerError,
  CustomerErrorDisplay,
  CustomerLocalMessages,
  PublicErrorCatalogSnapshot,
} from './errors';
import {
  isCustomerError,
  normalizeCustomerError,
  publicErrorCatalog,
} from './errors';

export type CustomerAuthSnapshot = {
  sessionGeneration: number;
  tenantId?: string | null;
};

export type CustomerAuthAdapter = {
  getSnapshot(): CustomerAuthSnapshot;
  applyHeaders(
    headers: Record<string, string>,
    snapshot: CustomerAuthSnapshot,
  ): void;
  /** true 表示已恢复；不自动重放原请求。 */
  recoverFromUnauthorized(snapshot: CustomerAuthSnapshot): Promise<boolean>;
};

export type CustomerNotifyAdapter = {
  global: (error: CustomerError) => void;
  page?: (error: CustomerError) => void;
  form?: (error: CustomerError) => void;
};

export type CustomerTransportRequest = {
  url: string;
  method: string;
  headers: Record<string, string>;
  body?: unknown;
  signal?: AbortSignal;
  timeoutMs?: number;
};

export type CustomerTransportResult = {
  status?: number;
  body?: unknown;
  headers?: Record<string, string>;
  aborted?: boolean;
  timeout?: boolean;
  network?: boolean;
  redirectFailed?: boolean;
};

export type CustomerTransport = (
  request: CustomerTransportRequest,
) => Promise<CustomerTransportResult>;

export type CustomerRequestOptions = {
  method?: string;
  headers?: Record<string, string>;
  params?: Record<string, unknown>;
  data?: unknown;
  display?: CustomerErrorDisplay;
  /** 仅映射 display=none，不能绕过归一化，也不能把失败变成成功。 */
  skipErrorHandler?: boolean;
  signal?: AbortSignal;
  timeoutMs?: number;
};

export type CreateCustomerRequestOptions = {
  transport: CustomerTransport;
  auth: CustomerAuthAdapter;
  messages: CustomerLocalMessages;
  notify: CustomerNotifyAdapter;
  catalog?: PublicErrorCatalogSnapshot;
};

export type CustomerRequestFn = <T>(
  url: string,
  options?: CustomerRequestOptions,
) => Promise<T>;

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isAbpErrorEnvelope(body: unknown): boolean {
  return isRecord(body) && isRecord(body.error);
}

function isClientFailureShape(body: unknown): boolean {
  if (!isRecord(body) || isRecord(body.error) || Array.isArray(body.items)) {
    return false;
  }
  return typeof body.code === 'string' && typeof body.message === 'string';
}

function appendQuery(url: string, params?: Record<string, unknown>): string {
  if (!params) {
    return url;
  }
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value == null || value === '') {
      continue;
    }
    search.append(key, String(value));
  }
  const qs = search.toString();
  if (!qs) {
    return url;
  }
  return url.includes('?') ? `${url}&${qs}` : `${url}?${qs}`;
}

function resolveDisplay(
  error: CustomerError,
  requested: CustomerErrorDisplay,
  aborted: boolean,
): CustomerErrorDisplay {
  if (aborted || error.kind === 'cancelled') {
    return 'none';
  }
  if (requested === 'none' || requested === 'page') {
    return requested;
  }
  if (Object.keys(error.fieldErrors).length > 0) {
    return 'form';
  }
  return requested;
}

function notifyError(
  notify: CustomerNotifyAdapter,
  error: CustomerError,
  display: CustomerErrorDisplay,
): void {
  if (display === 'none') {
    return;
  }
  if (display === 'page') {
    (notify.page ?? notify.global)(error);
    return;
  }
  if (display === 'form') {
    (notify.form ?? notify.global)(error);
    return;
  }
  notify.global(error);
}

export function createCustomerRequest(
  options: CreateCustomerRequestOptions,
): CustomerRequestFn {
  const catalog = options.catalog ?? publicErrorCatalog;
  let inFlightRecovery: {
    sessionGeneration: number;
    promise: Promise<boolean>;
  } | null = null;

  const recoverUnauthorized = async (
    snapshot: CustomerAuthSnapshot,
  ): Promise<boolean> => {
    const current = options.auth.getSnapshot();
    if (current.sessionGeneration !== snapshot.sessionGeneration) {
      return false;
    }
    if (
      inFlightRecovery &&
      inFlightRecovery.sessionGeneration === snapshot.sessionGeneration
    ) {
      return inFlightRecovery.promise;
    }
    const promise = (async () => {
      const latest = options.auth.getSnapshot();
      if (latest.sessionGeneration !== snapshot.sessionGeneration) {
        return false;
      }
      return options.auth.recoverFromUnauthorized(snapshot);
    })();
    inFlightRecovery = {
      sessionGeneration: snapshot.sessionGeneration,
      promise,
    };
    try {
      return await promise;
    } finally {
      if (inFlightRecovery?.promise === promise) {
        inFlightRecovery = null;
      }
    }
  };

  return async function customerRequest<T>(
    url: string,
    requestOptions: CustomerRequestOptions = {},
  ): Promise<T> {
    const method = (requestOptions.method ?? 'GET').toUpperCase();
    const requestedDisplay: CustomerErrorDisplay =
      requestOptions.skipErrorHandler
        ? 'none'
        : (requestOptions.display ?? 'global');
    const snapshot = options.auth.getSnapshot();
    const headers: Record<string, string> = {
      ...(requestOptions.headers ?? {}),
    };
    options.auth.applyHeaders(headers, snapshot);

    let transportResult: CustomerTransportResult;
    try {
      transportResult = await options.transport({
        url: appendQuery(url, requestOptions.params),
        method,
        headers,
        body: requestOptions.data,
        signal: requestOptions.signal,
        timeoutMs: requestOptions.timeoutMs,
      });
    } catch (caught) {
      if (isCustomerError(caught)) {
        notifyError(options.notify, caught, requestedDisplay);
        return Promise.reject(caught);
      }
      const aborted =
        requestOptions.signal?.aborted === true ||
        (caught instanceof Error && caught.name === 'AbortError');
      transportResult = {
        aborted,
        timeout: false,
        network: !aborted,
        redirectFailed: false,
      };
    }

    const status = transportResult.status;
    const asResultItem =
      status !== undefined &&
      status >= 200 &&
      status < 300 &&
      isClientFailureShape(transportResult.body) &&
      !isAbpErrorEnvelope(transportResult.body);

    if (
      status !== undefined &&
      status >= 200 &&
      status < 300 &&
      !isAbpErrorEnvelope(transportResult.body) &&
      !asResultItem
    ) {
      return transportResult.body as T;
    }

    const error = normalizeCustomerError({
      status,
      body: transportResult.body,
      headers: transportResult.headers,
      catalog,
      localMessages: options.messages,
      method,
      aborted: transportResult.aborted,
      timeout: transportResult.timeout,
      network: transportResult.network,
      redirectFailed: transportResult.redirectFailed,
      asResultItem,
    });

    if (status === 401 && !asResultItem) {
      await recoverUnauthorized(snapshot);
    }

    const display = resolveDisplay(
      error,
      requestedDisplay,
      transportResult.aborted === true,
    );
    notifyError(options.notify, error, display);
    return Promise.reject(error);
  };
}
