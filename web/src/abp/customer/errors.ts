/**
 * 客户端错误纯 TS 类型、ABP/OAuth 安全响应校验与归一化（方案 7.1/7.2）。
 *
 * 无 React/Umi/Antd/axios 依赖，可在 Node.js 或浏览器使用。
 * 安全错误自身的 message 只来自批准服务端响应或本地安全提示。
 *
 * @packageDocumentation
 */
import catalogJson from './public-error-catalog.json';

/**
 * 客户端错误分类（方案 7.2）。
 * 前 8 种来自后端 PublicKind；后 4 种由前端本地判定（网络、超时、取消、结果未知）。
 */
export type CustomerErrorKind =
  | 'business'
  | 'validation'
  | 'authentication'
  | 'forbidden'
  | 'notFound'
  | 'rateLimited'
  | 'conflict'
  | 'server'
  | 'network'
  | 'timeout'
  | 'cancelled'
  | 'outcomeUnknown';

/**
 * 客户端错误对象（方案 7.2）。Error 子类型，可直接 throw。
 */
export type CustomerError = Error & {
  /** 公开稳定码（可能为 null，前端走 kind 分支） */
  code?: string;
  /** 分类（客户端用于流程分支和展示策略） */
  kind: CustomerErrorKind;
  /** HTTP 状态码（可能为 null，如网络错误） */
  httpStatus?: number;
  /** 字段级校验错误（表单接管后不再全局提示） */
  fieldErrors: Record<string, string[]>;
  /** 关联号（可 null，用于客服查询日志） */
  correlationId?: string;
  /** 等待秒数（429/503 时可能提供） */
  retryAfterSeconds?: number;
};

/**
 * 错误展示责任（方案 7.2）。
 * - global: 全局提示（toast/notification）
 * - page: 页面级提示（如 404 页面）
 * - form: 表单字段提示（fieldErrors 接管后降级）
 * - none: skipErrorHandler 模式（仅 reject，不展示）
 */
export type CustomerErrorDisplay = 'global' | 'page' | 'form' | 'none';

/**
 * ABP 标准错误信封（方案 3.1）。
 * 前端归一化器从 HTTP 响应解析此结构。
 */
export interface AbpRemoteServiceErrorResponse {
  error: {
    code?: string | null;
    message?: string | null;
    details?: string | null;
    data?: Record<string, unknown> | null;
    validationErrors?: Array<{
      message?: string | null;
      members?: string[] | null;
    }> | null;
  };
}

/**
 * OAuth 错误响应（方案 3.2）。
 * /connect 端点使用此格式（+ WWW-Authenticate 头）。
 */
export interface OAuthErrorResponse {
  error?: string | null;
  error_description?: string | null;
}

/**
 * 公开错误快照条目（方案 7.4）。
 * 由后端 P3 快照测试生成，前端加载用于校验响应合法性。
 */
export interface PublicErrorCatalogEntry {
  /** 公开稳定码 */
  code: string;
  /** 分类 */
  kind: CustomerErrorKind;
  /** 允许的公开数据字段（lowerCamelCase） */
  dataFields: string[];
  /** 允许的 HTTP 状态码组合 */
  httpStatuses: number[];
}

/**
 * 公开错误快照（方案 7.4）。
 */
export interface PublicErrorCatalogSnapshot {
  /** 契约 schema 版本（前端需识别，不与服务端响应协商） */
  schemaVersion: number;
  /** 端点组合快照 */
  entries: PublicErrorCatalogEntry[];
}

/**
 * 契约 schema 版本常量。
 */
export const PUBLIC_ERROR_CONTRACT_SCHEMA_VERSION = 1;

/** 前端公开机器契约快照（仅码/分类/字段/状态，无内部映射与文案）。 */
export const publicErrorCatalog: PublicErrorCatalogSnapshot =
  catalogJson as PublicErrorCatalogSnapshot;

/**
 * 客户端错误限制常量（前端镜像后端 ClientErrorHandlingOptions）。
 */
