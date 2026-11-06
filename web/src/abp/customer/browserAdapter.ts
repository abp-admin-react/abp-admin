/**
 * 浏览器 fetch 传输：相对 URL、同源已登记前缀、凭据与重定向锁定。
 * 不存储 token；语言/租户/CSRF 由注入 getter 提供。
 */
import type { CustomerTransport, CustomerTransportResult } from './request';

export const CUSTOMER_TRANSPORT_REJECTED = 'CustomerTransportRejectedError';

export const CUSTOMER_ALLOWED_PATH_PREFIXES = [
  '/api/client/',
  '/api/abp/application-configuration',
  '/api/abp/application-localization',
  '/api/account',
  '/connect/',
] as const;

export type CustomerFetch = (
  input: string,
  init?: RequestInit,
) => Promise<Response>;

export type BrowserTransportOptions = {
  origin?: string;
  getLanguage?: () => string | undefined | null;
  getTenantId?: () => string | undefined | null;
  getCsrfToken?: () => string | undefined | null;
  timeoutMs?: number;
  fetchImpl?: CustomerFetch;
};

function rejectUrl(): never {
  const error = new Error('Request URL is not allowed');
  error.name = CUSTOMER_TRANSPORT_REJECTED;
  throw error;
}

function isHttpOrigin(origin: string): URL {
  let parsed: URL;
  try {
    parsed = new URL(origin);
  } catch {
    rejectUrl();
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    rejectUrl();
  }
  if (parsed.username || parsed.password) {
    rejectUrl();
  }
  return parsed;
}

function pathHasAmbiguousEncoding(path: string): boolean {
  if (path.includes('\\')) {
    return true;
  }
  if (/(^|\/)\.\.?(\/|$)/.test(path)) {
    return true;
  }
  if (/%(?:2f|5c|2e)/i.test(path)) {
    return true;
  }
  let current = path;
  for (let i = 0; i < 3; i += 1) {
    let decoded: string;
    try {
      decoded = decodeURIComponent(current);
    } catch {
      return true;
    }
    if (decoded === current) {
      break;
    }
    if (decoded.includes('\\')) {
      return true;
    }
    if (decoded.split('/').length !== path.split('/').length) {
      return true;
    }
    current = decoded;
  }
  return false;
}

function isAbsoluteOrProtocolRelative(url: string): boolean {
  return url.startsWith('//') || /^[a-zA-Z][a-zA-Z\d+.-]*:/.test(url);
}

function isAllowedPath(pathname: string): boolean {
  return CUSTOMER_ALLOWED_PATH_PREFIXES.some((prefix) => {
    if (prefix.endsWith('/')) {
      return pathname === prefix.slice(0, -1) || pathname.startsWith(prefix);
    }
    return pathname === prefix || pathname.startsWith(`${prefix}/`);
  });
}

