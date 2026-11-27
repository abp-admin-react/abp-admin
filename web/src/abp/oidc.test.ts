import { beforeEach, describe, expect, it, vi } from 'vitest';
import { completeLogin, getAccessTokenSnapshot, startLogin } from './oidc';
import { getStoredTenant, setStoredTenant } from './tenant';

const { getUser, signinRedirect, signinRedirectCallback } = vi.hoisted(() => ({
  getUser: vi.fn(),
  signinRedirect: vi.fn(),
  signinRedirectCallback: vi.fn(),
}));

const verified = { id: 'verified', name: 'demo' };

vi.mock('oidc-client-ts', () => ({
  User: class {},
  UserManager: class {
    getUser = getUser;
    signinRedirect = signinRedirect;
    signinRedirectCallback = signinRedirectCallback;
  },
  WebStorageStateStore: class {},
}));

describe('OpenIddict 登录上下文', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setStoredTenant(null);
  });

  it('显式 Host 覆盖残留租户并替换中间页历史记录，保留宿主 SSO', async () => {
    setStoredTenant({ id: 'stale', name: 'stale' });

    await startLogin({ tenant: null, replace: true });

    expect(signinRedirect).toHaveBeenCalledExactlyOnceWith({
      extraQueryParams: { hostLogin: '1' },
      state: { loginTenant: null },
      redirectMethod: 'replace',
    });
  });

  it('显式租户覆盖残留租户', async () => {
    setStoredTenant({ id: 'stale', name: 'stale' });

    await startLogin({ tenant: verified });

    expect(signinRedirect).toHaveBeenCalledExactlyOnceWith({
      extraQueryParams: { __tenant: 'verified', prompt: 'login' },
      state: { loginTenant: verified },
    });
  });

  it.each([null, verified])(
    '回调恢复本次授权使用的租户 (%j)',
    async (loginTenant) => {
      const user = { state: { loginTenant } };
      signinRedirectCallback.mockResolvedValueOnce(user);
      setStoredTenant({ id: 'different-tab', name: 'different-tab' });

      expect(await completeLogin()).toBe(user);
      expect(getStoredTenant()).toEqual(loginTenant);
    },
  );

  it('没有显式租户的回调保留原先选中的租户', async () => {
    signinRedirectCallback.mockResolvedValueOnce({});
    setStoredTenant(verified);

    await completeLogin();

    expect(getStoredTenant()).toEqual(verified);
  });

  it('未指定时沿用租户选择，Host 保留默认 OIDC 流程', async () => {
    await startLogin();
    expect(signinRedirect).toHaveBeenLastCalledWith({ extraQueryParams: {} });

    setStoredTenant({ id: 'selected', name: 'demo' });
    await startLogin();
    expect(signinRedirect).toHaveBeenLastCalledWith({
      extraQueryParams: { __tenant: 'selected', prompt: 'login' },
    });
  });

  it('从同一次读取返回访问令牌与账号标识', async () => {
    getUser
      .mockResolvedValueOnce({
        access_token: 'previous-token',
        profile: { sub: 'previous-account' },
      })
      .mockResolvedValueOnce({
        access_token: 'current-token',
        profile: { sub: 'current-account' },
      });

    expect(await getAccessTokenSnapshot()).toEqual({
      token: 'previous-token',
      userId: 'previous-account',
    });
    expect(getUser).toHaveBeenCalledTimes(1);
    expect(await getAccessTokenSnapshot()).toEqual({
      token: 'current-token',
      userId: 'current-account',
    });
  });

  it('过期会话没有可用的请求凭据', async () => {
    getUser.mockResolvedValueOnce({
      access_token: 'expired-token',
      profile: { sub: 'same-account' },
      expired: true,
    });
    expect(await getAccessTokenSnapshot()).toBeUndefined();
  });
});
