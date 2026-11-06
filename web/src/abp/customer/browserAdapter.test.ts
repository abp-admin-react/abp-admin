// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';
import {
  CUSTOMER_TRANSPORT_REJECTED,
  type CustomerFetch,
  createBrowserTransport,
} from './browserAdapter';

const origin = 'https://app.example.com';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('createBrowserTransport', () => {
  it('拒绝 userinfo、协议相对、反斜杠、编码斜杠、未登记路径和跨源', async () => {
    const fetchImpl = vi.fn<CustomerFetch>();
    const transport = createBrowserTransport({ origin, fetchImpl });
    const rejected = [
      'https://alice:secret@app.example.com/api/client/x',
      '//evil.example/api/client/x',
      '/api/client\\secret',
      '/api/client/%2Fsecret',
      '/api/app/books',
      'https://evil.example/api/client/x',
    ];
    for (const url of rejected) {
      await expect(
        transport({ url, method: 'GET', headers: {} }),
      ).rejects.toMatchObject({ name: CUSTOMER_TRANSPORT_REJECTED });
    }
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it('允许含 @/% 的合法编码查询', async () => {
    const fetchImpl = vi.fn<CustomerFetch>(async () =>
      jsonResponse({ items: [], totalCount: 0 }),
    );
    const transport = createBrowserTransport({ origin, fetchImpl });
    const result = await transport({
      url: '/api/client/projects?email=user%40example.com&q=%25xx',
      method: 'GET',
      headers: {},
    });
    expect(result.status).toBe(200);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
    const calledUrl = String(fetchImpl.mock.calls[0]?.[0]);
    expect(calledUrl.startsWith('/api/client/projects')).toBe(true);
    expect(calledUrl).toContain('email=');
    expect(calledUrl).toContain('q=');
    expect(fetchImpl.mock.calls[0]?.[1]).toMatchObject({
      credentials: 'same-origin',
      redirect: 'error',
    });
  });

  it('调用方不能覆盖 credentials 与 redirect', async () => {
    const fetchImpl = vi.fn<CustomerFetch>(async () =>
      jsonResponse({ ok: true }),
    );
    const transport = createBrowserTransport({ origin, fetchImpl });
    await transport({
      url: '/api/abp/application-configuration',
      method: 'GET',
      headers: {},
    });
    const init = fetchImpl.mock.calls[0]?.[1];
    expect(init?.credentials).toBe('same-origin');
    expect(init?.redirect).toBe('error');
  });

  it('区分调用方取消与超时', async () => {
    const fetchImpl = vi.fn<CustomerFetch>(
      (_url, init) =>
        new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => {
            const error = new Error('aborted');
            error.name = 'AbortError';
            reject(error);
          });
        }),
    );
    const transport = createBrowserTransport({ origin, fetchImpl });

    const timeoutResult = await transport({
      url: '/connect/token',
      method: 'POST',
      headers: {},
      timeoutMs: 5,
    });
    expect(timeoutResult.timeout).toBe(true);
    expect(timeoutResult.aborted).toBe(false);

    const controller = new AbortController();
    controller.abort();
    const abortedResult = await transport({
      url: '/api/account/my-profile',
      method: 'GET',
      headers: {},
      signal: controller.signal,
    });
    expect(abortedResult.aborted).toBe(true);
    expect(abortedResult.timeout).toBe(false);
  });

  it('注入语言、租户、CSRF，不写死登录地址', async () => {
    const fetchImpl = vi.fn<CustomerFetch>(async () => jsonResponse({}));
    const transport = createBrowserTransport({
      origin,
      fetchImpl,
      getLanguage: () => 'zh-Hans',
      getTenantId: () => 'tenant-1',
      getCsrfToken: () => 'csrf-token',
    });
    await transport({
      url: '/api/client/biz-template/projects',
      method: 'GET',
      headers: { Authorization: 'Bearer memory-only' },
    });
    const init = fetchImpl.mock.calls[0]?.[1];
    const headers = (init?.headers ?? {}) as Record<string, string>;
    expect(headers['Accept-Language']).toBe('zh-Hans');
    expect(headers.__tenant).toBe('tenant-1');
    expect(headers.RequestVerificationToken).toBe('csrf-token');
    expect(headers.Authorization).toBe('Bearer memory-only');
  });
});
