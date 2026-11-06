// @vitest-environment node
import { describe, expect, it } from 'vitest';
import {
  type CustomerLocalMessages,
  createCustomerError,
  isCustomerError,
  normalizeCustomerError,
  PUBLIC_ERROR_CONTRACT_SCHEMA_VERSION,
  parseAbpErrorResponse,
  parseClientFailureResult,
  parseOAuthErrorResponse,
  publicErrorCatalog,
} from './errors';

const catalog = publicErrorCatalog;
const localMessages: CustomerLocalMessages = {
  authentication: 'local-authentication',
  forbidden: 'local-forbidden',
  validation: 'local-validation',
  notFound: 'local-notFound',
  rateLimited: 'local-rateLimited',
  conflict: 'local-conflict',
  business: 'local-business',
  server: 'local-server',
  network: 'local-network',
  timeout: 'local-timeout',
  cancelled: 'local-cancelled',
  outcomeUnknown: 'local-outcomeUnknown',
};

function leakKeys(error: Error): string[] {
  return ['response', 'config', 'cause', 'raw', 'token', 'details'].filter(
    (key) => key in error,
  );
}

describe('public-error-catalog', () => {
  it('只包含公开码、分类与字段，不含内部映射或文案', () => {
    expect(catalog.schemaVersion).toBe(PUBLIC_ERROR_CONTRACT_SCHEMA_VERSION);
    expect(catalog.entries.map((entry) => entry.code)).toEqual([
      'AbpAdmin:Export:SyncThresholdExceeded',
      'AbpAdmin:Files:ContentMismatch',
      'BizTemplate:Public:ProjectUnavailable',
      'Client:AuthenticationRequired',
      'Client:Conflict',
      'Client:Forbidden',
      'Client:NotFound',
      'Client:OperationFailed',
      'Client:RateLimited',
      'Client:Unexpected',
      'Client:ValidationFailed',
    ]);
    const serialized = JSON.stringify(catalog);
    expect(serialized).not.toContain('InternalCode');
    expect(serialized).not.toContain('MessageKey');
    expect(serialized).not.toContain('请先登录');
    expect(serialized).not.toContain('"message"');
  });
});

describe('createCustomerError / isCustomerError', () => {
  it('创建安全错误且不附带原始响应字段', () => {
    const error = createCustomerError({
      message: 'safe',
      kind: 'forbidden',
      code: 'Client:Forbidden',
      httpStatus: 403,
      correlationId: 'cid',
    });
    expect(isCustomerError(error)).toBe(true);
    expect(error.fieldErrors).toEqual({});
    expect(leakKeys(error)).toEqual([]);
    expect(isCustomerError(new Error('nope'))).toBe(false);
  });
});

describe('parseAbpErrorResponse', () => {
  it('拒绝 HTML、内部字段、未登记 data 字段和 ValidationProblemDetails', () => {
    expect(
      parseAbpErrorResponse('<html><body>err</body></html>', catalog),
    ).toBeNull();
    expect(
      parseAbpErrorResponse(
        {
          error: {
            code: 'Client:NotFound',
            message: '请求的资源不存在。',
            stackTrace: 'at Foo',
          },
        },
        catalog,
      ),
    ).toBeNull();
    expect(
      parseAbpErrorResponse(
        {
          error: {
            code: 'Client:NotFound',
            message: '请求的资源不存在。',
            data: { correlationId: 'c', secret: 'nope' },
          },
        },
        catalog,
      ),
    ).toBeNull();
    expect(
      parseAbpErrorResponse(
        {
          error: {
            code: 'Client:ValidationFailed',
            message: '输入校验未通过。',
            validationErrors: { name: ['required'] },
          },
        },
        catalog,
      ),
    ).toBeNull();
  });

  it('接受 ABP members 形状并忽略 details', () => {
    const parsed = parseAbpErrorResponse(
      {
        error: {
          code: 'Client:ValidationFailed',
          message: '输入校验未通过。',
          details: 'DO NOT USE',
          data: { correlationId: 'c1' },
          validationErrors: [{ message: '名称不能为空', members: ['name'] }],
        },
      },
      catalog,
    );
    expect(parsed).toMatchObject({
      code: 'Client:ValidationFailed',
      message: '输入校验未通过。',
      data: { correlationId: 'c1' },
    });
    expect(parsed?.validationErrors[0]?.members).toEqual(['name']);
  });
});