export const CustomerErrorLimits = {
  /** 公开消息最大长度 */
  maxPublicMessageLength: 1000,
  /** 校验错误最大数量 */
  maxValidationErrors: 20,
  /** 公开参数最大长度 */
  maxPublicParameterLength: 256,
} as const;

/**
 * 客户端失败结果（方案 3.3 ClientFailureDto）。
 * 用于 HTTP 200 的任务/批量失败结果。
 */
export interface ClientFailureResult {
  /** 公开稳定码 */
  code: string;
  /** 安全文案 */
  message: string;
  /** 关联号（可能为 null） */
  correlationId: string | null;
  /** 公开字段结果（仅包含批准字段） */
  data: Record<string, unknown>;
}

/** 前端本地安全兜底文案（离线/超时/取消/HTTP 状态兜底）。 */
export type CustomerLocalMessages = {
  authentication: string;
  forbidden: string;
  validation: string;
  notFound: string;
  rateLimited: string;
  conflict: string;
  business: string;
  server: string;
  network: string;
  timeout: string;
  cancelled: string;
  outcomeUnknown: string;
};

export type CreateCustomerErrorInput = {
  message: string;
  kind: CustomerErrorKind;
  code?: string;
  httpStatus?: number;
  fieldErrors?: Record<string, string[]>;
  correlationId?: string;
  retryAfterSeconds?: number;
};

export type ParsedAbpError = {
  code?: string;
  message?: string;
  data: Record<string, unknown>;
  validationErrors: Array<{ message?: string; members: string[] }>;
};

export type ParsedOAuthError = {
  error?: string;
  errorDescription?: string;
};

export type NormalizeCustomerErrorInput = {
  status?: number;
  body?: unknown;
  headers?: Record<string, string> | Headers;
  catalog: PublicErrorCatalogSnapshot;
  localMessages: CustomerLocalMessages;
  method?: string;
  aborted?: boolean;
  timeout?: boolean;
  network?: boolean;
  redirectFailed?: boolean;
  /** HTTP 200 任务/批量失败项：按 ClientFailureDto 校验，不走 HTTP 401 语义。 */
  asResultItem?: boolean;
  now?: number;
};

const CUSTOMER_ERROR_NAME = 'CustomerError';
const MAX_RETRY_AFTER_COUNTDOWN_SECONDS = 86400;

const ABP_ERROR_KEYS = new Set([
  'code',
  'message',
  'details',
  'data',
  'validationErrors',
]);

const OAUTH_BODY_KEYS = new Set(['error', 'error_description']);

const INTERNAL_KEYS = new Set([
  'stack',
  'stackTrace',
  'stackTraceString',
  'exception',
  'exceptionType',
  'innerException',
  'inner',
  'cause',
  'raw',
  'config',
  'response',
  'token',
  'accessToken',
  'refreshToken',
  'password',
  'connectionString',
  'sql',
  'filePath',
  'lineNumber',
  'entityType',
  'entityId',
  'traceIdentifier',
  'targetSite',
  'source',
  'className',
]);

const AUTHENTICATION_CODE = 'Client:AuthenticationRequired';

