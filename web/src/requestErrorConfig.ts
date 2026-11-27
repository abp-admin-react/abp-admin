/**
 * Umi request 全局错误与鉴权头。
 *
 * 管理端默认错误处理。skipErrorHandler 会直接向调用方抛出原始错误。
 * - 401 → 静默跳到 /user/login?retry=1，登录页停住等用户点重试，避免立刻再跳授权。
 * - 403 → 只弹一条 message（key=http-403）；有 ABP 文案用文案，否则中文兜底；
 *   绝不透出 axios 英文 "Request failed with status code 403"。
 * - 404 → 全局静默；开发环境 console.warn `[request] 404 silenced...` 便于抓错路径；
 *   页面必须用 isNotFound 自行降级（见 AGENTS.md）。
 * - 429 → 限流文案。管理端 Data 键是 PascalCase RetryAfterSeconds，
 *   客户信封是 retryAfterSeconds；两键都读，有秒数才格式化等待时间。
 * - 离线 → message「网络不可用」（i18n）；与 OfflineBanner 一致语义。
 * - 带账号快照的角色请求同时核对令牌主体和租户。401 仍走登录跳转，其余失败由编辑器显示。
 * - 请求头：Bearer + __tenant（getAbpHeaders）+
 *   RequestVerificationToken（XSRF-TOKEN cookie）+ Accept-Language（toAbpCulture）。
 */
import type { RequestOptions } from '@@/plugin-request/request';
import type { RequestConfig } from '@umijs/max';
import { getIntl, getLocale, history } from '@umijs/max';
import { message, notification } from 'antd';
import { toAbpCulture } from './abp/culture';
import { getAccessToken, getAccessTokenSnapshot } from './abp/oidc';
import { getAbpHeaders } from './abp/tenant';
import { formatRetryAfter } from './utils/format';
import { LOGIN_RETRY_PATH } from './utils/redirect';

type AbpError = {
  error?: {
    code?: string;
    message?: string;
    details?: string;
    validationErrors?: { message?: string }[];
  };
};

// antd 6 未从包根导出 NotificationInstance 类型——用静态对象反推实例形态
type NotificationLike = typeof notification;
let notificationInstance: NotificationLike | null = null;

/** 运行时注入 AntdApp.useApp() 的 notification（见 app.tsx）——静态函数不吃 context 的官方出路。 */
export function setNotificationInstance(instance: NotificationLike | null) {
  notificationInstance = instance;
}

function notifyError(args: { title: string; description: string }) {
  // antd 6：notification.message 已更名 title
  (notificationInstance ?? notification).error({
    title: args.title,
    description: args.description,
  });
}

/** 404 判定：全局层对 404 静默（业务语义，见 errorHandler），页面层用它区分
 *  「未配置/不存在」与「读失败（403/500/网络）」——后者必须向上抛而不是被当成不存在。 */
export function isNotFound(error: unknown): boolean {
  return (
    (error as { response?: { status?: number } } | null)?.response?.status ===
    404
  );
}

/** Thrown before dispatch when a request's captured tenant or account changed. */
export class RequestSessionContextChangedError extends Error {
  constructor(readonly scope: 'tenant' | 'user') {
    super(
      scope === 'tenant'
        ? '租户已切换，请重新打开角色。'
        : '登录账号已更换或会话已失效，请重新打开角色。',
    );
    this.name = 'RequestSessionContextChangedError';
  }
}

function getAntiForgeryHeaders(): Record<string, string> {
  if (typeof document === 'undefined') {
    return {};
  }
  const token = document.cookie
    .split('; ')
    .find((row) => row.startsWith('XSRF-TOKEN='))
    ?.slice('XSRF-TOKEN='.length);
  if (!token) {
    return {};
  }
  return { RequestVerificationToken: decodeURIComponent(token) };
}