export function assertSafeCustomerUrl(url: string, origin: string): URL {
  if (typeof url !== 'string' || url.length === 0) {
    rejectUrl();
  }
  if (url.includes('\\')) {
    rejectUrl();
  }
  if (isAbsoluteOrProtocolRelative(url) || !url.startsWith('/')) {
    rejectUrl();
  }
  const pageOrigin = isHttpOrigin(origin);
  const pathPart = url.split(/[?#]/, 1)[0] ?? url;
  if (pathHasAmbiguousEncoding(pathPart)) {
    rejectUrl();
  }
  let parsed: URL;
  try {
    parsed = new URL(url, pageOrigin);
  } catch {
    rejectUrl();
  }
  if (parsed.origin !== pageOrigin.origin) {
    rejectUrl();
  }
  if (parsed.username || parsed.password) {
    rejectUrl();
  }
  if (parsed.pathname !== pathPart) {
    let decodedPath = pathPart;
    try {
      decodedPath = decodeURI(pathPart);
    } catch {
      rejectUrl();
    }
    if (decodedPath !== parsed.pathname) {
      rejectUrl();
    }
  }
  if (!isAllowedPath(parsed.pathname)) {
    rejectUrl();
  }
  return parsed;
}

function headerName(
  headers: Record<string, string>,
  name: string,
): string | undefined {
  const needle = name.toLowerCase();
  return Object.keys(headers).find((key) => key.toLowerCase() === needle);
}

function applyInjectedHeaders(
  headers: Record<string, string>,
  options: BrowserTransportOptions,
): void {
  const language = options.getLanguage?.();
  if (language && !headerName(headers, 'Accept-Language')) {
    headers['Accept-Language'] = language;
  }
  const tenantId = options.getTenantId?.();
  if (tenantId && !headerName(headers, '__tenant')) {
    headers.__tenant = tenantId;
  }
  const csrf = options.getCsrfToken?.();
  if (csrf && !headerName(headers, 'RequestVerificationToken')) {
    headers.RequestVerificationToken = csrf;
  }
}

function serializeBody(
  body: unknown,
  headers: Record<string, string>,
): BodyInit | undefined {
  if (body == null) {
    return undefined;
  }
  if (typeof body === 'string') {
    return body;
  }
  if (typeof FormData !== 'undefined' && body instanceof FormData) {
    return body;
  }
  if (
    typeof URLSearchParams !== 'undefined' &&
    body instanceof URLSearchParams
  ) {
    return body;
  }
  if (typeof Blob !== 'undefined' && body instanceof Blob) {
    return body;
  }
  if (!headerName(headers, 'Content-Type')) {
    headers['Content-Type'] = 'application/json';
  }
  return JSON.stringify(body);
}

function collectHeaders(headers: Headers): Record<string, string> {
  const result: Record<string, string> = {};
  headers.forEach((value, key) => {
    result[key] = value;
  });
  return result;
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof Error && error.name === 'AbortError') ||
    (typeof DOMException !== 'undefined' &&
      error instanceof DOMException &&
      error.name === 'AbortError')
  );
}

function emptyFailure(
  flags: Pick<
    CustomerTransportResult,
    'aborted' | 'timeout' | 'network' | 'redirectFailed'
  >,
): CustomerTransportResult {
  return {
    status: undefined,
    body: undefined,
    headers: {},
    aborted: flags.aborted === true,
    timeout: flags.timeout === true,
    network: flags.network === true,
    redirectFailed: flags.redirectFailed === true,
  };
}

export function createBrowserTransport(
  options: BrowserTransportOptions = {},
): CustomerTransport {
  const fetchImpl = options.fetchImpl ?? fetch;
  const defaultTimeoutMs = options.timeoutMs;

  return async (request) => {
    const origin =
      options.origin ??
      (typeof window !== 'undefined' ? window.location.origin : '');
    if (!origin) {
      rejectUrl();
    }
    const parsed = assertSafeCustomerUrl(request.url, origin);
    const headers: Record<string, string> = { ...request.headers };
    applyInjectedHeaders(headers, options);
    const timeoutMs =
      request.timeoutMs !== undefined ? request.timeoutMs : defaultTimeoutMs;
    const controller = new AbortController();
    let timedOut = false;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;
    const onCallerAbort = () => {
      controller.abort();
    };
    if (request.signal) {
      if (request.signal.aborted) {
        return emptyFailure({ aborted: true });
      }
      request.signal.addEventListener('abort', onCallerAbort, { once: true });
    }
    if (timeoutMs !== undefined && timeoutMs > 0) {
      timeoutId = setTimeout(() => {
        timedOut = true;
        controller.abort();
      }, timeoutMs);
    }
    try {
      const response = await fetchImpl(`${parsed.pathname}${parsed.search}`, {
        method: request.method,
        headers,
        body: serializeBody(request.body, headers),
        signal: controller.signal,
        credentials: 'same-origin',
        redirect: 'error',
      });
      const text = await response.text();
      let body: unknown = text === '' ? null : text;
      if (text) {
        try {
          body = JSON.parse(text);
        } catch {
          body = text;
        }
      }
      return {
        status: response.status,
        body,
        headers: collectHeaders(response.headers),
        aborted: false,
        timeout: false,
        network: false,
        redirectFailed: false,
      };
    } catch (error) {
      if (isAbortError(error)) {
        if (request.signal?.aborted && !timedOut) {
          return emptyFailure({ aborted: true });
        }
        if (timedOut) {
          return emptyFailure({ timeout: true });
        }
        return emptyFailure({ aborted: true });
      }
      return emptyFailure({ network: true });
    } finally {
      if (timeoutId !== undefined) {
        clearTimeout(timeoutId);
      }
      request.signal?.removeEventListener('abort', onCallerAbort);
    }
  };
}
