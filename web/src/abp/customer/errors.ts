/**
 * 客户端错误纯 TS 类型与常量（方案 7.1/7.2）。P1 契约。
 *
 * 本文件仅包含类型定义和常量，不包含运行时归一化逻辑（P3 实现）。
 * 所有类型均无 React/Umi/Antd 依赖，可在 Node.js 或浏览器环境使用。
 *
 * @packageDocumentation
 */

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