const PUBLIC_KINDS = new Set<CustomerErrorKind>([
  'business',
  'validation',
  'authentication',
  'forbidden',
  'notFound',
  'rateLimited',
  'conflict',
  'server',
]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function hasInternalKeys(value: Record<string, unknown>): boolean {
  for (const key of Object.keys(value)) {
    if (INTERNAL_KEYS.has(key)) {
      return true;
    }
  }
  return false;
}

function isHtmlPayload(body: unknown): boolean {
  if (typeof body !== 'string') {
    return false;
  }
  const head = body.trim().slice(0, 512).toLowerCase();
  return (
    head.startsWith('<!doctype') ||
    head.startsWith('<html') ||
    head.startsWith('<head') ||
    head.startsWith('<body') ||
    head.includes('<html') ||
    head.includes('<body')
  );
}

function isSafePublicMessage(message: string): boolean {
  if (!message || message.length > CustomerErrorLimits.maxPublicMessageLength) {
    return false;
  }
  if (/<[a-z!?/]/i.test(message)) {
    return false;
  }
  if (message.includes('\u0000')) {
    return false;
  }
  return true;
}

function catalogEntries(
  catalog: PublicErrorCatalogSnapshot,
): PublicErrorCatalogEntry[] {
  if (catalog.schemaVersion !== PUBLIC_ERROR_CONTRACT_SCHEMA_VERSION) {
    return [];
  }
  return Array.isArray(catalog.entries) ? catalog.entries : [];
}

function findCatalogEntry(
  catalog: PublicErrorCatalogSnapshot,
  code: string | undefined,
): PublicErrorCatalogEntry | undefined {
  if (!code) {
    return undefined;
  }
  return catalogEntries(catalog).find((entry) => entry.code === code);
}

function allowedDataFields(
  catalog: PublicErrorCatalogSnapshot,
  code: string | undefined,
): Set<string> {
  const entry = findCatalogEntry(catalog, code);
  if (entry) {
    return new Set(entry.dataFields);
  }
  return new Set(['correlationId']);
}

function isNonNegativeInt(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

function parseNonNegativeIntToken(raw: string): number | undefined {
  const trimmed = raw.trim();
  if (!/^\d+$/.test(trimmed)) {
    return undefined;
  }
  const parsed = Number(trimmed);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    return undefined;
  }
  return parsed;
}

function readHeader(
  headers: Record<string, string> | Headers | undefined,
  name: string,
): string | undefined {
  if (!headers) {
    return undefined;
  }
  if (typeof (headers as Headers).get === 'function') {
    const value = (headers as Headers).get(name);
    return value == null || value === '' ? undefined : value;
  }
  const needle = name.toLowerCase();
  for (const [key, value] of Object.entries(
    headers as Record<string, string>,
  )) {
    if (key.toLowerCase() === needle && value !== '') {
      return value;
    }
  }
  return undefined;
}

function parseRetryAfterHeader(raw: string, now: number): number | undefined {
  const asInt = parseNonNegativeIntToken(raw);
  if (asInt !== undefined) {
    return asInt;
  }
  // HTTP-date 必含字母（IMF-fixdate 等）；拒绝 "-3" 被 Date.parse 收成公元年份。
  if (!/[A-Za-z]/.test(raw)) {
    return undefined;
  }
  const parsedDate = Date.parse(raw);
  if (Number.isNaN(parsedDate)) {
    return undefined;
  }
  const deltaSeconds = Math.ceil((parsedDate - now) / 1000);
  return deltaSeconds < 0 ? 0 : deltaSeconds;
}

function countdownSeconds(value: number | undefined): number | undefined {
  if (value === undefined || value < 0) {
    return undefined;
  }
  if (value > MAX_RETRY_AFTER_COUNTDOWN_SECONDS) {
    return undefined;
  }
  return value;
}

function resolveRetryAfterSeconds(input: {
  headerValue?: string;
  bodyValue?: unknown;
  now: number;
}): number | undefined {
  const fromHeader =
    input.headerValue === undefined
      ? undefined
      : parseRetryAfterHeader(input.headerValue, input.now);
  if (fromHeader !== undefined) {
    return countdownSeconds(fromHeader);
  }
  if (isNonNegativeInt(input.bodyValue)) {
    return countdownSeconds(input.bodyValue);
  }
  if (typeof input.bodyValue === 'string') {
    const parsed = parseNonNegativeIntToken(input.bodyValue);
    return countdownSeconds(parsed);
  }
  return undefined;
}

function parseMaybeJson(body: unknown): unknown {
  if (typeof body !== 'string') {
    return body;
  }
  const trimmed = body.trim();
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) {
    return body;
  }
  try {
    return JSON.parse(trimmed);
  } catch {
    return body;
  }
}

