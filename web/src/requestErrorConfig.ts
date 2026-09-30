import type { RequestOptions } from '@@/plugin-request/request';
import type { RequestConfig } from '@umijs/max';
import { getIntl, history } from '@umijs/max';
import { message, notification } from 'antd';
import { getAccessToken } from './abp/oidc';
import { getAbpHeaders } from './abp/tenant';
import { formatRetryAfter } from './utils/format';

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
      if (opts?.skipErrorHandler) throw error;
      if (error.response?.status === 401) {
        history.replace('/user/login');
        return;
      }
      const abpError = error.response?.data?.error as AbpError['error'];
      const text =
        abpError?.validationErrors?.map((item) => item.message).join('; ') ||
        abpError?.details ||
        abpError?.message ||
        error.message;
      if (error.response?.status === 403) {
        message.error(text || '没有权限');
        return;
      }
      // T2.5: 操作限流 429 处理
      if (error.response?.status === 429) {
        const data = (error.response.data?.error?.data ?? {}) as Record<
          string,
          any
        >;
        const seconds = Number(data.RetryAfterSeconds ?? 0);
        const retryText =
          seconds > 0
            ? `操作过于频繁，请在 ${formatRetryAfter(seconds)} 后重试`
            : '该操作已被限制，请联系管理员';
        message.error(retryText);
        return;
      }
      if (error.response) {
        if (error.response?.status === 404) {
          // 404 是业务语义（数据不存在/端点未覆盖），页面层已 catch 降级——不弹全局错误。
          // 开发期留一条 console 痕迹：静默是全局面板级策略，端点拼写错/契约漂移时
          // 没有任何线索会显著拖慢排查（生产不输出）
          if (process.env.NODE_ENV === 'development') {
            console.warn(
              '[request] 404 silenced (page layer should catch):',
              error.config?.url,
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
      const token = await getAccessToken();
      const headers = {
        ...(config.headers || {}),
        ...getAbpHeaders(),
        ...getAntiForgeryHeaders(),
      } as Record<string, string>;
      if (token) {
        headers.Authorization = `Bearer ${token}`;
      }
      return {
        ...config,
        headers,
      };
    },
  ],
  responseInterceptors: [],
};
