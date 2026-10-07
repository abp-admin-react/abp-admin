import { message, notification } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getAccessToken } from './abp/oidc';
import { getAbpHeaders } from './abp/tenant';
import { errorConfig, isNotFound, setNotificationInstance } from './requestErrorConfig';

const mockHistoryReplace = vi.hoisted(() => vi.fn());

vi.mock('antd', () => ({
  message: {
    warning: vi.fn(),
    error: vi.fn(),
  },
  notification: {
    error: vi.fn(),
  },
}));

vi.mock('@umijs/max', () => ({
  getIntl: vi.fn(() => ({
    formatMessage: vi.fn(({ defaultMessage }) => defaultMessage),
  })),
  getLocale: vi.fn(() => 'zh-CN'),
  history: {
    replace: mockHistoryReplace,
  },
}));

vi.mock('./abp/oidc', () => ({
  getAccessToken: vi.fn(),
}));

vi.mock('./abp/tenant', () => ({
  getAbpHeaders: vi.fn(() => ({})),
}));

describe('requestErrorConfig', () => {
  // biome-ignore lint/style/noNonNullAssertion: config handlers are always defined
  const errorThrower = errorConfig.errorConfig!.errorThrower!;
  // biome-ignore lint/style/noNonNullAssertion: config handlers are always defined
  const errorHandler = errorConfig.errorConfig!.errorHandler!;

  beforeEach(() => {
    vi.clearAllMocks();
    // 桥接实例是模块级状态：不重置会串场（上一用例注入的实例会劫持后续断言目标）
    setNotificationInstance(null);
  });

  describe('errorThrower', () => {
    it('should throw BizError with ABP error payload when success is false', () => {
      const response = {
        success: false,
        error: {
          code: 'AbpAdmin:01001',
          message: 'Bad Request',
          details: 'field invalid',
        },
      };

      expect.assertions(3);
      try {
        errorThrower(response);
      } catch (error: any) {
        expect(error.name).toBe('BizError');
        expect(error.message).toBe('Bad Request');
        expect(error.info).toEqual(response.error);
      }
    });

    it('should fall back to default message when error payload is missing', () => {
      expect(() => {
        errorThrower({ success: false });
      }).toThrow('Request failed');
    });

    it('should not throw error when success is true', () => {
      expect(() => {
        errorThrower({ success: true, data: { id: 1 } });
      }).not.toThrow();
    });
  });

  describe('errorHandler', () => {
    it('should rethrow error when skipErrorHandler is true', () => {
      const error = new Error('Test error');

      expect(() => {
        errorHandler(error, { skipErrorHandler: true });
      }).toThrow('Test error');
    });

    it('should redirect to login on 401 without showing message', () => {
      const error: any = new Error('Unauthorized');
      error.response = { status: 401, data: {} };

      errorHandler(error, {});

      expect(mockHistoryReplace).toHaveBeenCalledWith('/user/login');
      expect(message.error).not.toHaveBeenCalled();
      expect(notification.error).not.toHaveBeenCalled();
    });

    it('should show ABP error message on 403', () => {
      const error: any = new Error('Forbidden');
      error.response = {
        status: 403,
        data: { error: { message: '没有权限执行该操作' } },
      };

      errorHandler(error, {});

      expect(message.error).toHaveBeenCalledWith('没有权限执行该操作');
    });

    it('should join validation errors on 403', () => {
      const error: any = new Error('Forbidden');
      error.response = {
        status: 403,
        data: {
          error: {
            message: 'Validation failed',
            validationErrors: [
              { message: '名称必填' },
              { message: '邮箱格式错误' },
            ],
          },
        },
      };

      errorHandler(error, {});

      expect(message.error).toHaveBeenCalledWith('名称必填; 邮箱格式错误');
    });

    it('should show retry hint with seconds on 429', () => {
      const error: any = new Error('Too Many Requests');
      error.response = {
        status: 429,
        data: { error: { data: { RetryAfterSeconds: 90 } } },
      };

      errorHandler(error, {});

      expect(message.error).toHaveBeenCalledWith(
        '操作过于频繁，请在 1 分 30 秒 后重试',
      );
    });

    it('should show generic limit message on 429 without seconds', () => {
      const error: any = new Error('Too Many Requests');
      error.response = { status: 429, data: {} };

      errorHandler(error, {});

      expect(message.error).toHaveBeenCalledWith(
        '该操作已被限制，请联系管理员',
      );
    });

    it('should open error notification for other response errors', () => {
      const error: any = new Error('Server Error');
      error.response = {
        status: 500,
        data: { error: { message: '服务器内部错误' } },
      };

      errorHandler(error, {});

      expect(notification.error).toHaveBeenCalledWith({
        // antd 6：notification 的 message 键已更名 title（源文件随之迁移，测试对齐）
        title: 'HTTP 500',
        description: '服务器内部错误',
      });
      expect(message.error).not.toHaveBeenCalled();
    });

    it('should NOT open global notification for 404 (business semantics, page-level catch)', () => {
      const error: any = new Error('Not Found');
      error.response = { status: 404, data: {} };

      errorHandler(error, {});

      // 404 是业务语义（数据不存在/端点未覆盖），页面层自行 catch 降级——全局层零反馈
      expect(notification.error).not.toHaveBeenCalled();
      expect(message.error).not.toHaveBeenCalled();
    });

    it('should prefer the injected notification instance over the static one', () => {
      // app.tsx 的 NotificationInstanceBridge 在挂载后注入 App 上下文里的实例；
      // 注入后静态 notification（每弹一条打两条弃用警告）不得再被触达
      const injected = { error: vi.fn() };
      setNotificationInstance(injected as unknown as typeof notification);

      const error: any = new Error('Server Error');
      error.response = {
        status: 500,
        data: { error: { message: '服务器内部错误' } },
      };

      errorHandler(error, {});

      expect(injected.error).toHaveBeenCalledWith({
        title: 'HTTP 500',
        description: '服务器内部错误',
      });
      expect(notification.error).not.toHaveBeenCalled();
    });

    it('should fall back to the static notification when the instance is reset to null', () => {
      setNotificationInstance({
        error: vi.fn(),
      } as unknown as typeof notification);
      setNotificationInstance(null);

      const error: any = new Error('Server Error');
      error.response = { status: 500, data: {} };

      errorHandler(error, {});

      expect(notification.error).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'HTTP 500' }),
      );
    });

    it('should show offline message when navigator is offline', () => {
      const error: any = new Error('Network Error');

      const originalOnLine = navigator.onLine;
      Object.defineProperty(navigator, 'onLine', {
        configurable: true,
        get: () => false,
      });

      try {
        errorHandler(error, {});

        expect(message.error).toHaveBeenCalledWith('网络不可用');
      } finally {
        Object.defineProperty(navigator, 'onLine', {
          configurable: true,
          get: () => originalOnLine,
        });
      }
    });

    it('should show error message for generic errors', () => {
      errorHandler(new Error('Generic error'), {});

      expect(message.error).toHaveBeenCalledWith('Generic error');
    });
  });

  describe('requestInterceptors', () => {
    // The interceptor is registered as a plain function (not a tuple),
    // so narrow the union type to a callable for the test.
    const interceptor = errorConfig.requestInterceptors?.[0] as (config: {
      url?: string;
      method?: string;
      headers?: Record<string, string>;
    }) => Promise<{ url?: string; headers: Record<string, string> }>;

    it('should attach ABP headers and bearer token', async () => {
      vi.mocked(getAccessToken).mockResolvedValue('token-1');
      vi.mocked(getAbpHeaders).mockReturnValue({ 'x-tenant': 'tenant-1' });

      const result = await interceptor({
        url: 'https://api.example.com/users',
        method: 'GET',
      });

      expect(result.url).toBe('https://api.example.com/users');
      expect(result.headers.Authorization).toBe('Bearer token-1');
      expect(result.headers['x-tenant']).toBe('tenant-1');
      // SPA 语言单源：每请求经 Accept-Language 传给后端（zh-CN 映射 zh-Hans）
      expect(result.headers['Accept-Language']).toBe('zh-Hans');
    });

    it('should pass through config without Authorization when no token', async () => {
      vi.mocked(getAccessToken).mockResolvedValue(undefined);

      const result = await interceptor({
        url: 'https://api.example.com/users',
        method: 'GET',
      });

      expect(result.url).toBe('https://api.example.com/users');
      expect(result.headers.Authorization).toBeUndefined();
    });
  });
});

describe('isNotFound', () => {
  // 404 全局静默的配套判别器：页面层靠它区分「未配置/不存在」与「读失败」——
  // 形态漂移（如 umi 错误结构变化）会让 dataScope 一类分流静默误判

  it('识别 response.status 为 404 的错误', () => {
    const error = Object.assign(new Error('HTTP 404'), {
      response: { status: 404 },
    });
    expect(isNotFound(error)).toBe(true);
  });

  it.each([403, 500])('非 404 状态码返回 false（%d）', (status) => {
    const error = Object.assign(new Error(`HTTP ${status}`), {
      response: { status },
    });
    expect(isNotFound(error)).toBe(false);
  });

  it('无 response 形态（网络层错误/null）返回 false 而不是抛错', () => {
    expect(isNotFound(new Error('Network Error'))).toBe(false);
    expect(isNotFound(null)).toBe(false);
    expect(isNotFound(undefined)).toBe(false);
  });
});
