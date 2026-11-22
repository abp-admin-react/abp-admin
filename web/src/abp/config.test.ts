import { afterEach, describe, expect, it, vi } from 'vitest';
import { findTenantByName } from './config';

vi.mock('@umijs/max', () => ({ request: vi.fn() }));

describe('匿名登录配置', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('编码指定租户名称且不携带旧租户 Cookie', async () => {
    const lookup = {
      success: true,
      name: 'a/b',
      tenantId: 'id',
      isActive: true,
    };
    const fetch = vi.fn().mockResolvedValue(Response.json(lookup));
    vi.stubGlobal('fetch', fetch);

    expect(await findTenantByName('a/b')).toEqual(lookup);
    expect(fetch).toHaveBeenCalledExactlyOnceWith(
      '/api/abp/multi-tenancy/tenants/by-name/a%2Fb',
      {
        cache: 'no-store',
        credentials: 'omit',
        signal: expect.any(AbortSignal),
      },
    );
  });

  it('拒绝失败的租户名称查询', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 503 })),
    );

    await expect(findTenantByName('demo')).rejects.toThrow('HTTP 503');
  });
});
