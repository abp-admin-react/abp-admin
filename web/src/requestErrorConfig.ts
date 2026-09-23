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
        notification.error({
          message: `HTTP ${error.response.status}`,
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