export const errorConfig: RequestConfig = {
  errorConfig: {
    errorThrower: (res) => {
      const payload = res as { success?: boolean } & AbpError;
      if (payload.success === false) {
        const error: any = new Error(
          payload.error?.message || 'Request failed',
        );
        error.name = 'BizError';
        error.info = payload.error;
        throw error;
      }
    },
    errorHandler: (error: any, opts: any) => {
      // 管理 skip 仍直接抛出 axios/ABP 原错误。客户请求库 skipErrorHandler 只映射 display=none，仍归一化为 CustomerError，两套不要混用。
      if (
        opts?.skipErrorHandler ||
        error instanceof RequestSessionContextChangedError
      )
        throw error;
      if (error.response?.status === 401) {
        history.replace(LOGIN_RETRY_PATH);
        return;
      }
      // 角色编辑器按服务端回执区分可以重试和需要先核对的状态，错误只在编辑器内展示。
      if (opts?.expectedUserId !== undefined) throw error;
      const abpError = error.response?.data?.error as AbpError['error'];
      const text =
        abpError?.validationErrors?.map((item) => item.message).join('; ') ||
        abpError?.details ||
        abpError?.message ||
        error.message;
      if (error.response?.status === 403) {
        // 403 单提示：有 ABP 业务文案（validationErrors/details/message）就只弹它一次；
        // 没有文案时统一给中文兜底，绝不透出 axios 英文原文（Request failed with status code 403）。
        // 固定 key 让同一时刻多个 403（并发请求/重复点击）合并成一条，不叠弹。
        // 页面若自己处理 403 提示，应在请求上传 skipErrorHandler，全局层不再弹。
        const forbiddenText =
          abpError?.validationErrors?.map((item) => item.message).join('; ') ||
          abpError?.details ||
          abpError?.message ||
          '没有权限执行此操作';
        message.error({ content: forbiddenText, key: 'http-403' });
        return;
      }
      if (error.response?.status === 429) {
        const data = (error.response.data?.error?.data ?? {}) as Record<
          string,
          any
        >;
        const seconds = Number(
          data.RetryAfterSeconds ?? data.retryAfterSeconds ?? 0,
        );
        const retryText =
          seconds > 0
            ? `操作过于频繁，请在 ${formatRetryAfter(seconds)} 后重试`
            : '该操作已被限制，请联系管理员';
        message.error(retryText);
        return;
      }
      if (error.response) {
        if (error.response?.status === 404) {
          // 资源缺失由调用页解释；开发时保留接口路径，便于检查路由是否匹配。
          if (process.env.NODE_ENV === 'development') {
            console.warn(
              '[request] 404 silenced (page layer should catch):',
              error.config?.url?.split(/[?#]/, 1)[0],
            );
          }
          return;
        }
        notifyError({
          title: `HTTP ${error.response.status}`,
          description: text,
        });
        return;
      }
      if (typeof navigator !== 'undefined' && !navigator.onLine) {
        message.error(
          getIntl().formatMessage({
            id: 'app.request.offline',
            defaultMessage: '网络不可用',
          }),
        );
        return;
      }
      message.error(text || '请求失败');
    },
  },
  requestInterceptors: [
    async (config: RequestOptions) => {
      const expectedUserId = config.expectedUserId;
      const credential =
        expectedUserId === undefined
          ? { token: await getAccessToken(), userId: undefined }
          : await getAccessTokenSnapshot();
      const tenantHeaders = getAbpHeaders();
      if (
        config.expectedTenantId !== undefined &&
        (tenantHeaders.__tenant ?? null) !== config.expectedTenantId
      ) {
        throw new RequestSessionContextChangedError('tenant');
      }
      if (
        expectedUserId !== undefined &&
        (!credential?.token || credential.userId !== expectedUserId)
      ) {
        throw new RequestSessionContextChangedError('user');
      }
      const headers = {
        ...(config.headers || {}),
        ...tenantHeaders,
        ...getAntiForgeryHeaders(),
      } as Record<string, string>;
      if (credential?.token) {
        headers.Authorization = `Bearer ${credential.token}`;
      }
      // ABP Angular 同款做法：SPA 语言即事实源，每请求经 Accept-Language 传给后端，
      // 后端错误消息与前端 UI 同语言（见 abp/culture.ts）。
      if (typeof document !== 'undefined') {
        headers['Accept-Language'] = toAbpCulture(getLocale());
      }
      return {
        ...config,
        headers,
      };
    },
  ],
  responseInterceptors: [
    // nginx 的 SPA 回退也可能返回 200。API/Hub 收到 HTML 时报告路由错误，空响应和文件下载仍然有效。
    async (response: any) => {
      const endpoint: string = (response?.config?.url || '').split(
        /[?#]/,
        1,
      )[0];
      const contentType: string =
        response?.headers?.['content-type'] ||
        response?.headers?.get?.('content-type') ||
        '';
      const status: number = response?.status ?? 0;
      if (
        (endpoint.includes('/api/') || endpoint.includes('/signalr-hubs/')) &&
        status >= 200 &&
        status < 300 &&
        contentType.includes('text/html')
      ) {
        throw new Error(`接口返回了 HTML 页面，请核对后端路由：${endpoint}`);
      }
      return response;
    },
  ],
};