function parseDataObject(
  data: unknown,
  allowed: Set<string>,
): Record<string, unknown> | null {
  if (data == null) {
    return {};
  }
  if (!isRecord(data) || hasInternalKeys(data)) {
    return null;
  }
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(data)) {
    if (!allowed.has(key)) {
      return null;
    }
    if (key === 'correlationId') {
      if (value == null) {
        continue;
      }
      if (
        typeof value !== 'string' ||
        value.length > CustomerErrorLimits.maxPublicParameterLength
      ) {
        return null;
      }
      result[key] = value;
      continue;
    }
    if (key === 'retryAfterSeconds') {
      if (value == null) {
        continue;
      }
      if (isNonNegativeInt(value)) {
        result[key] = value;
        continue;
      }
      if (typeof value === 'string') {
        const parsed = parseNonNegativeIntToken(value);
        if (parsed !== undefined) {
          result[key] = parsed;
        }
      }
      continue;
    }
    result[key] = value;
  }
  return result;
}

function parseValidationErrors(
  value: unknown,
): Array<{ message?: string; members: string[] }> | null {
  if (value == null) {
    return [];
  }
  if (!Array.isArray(value)) {
    return null;
  }
  const items: Array<{ message?: string; members: string[] }> = [];
  for (const entry of value.slice(0, CustomerErrorLimits.maxValidationErrors)) {
    if (!isRecord(entry) || hasInternalKeys(entry)) {
      return null;
    }
    const membersRaw = entry.members;
    if (membersRaw != null && !Array.isArray(membersRaw)) {
      return null;
    }
    const members = Array.isArray(membersRaw)
      ? membersRaw.filter(
          (member): member is string =>
            typeof member === 'string' &&
            member.length > 0 &&
            member.length <= CustomerErrorLimits.maxPublicParameterLength,
        )
      : [];
    const message =
      typeof entry.message === 'string' && isSafePublicMessage(entry.message)
        ? entry.message
        : undefined;
    items.push({ message, members });
  }
  return items;
}

function toFieldErrors(
  validationErrors: Array<{ message?: string; members: string[] }>,
): Record<string, string[]> {
  const fieldErrors: Record<string, string[]> = {};
  for (const item of validationErrors) {
    if (!item.message || item.members.length === 0) {
      continue;
    }
    for (const member of item.members) {
      if (!fieldErrors[member]) {
        fieldErrors[member] = [];
      }
      if (!fieldErrors[member].includes(item.message)) {
        fieldErrors[member].push(item.message);
      }
    }
  }
  return fieldErrors;
}

function localMessage(
  messages: CustomerLocalMessages,
  kind: CustomerErrorKind,
): string {
  return messages[kind];
}

function kindFromHttpStatus(
  status: number,
  fieldErrors: Record<string, string[]>,
): CustomerErrorKind {
  if (status === 401) {
    return 'authentication';
  }
  if (status === 403) {
    return 'forbidden';
  }
  if (status === 404) {
    return 'notFound';
  }
  if (status === 409) {
    return 'conflict';
  }
  if (status === 429) {
    return 'rateLimited';
  }
  if (status === 400) {
    return Object.keys(fieldErrors).length > 0 ? 'validation' : 'business';
  }
  if (status >= 500) {
    return 'server';
  }
  if (status >= 400) {
    return 'business';
  }
  return 'server';
}

function kindFromOAuthError(
  error: string | undefined,
  status?: number,
): CustomerErrorKind | undefined {
  if (!error) {
    return undefined;
  }
  if (error === 'access_denied') {
    // OAuth 把未登录与已登录无权限都写成 access_denied；HTTP 403 是授权失败。
    return status === 403 ? 'forbidden' : 'authentication';
  }
  if (
    error === 'invalid_token' ||
    error === 'invalid_grant' ||
    error === 'unauthorized_client' ||
    error === 'login_required' ||
    error === 'session_revoked'
  ) {
    return 'authentication';
  }
  if (error === 'slow_down') {
    return 'rateLimited';
  }
  if (error === 'server_error' || error === 'temporarily_unavailable') {
    return 'server';
  }
  return undefined;
}