describe('parseOAuthErrorResponse', () => {
  it('拒绝 HTML 与内部字段，且不把 description 当业务码', () => {
    expect(parseOAuthErrorResponse('<html>no</html>')).toBeNull();
    expect(
      parseOAuthErrorResponse({
        error: 'invalid_token',
        error_description: 'stack at Foo',
        stackTrace: 'x',
      }),
    ).toBeNull();
    expect(
      parseOAuthErrorResponse({
        error: 'session_revoked',
        error_description: 'session gone',
      }),
    ).toEqual({
      error: 'session_revoked',
      errorDescription: 'session gone',
    });
  });
});

describe('normalizeCustomerError', () => {
  it('精确匹配快照才接纳服务端 message，否则本地兜底', () => {
    const accepted = normalizeCustomerError({
      status: 404,
      body: {
        error: {
          code: 'Client:NotFound',
          message: '请求的资源不存在。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(accepted.message).toBe('请求的资源不存在。');
    expect(accepted.code).toBe('Client:NotFound');
    expect(accepted.kind).toBe('notFound');

    const unknown = normalizeCustomerError({
      status: 400,
      body: {
        error: {
          code: 'Biz:Secret:Internal',
          message: 'SELECT * FROM users',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(unknown.message).toBe('local-business');
    expect(unknown.code).toBeUndefined();
  });

  it('HTTP 401 只允许 AuthenticationRequired 或无码，其他码丢弃', () => {
    const other = normalizeCustomerError({
      status: 401,
      body: {
        error: {
          code: 'Client:Forbidden',
          message: '没有权限执行此操作。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(other.kind).toBe('authentication');
    expect(other.code).toBeUndefined();
    expect(other.message).toBe('local-authentication');

    const empty = normalizeCustomerError({
      status: 401,
      body: null,
      catalog,
      localMessages,
    });
    expect(empty.kind).toBe('authentication');
    expect(empty.message).toBe('local-authentication');
  });

  it('OAuth access_denied 在 HTTP 403 标 forbidden，401 仍 authentication', () => {
    const forbidden = normalizeCustomerError({
      status: 403,
      body: { error: 'access_denied', error_description: 'no' },
      catalog,
      localMessages,
    });
    expect(forbidden.kind).toBe('forbidden');

    const unauth = normalizeCustomerError({
      status: 401,
      body: { error: 'access_denied', error_description: 'no' },
      catalog,
      localMessages,
    });
    expect(unauth.kind).toBe('authentication');
  });

  it('HTTP 403 上的 authentication 丢弃，合法 business/403 保留', () => {
    const discarded = normalizeCustomerError({
      status: 403,
      body: {
        error: {
          code: 'Client:AuthenticationRequired',
          message: '请先登录后再试。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(discarded.kind).toBe('forbidden');
    expect(discarded.code).toBeUndefined();
    expect(discarded.message).toBe('local-forbidden');

    const business = normalizeCustomerError({
      status: 403,
      body: {
        error: {
          code: 'BizTemplate:Public:ProjectUnavailable',
          message: '该项目当前不可用，请刷新后查看。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(business.kind).toBe('business');
    expect(business.code).toBe('BizTemplate:Public:ProjectUnavailable');
    expect(business.message).toBe('该项目当前不可用，请刷新后查看。');
  });

  it('Retry-After 头优先，非法丢弃，0 合法，超时不展示倒计时', () => {
    const headerWins = normalizeCustomerError({
      status: 429,
      headers: { 'Retry-After': '12' },
      body: {
        error: {
          code: 'Client:RateLimited',
          message: '请求过于频繁，请稍后再试。',
          data: { correlationId: 'c', retryAfterSeconds: 99 },
        },
      },
      catalog,
      localMessages,
    });
    expect(headerWins.retryAfterSeconds).toBe(12);

    const negativeDate = normalizeCustomerError({
      status: 429,
      headers: { 'Retry-After': 'Wed, 21 Oct 2015 07:28:00 GMT' },
      body: {
        error: {
          code: 'Client:RateLimited',
          message: '请求过于频繁，请稍后再试。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
      now: Date.parse('Wed, 21 Oct 2015 07:28:05 GMT'),
    });
    expect(negativeDate.retryAfterSeconds).toBe(0);

    const illegal = normalizeCustomerError({
      status: 429,
      headers: { 'Retry-After': '-3' },
      body: {
        error: {
          code: 'Client:RateLimited',
          message: '请求过于频繁，请稍后再试。',
          data: { correlationId: 'c', retryAfterSeconds: 'nope' },
        },
      },
      catalog,
      localMessages,
    });
    expect(illegal.retryAfterSeconds).toBeUndefined();
    expect(illegal.code).toBe('Client:RateLimited');

    const zero = normalizeCustomerError({
      status: 429,
      headers: { 'Retry-After': '0' },
      body: {
        error: {
          code: 'Client:RateLimited',
          message: '请求过于频繁，请稍后再试。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(zero.retryAfterSeconds).toBe(0);

    const tooLarge = normalizeCustomerError({
      status: 429,
      headers: { 'Retry-After': '86401' },
      body: {
        error: {
          code: 'Client:RateLimited',
          message: '请求过于频繁，请稍后再试。',
          data: { correlationId: 'c' },
        },
      },
      catalog,
      localMessages,
    });
    expect(tooLarge.retryAfterSeconds).toBeUndefined();
    expect(tooLarge.code).toBe('Client:RateLimited');
  });

  it('fieldErrors 来自 validationErrors.members，忽略 details 且不附带 raw', () => {
    const error = normalizeCustomerError({
      status: 400,
      body: {
        error: {
          code: 'Client:ValidationFailed',
          message: '输入校验未通过。',
          details: 'stack',
          data: { correlationId: 'c' },
          validationErrors: [
            { message: '名称不能为空', members: ['name', 'title'] },
          ],
        },
      },
      catalog,
      localMessages,
    });
    expect(error.fieldErrors).toEqual({
      name: ['名称不能为空'],
      title: ['名称不能为空'],
    });
    expect(error).not.toHaveProperty('details');
    expect(leakKeys(error)).toEqual([]);
  });

  it('写超时为 outcomeUnknown，取消为 cancelled，GET 网络为 network', () => {
    expect(
      normalizeCustomerError({
        method: 'POST',
        timeout: true,
        catalog,
        localMessages,
      }).kind,
    ).toBe('outcomeUnknown');
    expect(
      normalizeCustomerError({
        aborted: true,
        catalog,
        localMessages,
      }).kind,
    ).toBe('cancelled');
    expect(
      normalizeCustomerError({
        method: 'GET',
        network: true,
        catalog,
        localMessages,
      }).kind,
    ).toBe('network');
  });

  it('HTTP 200 任务项按结果契约校验', () => {
    const item = parseClientFailureResult(
      {
        code: 'Client:OperationFailed',
        message: '操作失败，请稍后重试。',
        correlationId: 'task-1',
        data: { correlationId: 'task-1' },
      },
      catalog,
    );
    expect(item?.code).toBe('Client:OperationFailed');

    const error = normalizeCustomerError({
      status: 200,
      asResultItem: true,
      body: {
        code: 'Client:AuthenticationRequired',
        message: '请先登录后再试。',
        correlationId: 'task-auth',
        data: { correlationId: 'task-auth' },
      },
      catalog,
      localMessages,
    });
    expect(error.kind).toBe('authentication');
    expect(error.httpStatus).toBe(200);
    expect(error.code).toBe('Client:AuthenticationRequired');
  });
});
