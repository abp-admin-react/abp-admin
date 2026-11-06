// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';
import type { CustomerLocalMessages } from './errors';
import { isCustomerError, publicErrorCatalog } from './errors';
import {
  type CustomerAuthAdapter,
  type CustomerTransportResult,
  createCustomerRequest,
} from './request';

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

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

describe('createCustomerRequest', () => {
  it('同会话 5 个并发 401 只恢复一次', async () => {
    let generation = 1;
    let recoverCount = 0;
    const started = deferred<void>();
    const release = deferred<void>();
    const auth: CustomerAuthAdapter = {
      getSnapshot: () => ({ sessionGeneration: generation }),
      applyHeaders: () => {},
      recoverFromUnauthorized: async (snapshot) => {
        recoverCount += 1;
        if (snapshot.sessionGeneration !== generation) {
          throw new Error('late 401 cleared new session');
        }
        started.resolve();
        await release.promise;
        generation += 1;
        return true;
      },
    };
    const request = createCustomerRequest({
      transport: async () => ({
        status: 401,
        body: {
          error: {
            code: 'Client:AuthenticationRequired',
            message: '请先登录后再试。',
            data: { correlationId: 'c' },
          },
        },
        headers: {},
      }),
      auth,
      messages: localMessages,
      notify: { global: vi.fn(), form: vi.fn() },
      catalog,
    });

    const pending = Promise.allSettled([
      request('/api/client/a'),
      request('/api/client/b'),
      request('/api/client/c'),
      request('/api/client/d'),
      request('/api/client/e'),
    ]);
    await started.promise;
    release.resolve();
    const results = await pending;
    expect(recoverCount).toBe(1);
    expect(generation).toBe(2);
    expect(results.every((item) => item.status === 'rejected')).toBe(true);
    for (const item of results) {
      if (item.status === 'rejected') {
        expect(isCustomerError(item.reason)).toBe(true);
      }
    }
  });

  it('迟到的旧会话 401 不会清除新会话', async () => {
    let generation = 1;
    let recoverCount = 0;
    const auth: CustomerAuthAdapter = {
      getSnapshot: () => ({ sessionGeneration: generation }),
      applyHeaders: () => {},
      recoverFromUnauthorized: async (snapshot) => {
        recoverCount += 1;
        if (snapshot.sessionGeneration !== generation) {
          throw new Error('late 401 cleared new session');
        }
        generation += 1;
        return true;
      },
    };
    const request = createCustomerRequest({
      transport: async () => {
        generation = 2;
        return {
          status: 401,
          body: {
            error: {
              code: 'Client:AuthenticationRequired',
              message: '请先登录后再试。',
              data: { correlationId: 'c' },
            },
          },
          headers: {},
        };
      },
      auth,
      messages: localMessages,
      notify: { global: vi.fn() },
      catalog,
    });

    await expect(request('/api/client/late')).rejects.toMatchObject({
      kind: 'authentication',
    });
    expect(recoverCount).toBe(0);
    expect(generation).toBe(2);
  });

  it('403 不触发恢复', async () => {
    const recoverFromUnauthorized = vi.fn(async () => true);
    const request = createCustomerRequest({
      transport: async () => ({
        status: 403,
        body: {
          error: {
            code: 'Client:Forbidden',
            message: '没有权限执行此操作。',
            data: { correlationId: 'c' },
          },
        },
        headers: {},
      }),
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized,
      },
      messages: localMessages,
      notify: { global: vi.fn() },
      catalog,
    });
    await expect(request('/api/client/x')).rejects.toMatchObject({
      kind: 'forbidden',
    });
    expect(recoverFromUnauthorized).not.toHaveBeenCalled();
  });

  it('HTTP 200 任务认证失败不恢复会话', async () => {
    const recoverFromUnauthorized = vi.fn(async () => true);
    const request = createCustomerRequest({
      transport: async () => ({
        status: 200,
        body: {
          code: 'Client:AuthenticationRequired',
          message: '请先登录后再试。',
          correlationId: 'task',
          data: { correlationId: 'task' },
        },
        headers: {},
      }),
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized,
      },
      messages: localMessages,
      notify: { global: vi.fn() },
      catalog,
    });
    await expect(request('/api/client/task')).rejects.toMatchObject({
      kind: 'authentication',
      httpStatus: 200,
    });
    expect(recoverFromUnauthorized).not.toHaveBeenCalled();
  });

  it('skipErrorHandler 仍归一化且不展示', async () => {
    const notify = { global: vi.fn(), form: vi.fn(), page: vi.fn() };
    const request = createCustomerRequest({
      transport: async () => ({
        status: 404,
        body: '<html>not found</html>',
        headers: {},
      }),
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized: async () => true,
      },
      messages: localMessages,
      notify,
      catalog,
    });
    await expect(
      request('/api/client/missing', { skipErrorHandler: true }),
    ).rejects.toMatchObject({
      kind: 'notFound',
      message: 'local-notFound',
    });
    expect(notify.global).not.toHaveBeenCalled();
    expect(notify.form).not.toHaveBeenCalled();
    expect(notify.page).not.toHaveBeenCalled();
  });

  it('写超时结果未知且不重试', async () => {
    let calls = 0;
    const request = createCustomerRequest({
      transport: async () => {
        calls += 1;
        return { timeout: true } satisfies CustomerTransportResult;
      },
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized: async () => true,
      },
      messages: localMessages,
      notify: { global: vi.fn() },
      catalog,
    });
    await expect(
      request('/api/client/write', { method: 'POST', data: { name: 'x' } }),
    ).rejects.toMatchObject({
      kind: 'outcomeUnknown',
      message: 'local-outcomeUnknown',
    });
    expect(calls).toBe(1);
  });

  it('Abort 静默展示但仍以 cancelled reject', async () => {
    const notify = { global: vi.fn(), form: vi.fn() };
    const request = createCustomerRequest({
      transport: async () => ({ aborted: true }),
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized: async () => true,
      },
      messages: localMessages,
      notify,
      catalog,
    });
    await expect(request('/api/client/abort')).rejects.toMatchObject({
      kind: 'cancelled',
    });
    expect(notify.global).not.toHaveBeenCalled();
  });

  it('有 fieldErrors 时走 form 展示', async () => {
    const notify = { global: vi.fn(), form: vi.fn() };
    const request = createCustomerRequest({
      transport: async () => ({
        status: 400,
        body: {
          error: {
            code: 'Client:ValidationFailed',
            message: '输入校验未通过。',
            data: { correlationId: 'c' },
            validationErrors: [{ message: '名称不能为空', members: ['name'] }],
          },
        },
        headers: {},
      }),
      auth: {
        getSnapshot: () => ({ sessionGeneration: 1 }),
        applyHeaders: () => {},
        recoverFromUnauthorized: async () => true,
      },
      messages: localMessages,
      notify,
      catalog,
    });
    await expect(
      request('/api/client/form', { method: 'POST' }),
    ).rejects.toMatchObject({
      kind: 'validation',
    });
    expect(notify.form).toHaveBeenCalledTimes(1);
    expect(notify.global).not.toHaveBeenCalled();
  });
});