function isAllowedCombination(
  entry: PublicErrorCatalogEntry,
  status: number | undefined,
): boolean {
  if (status === undefined) {
    return false;
  }
  if (!entry.httpStatuses.includes(status)) {
    return false;
  }
  if (!PUBLIC_KINDS.has(entry.kind)) {
    return false;
  }
  if (status === 401) {
    return (
      entry.kind === 'authentication' && entry.code === AUTHENTICATION_CODE
    );
  }
  if (status === 403) {
    return entry.kind === 'business' || entry.kind === 'forbidden';
  }
  if (status === 429) {
    return entry.kind === 'rateLimited';
  }
  if (entry.kind === 'authentication') {
    return status === 401;
  }
  if (entry.kind === 'forbidden') {
    return status === 403;
  }
  if (entry.kind === 'rateLimited') {
    return status === 429;
  }
  if (entry.kind === 'validation') {
    return status === 400;
  }
  if (entry.kind === 'notFound') {
    return status === 404;
  }
  if (entry.kind === 'conflict') {
    return status === 409;
  }
  if (entry.kind === 'server') {
    return status >= 500 && status <= 599;
  }
  return status >= 400 && status <= 599;
}

function stripForbiddenProps(error: CustomerError): CustomerError {
  const leakKeys = [
    'response',
    'config',
    'cause',
    'raw',
    'token',
    'accessToken',
    'refreshToken',
    'details',
  ] as const;
  const mutable = error as CustomerError & Record<string, unknown>;
  for (const key of leakKeys) {
    if (key in mutable) {
      delete mutable[key];
    }
  }
  return error;
}

/** 写方法：网络/超时/重定向失败一律结果未知，且不得自动重试。 */
export function isWriteHttpMethod(method: string | undefined): boolean {
  const normalized = (method ?? 'GET').toUpperCase();
  return (
    normalized === 'POST' ||
    normalized === 'PUT' ||
    normalized === 'PATCH' ||
    normalized === 'DELETE'
  );
}

export function createCustomerError(
  input: CreateCustomerErrorInput,
): CustomerError {
  const error = new Error(input.message) as CustomerError;
  error.name = CUSTOMER_ERROR_NAME;
  error.kind = input.kind;
  error.fieldErrors = input.fieldErrors ?? {};
  if (input.code) {
    error.code = input.code;
  }
  if (input.httpStatus !== undefined) {
    error.httpStatus = input.httpStatus;
  }
  if (input.correlationId) {
    error.correlationId = input.correlationId;
  }
  if (input.retryAfterSeconds !== undefined) {
    const countdown = countdownSeconds(input.retryAfterSeconds);
    if (countdown !== undefined) {
      error.retryAfterSeconds = countdown;
    }
  }
  return stripForbiddenProps(error);
}

export function isCustomerError(value: unknown): value is CustomerError {
  if (!(value instanceof Error) || value.name !== CUSTOMER_ERROR_NAME) {
    return false;
  }
  const candidate = value as CustomerError;
  return typeof candidate.kind === 'string' && isRecord(candidate.fieldErrors);
}

export function parseAbpErrorResponse(
  body: unknown,
  catalog: PublicErrorCatalogSnapshot,
): ParsedAbpError | null {
  if (isHtmlPayload(body)) {
    return null;
  }
  const parsed = parseMaybeJson(body);
  if (!isRecord(parsed) || hasInternalKeys(parsed)) {
    return null;
  }
  const errorNode = parsed.error;
  if (!isRecord(errorNode) || hasInternalKeys(errorNode)) {
    return null;
  }
  for (const key of Object.keys(errorNode)) {
    if (!ABP_ERROR_KEYS.has(key) && INTERNAL_KEYS.has(key)) {
      return null;
    }
  }
  const code =
    typeof errorNode.code === 'string' && errorNode.code
      ? errorNode.code
      : undefined;
  const data = parseDataObject(
    errorNode.data,
    allowedDataFields(catalog, code),
  );
  if (data == null) {
    return null;
  }
  const validationErrors = parseValidationErrors(errorNode.validationErrors);
  if (validationErrors == null) {
    return null;
  }
  const message =
    typeof errorNode.message === 'string' &&
    isSafePublicMessage(errorNode.message)
      ? errorNode.message
      : undefined;
  return {
    code,
    message,
    data,
    validationErrors,
  };
}

export function parseOAuthErrorResponse(
  body: unknown,
): ParsedOAuthError | null {
  if (isHtmlPayload(body)) {
    return null;
  }
  const parsed = parseMaybeJson(body);
  if (!isRecord(parsed) || hasInternalKeys(parsed)) {
    return null;
  }
  if (isRecord(parsed.error)) {
    return null;
  }
  for (const key of Object.keys(parsed)) {
    if (!OAUTH_BODY_KEYS.has(key) && INTERNAL_KEYS.has(key)) {
      return null;
    }
  }
  const error =
    typeof parsed.error === 'string' && parsed.error ? parsed.error : undefined;
  const errorDescription =
    typeof parsed.error_description === 'string'
      ? parsed.error_description
      : undefined;
  if (!error && !errorDescription) {
    return null;
  }
  return { error, errorDescription };
}

export function parseClientFailureResult(
  body: unknown,
  catalog: PublicErrorCatalogSnapshot,
): ClientFailureResult | null {
  if (isHtmlPayload(body)) {
    return null;
  }
  const parsed = parseMaybeJson(body);
  if (!isRecord(parsed) || hasInternalKeys(parsed) || isRecord(parsed.error)) {
    return null;
  }
  if (typeof parsed.code !== 'string' || !parsed.code) {
    return null;
  }
  if (typeof parsed.message !== 'string') {
    return null;
  }
  if (
    parsed.correlationId != null &&
    typeof parsed.correlationId !== 'string'
  ) {
    return null;
  }
  const data = parseDataObject(
    parsed.data ?? {},
    allowedDataFields(catalog, parsed.code),
  );
  if (data == null) {
    return null;
  }
  return {
    code: parsed.code,
    message: parsed.message,
    correlationId:
      typeof parsed.correlationId === 'string' ? parsed.correlationId : null,
    data,
  };
}

function transportFallbackKind(
  input: NormalizeCustomerErrorInput,
): CustomerErrorKind | undefined {
  if (input.aborted) {
    return 'cancelled';
  }
  const write = isWriteHttpMethod(input.method);
  if (input.timeout) {
    return write ? 'outcomeUnknown' : 'timeout';
  }
  if (input.redirectFailed) {
    return write ? 'outcomeUnknown' : 'network';
  }
  if (input.network) {
    return write ? 'outcomeUnknown' : 'network';
  }
  if (input.status === undefined || input.status < 100) {
    return write ? 'outcomeUnknown' : 'network';
  }
  return undefined;
}

export function normalizeCustomerError(
  input: NormalizeCustomerErrorInput,
): CustomerError {
  const now = input.now ?? Date.now();
  const fieldErrorsEmpty: Record<string, string[]> = {};
  const transportKind = transportFallbackKind(input);
  if (
    transportKind &&
    (input.aborted ||
      input.timeout ||
      input.network ||
      input.redirectFailed ||
      input.status === undefined ||
      input.status < 100)
  ) {
    return createCustomerError({
      message: localMessage(input.localMessages, transportKind),
      kind: transportKind,
      httpStatus:
        input.status !== undefined && input.status >= 100
          ? input.status
          : undefined,
      fieldErrors: fieldErrorsEmpty,
    });
  }

  const retryHeader = readHeader(input.headers, 'Retry-After');
  const wwwAuthenticate = readHeader(input.headers, 'WWW-Authenticate');

  if (input.asResultItem) {
    const item = parseClientFailureResult(input.body, input.catalog);
    const entry = item ? findCatalogEntry(input.catalog, item.code) : undefined;
    const accept =
      item &&
      entry &&
      isSafePublicMessage(item.message) &&
      (input.status === undefined ||
        input.status < 400 ||
        isAllowedCombination(entry, input.status) ||
        input.status < 300);
    const kind = entry?.kind ?? 'business';
    const retryAfterSeconds = resolveRetryAfterSeconds({
      headerValue: retryHeader,
      bodyValue: item?.data.retryAfterSeconds,
      now,
    });
    const correlationId =
      (typeof item?.data.correlationId === 'string' &&
        item.data.correlationId) ||
      item?.correlationId ||
      undefined;
    return createCustomerError({
      message:
        accept && item && isSafePublicMessage(item.message)
          ? item.message
          : localMessage(input.localMessages, kind),
      kind,
      code: accept ? item?.code : undefined,
      httpStatus: input.status,
      correlationId,
      retryAfterSeconds,
      fieldErrors: fieldErrorsEmpty,
    });
  }

  const abp = parseAbpErrorResponse(input.body, input.catalog);
  const oauth = abp ? null : parseOAuthErrorResponse(input.body);
  const fieldErrors = abp
    ? toFieldErrors(abp.validationErrors)
    : fieldErrorsEmpty;
  const status = input.status;

  const entry = abp?.code
    ? findCatalogEntry(input.catalog, abp.code)
    : undefined;
  const catalogMatches =
    !!abp &&
    !!entry &&
    status !== undefined &&
    isAllowedCombination(entry, status);

  let kind: CustomerErrorKind | undefined;
  let code: string | undefined;
  let acceptServerMessage = false;

  if (catalogMatches && entry) {
    kind = entry.kind;
    code = entry.code;
    acceptServerMessage = !!abp?.message;
  }

  if (status === 401) {
    const legal401 = !abp?.code || abp.code === AUTHENTICATION_CODE;
    if (!legal401 || (kind && kind !== 'authentication')) {
      kind = 'authentication';
      code = undefined;
      acceptServerMessage = false;
    } else {
      kind = 'authentication';
      if (!catalogMatches) {
        code = undefined;
        acceptServerMessage = false;
      }
    }
  } else if (
    status === 403 &&
    (kind === 'authentication' || abp?.code === AUTHENTICATION_CODE)
  ) {
    kind = 'forbidden';
    code = undefined;
    acceptServerMessage = false;
  } else if (!kind) {
    kind = kindFromOAuthError(oauth?.error, status);
    if (!kind && wwwAuthenticate && status === 401) {
      kind = 'authentication';
    }
    if (!kind && status !== undefined) {
      kind = kindFromHttpStatus(status, fieldErrors);
    }
    if (!kind) {
      kind = isWriteHttpMethod(input.method) ? 'outcomeUnknown' : 'network';
    }
    code = undefined;
    acceptServerMessage = false;
  }

  if (status === 403 && kind === 'authentication') {
    kind = 'forbidden';
    code = undefined;
    acceptServerMessage = false;
  }

  if (
    status !== undefined &&
    status !== 401 &&
    entry &&
    kind &&
    !isAllowedCombination(entry, status) &&
    catalogMatches
  ) {
    kind = kindFromHttpStatus(status, fieldErrors);
    code = undefined;
    acceptServerMessage = false;
  }

  const retryAfterSeconds = resolveRetryAfterSeconds({
    headerValue: retryHeader,
    bodyValue: abp?.data.retryAfterSeconds,
    now,
  });
  const correlationId =
    typeof abp?.data.correlationId === 'string'
      ? abp.data.correlationId
      : undefined;

  const message =
    acceptServerMessage && abp?.message
      ? abp.message
      : localMessage(input.localMessages, kind);

  return createCustomerError({
    message,
    kind,
    code,
    httpStatus: status,
    fieldErrors,
    correlationId,
    retryAfterSeconds,
  });
}
