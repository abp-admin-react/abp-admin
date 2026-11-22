import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { App } from 'antd';
import React, { StrictMode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getStoredTenant, setStoredTenant } from '@/abp/tenant';
import Login from './index';

const mocks = vi.hoisted(() => ({
  findTenantByName: vi.fn(),
  startLogin: vi.fn(),
  replace: vi.fn(),
  loginWithMagicLink: vi.fn(),
  isSubdomainTenantMode: vi.fn(),
  syncTenantFromSubdomain: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  Helmet: () => null,
  history: { replace: mocks.replace },
}));
vi.mock('@/abp/config', () => ({
  findTenantByName: mocks.findTenantByName,
}));
vi.mock('@/abp/oidc', () => ({
  applyTokensForNewSession: vi.fn(),
  startLogin: mocks.startLogin,
}));
vi.mock('@/abp/account', () => ({
  loginWithMagicLink: mocks.loginWithMagicLink,
}));
vi.mock('@/abp/subdomain', () => ({
  syncTenantFromSubdomain: mocks.syncTenantFromSubdomain,
}));
vi.mock('@/abp/tenant', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/tenant')>()),
  isSubdomainTenantMode: mocks.isSubdomainTenantMode,
}));

const tenant = { id: 'tenant-id', name: 'demo' };

function renderLogin() {
  return render(
    <StrictMode>
      <App>
        <Login />
      </App>
    </StrictMode>,
  );
}

describe('登录入口的租户选择', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    window.history.replaceState(null, '', '/user/login');
    mocks.startLogin.mockResolvedValue(undefined);
    mocks.isSubdomainTenantMode.mockReturnValue(false);
  });

  it('裸域名清理本地旧租户，只发起一次 Host 登录', async () => {
    window.history.replaceState(null, '', '/user/login?redirect=%2F');
    setStoredTenant(tenant);

    renderLogin();

    await waitFor(() =>
      expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({
        tenant: null,
        replace: true,
      }),
    );
    expect(getStoredTenant()).toBeNull();
    expect(screen.queryByRole('button', { name: '切换租户' })).toBeNull();
    expect(screen.queryByPlaceholderText('租户名称，留空表示 Host')).toBeNull();
    expect(screen.queryByText('当前上下文：Host')).toBeNull();
    expect(mocks.findTenantByName).not.toHaveBeenCalled();
  });

  it('管理端鉴权失败后等待手动重试，避免静默登录循环', async () => {
    window.history.replaceState(null, '', '/user/login?retry=1');
    setStoredTenant(tenant);

    renderLogin();

    const retry = await screen.findByRole('button', { name: '重试登录' });
    expect(mocks.startLogin).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: '切换租户' })).toBeNull();
    fireEvent.click(retry);
    expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({
      tenant: null,
      replace: true,
    });
    expect(getStoredTenant()).toBeNull();
  });

  it('自动跳转失败后可重试且不显示租户选择', async () => {
    mocks.startLogin.mockRejectedValueOnce(new Error('discovery unavailable'));

    renderLogin();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '无法打开登录页',
    );
    expect(screen.queryByRole('button', { name: '切换租户' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '重试登录' }));
    expect(mocks.startLogin).toHaveBeenCalledTimes(2);
    expect(mocks.startLogin).toHaveBeenLastCalledWith({
      tenant: null,
      replace: true,
    });
  });

  it('显式租户链接先验证租户，再按验证出的 ID 登录', async () => {
    window.history.replaceState(null, '', '/user/login?tenant=demo');
    const lookup = Promise.withResolvers<{
      success: true;
      isActive: true;
      tenantId: string;
    }>();
    mocks.findTenantByName.mockReturnValue(lookup.promise);

    renderLogin();
    expect(mocks.findTenantByName).toHaveBeenCalledWith('demo');
    expect(mocks.startLogin).not.toHaveBeenCalled();
    await act(async () =>
      lookup.resolve({ success: true, isActive: true, tenantId: tenant.id }),
    );

    expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({
      tenant,
      replace: true,
    });
    expect(getStoredTenant()).toEqual(tenant);
  });

  it.each([
    { success: false },
    { success: true, isActive: false, tenantId: tenant.id },
  ])('显式租户无效时不降级成 Host 登录 (%j)', async (result) => {
    window.history.replaceState(null, '', '/user/login?tenant=demo');
    mocks.findTenantByName.mockResolvedValue(result);

    renderLogin();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '租户不存在或未启用',
    );
    expect(screen.getByRole('link', { name: '返回登录' })).toHaveAttribute(
      'href',
      '/user/login',
    );
    expect(mocks.startLogin).not.toHaveBeenCalled();
  });

  it('子域名租户优先，继续使用已有登录入口', async () => {
    window.history.replaceState(null, '', '/user/login?tenant=another');
    mocks.isSubdomainTenantMode.mockReturnValue(true);
    mocks.syncTenantFromSubdomain.mockResolvedValue({ mode: 'tenant', tenant });

    renderLogin();

    expect(await screen.findByText('当前上下文：demo（子域名）')).toBeVisible();
    expect(screen.queryByRole('button', { name: '切换租户' })).toBeNull();
    expect(mocks.findTenantByName).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: '使用 ABP 账号登录' }));
    expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({ tenant });
  });

  it('保留不存在的租户子域名跳转', async () => {
    mocks.isSubdomainTenantMode.mockReturnValue(true);
    mocks.syncTenantFromSubdomain.mockResolvedValue({
      mode: 'missing',
      name: 'nope',
    });

    renderLogin();

    await waitFor(() =>
      expect(mocks.replace).toHaveBeenCalledExactlyOnceWith(
        '/tenant-not-found',
      ),
    );
    expect(mocks.startLogin).not.toHaveBeenCalled();
  });

  it('邮件链接和验证码优先于租户选择，只消费一次链接', async () => {
    window.history.replaceState(
      null,
      '',
      '/user/login?tenant=demo&email=demo%40example.com&magicLinkToken=token',
    );
    mocks.findTenantByName.mockResolvedValue({
      success: true,
      isActive: true,
      tenantId: tenant.id,
    });
    mocks.loginWithMagicLink.mockRejectedValue(new Error('expired'));

    renderLogin();

    const otp = await screen.findByPlaceholderText('6 位验证码');
    expect(window.location.search).toBe('?tenant=demo');
    expect(getStoredTenant()).toEqual({ id: tenant.id, name: 'demo' });
    expect(mocks.loginWithMagicLink).toHaveBeenCalledExactlyOnceWith({
      email: 'demo@example.com',
      magicLinkToken: 'token',
      tenantName: 'demo',
    });
    fireEvent.change(otp, { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: '用验证码登录' }));
    await waitFor(() =>
      expect(mocks.loginWithMagicLink).toHaveBeenLastCalledWith({
        email: 'demo@example.com',
        code: '123456',
        tenantName: 'demo',
      }),
    );
    expect(mocks.startLogin).not.toHaveBeenCalled();

    fireEvent.click(
      await screen.findByRole('button', { name: '返回账号密码登录' }),
    );
    await waitFor(() =>
      expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({
        tenant,
        replace: true,
      }),
    );
  });

  it('Host 邮件链接回到账号密码登录时按普通入口处理', async () => {
    window.history.replaceState(
      null,
      '',
      '/user/login?email=demo%40example.com&magicLinkToken=token',
    );
    mocks.loginWithMagicLink.mockRejectedValue(new Error('expired'));

    renderLogin();

    fireEvent.click(
      await screen.findByRole('button', { name: '返回账号密码登录' }),
    );
    await waitFor(() =>
      expect(mocks.startLogin).toHaveBeenCalledExactlyOnceWith({
        tenant: null,
        replace: true,
      }),
    );
    expect(mocks.findTenantByName).not.toHaveBeenCalled();
  });

  it('Host 邮件链接换票前清掉本地旧租户', async () => {
    setStoredTenant(tenant);
    window.history.replaceState(
      null,
      '',
      '/user/login?email=demo%40example.com&magicLinkToken=token',
    );
    mocks.loginWithMagicLink.mockImplementation(async () => {
      expect(getStoredTenant()).toBeNull();
      throw new Error('expired');
    });

    renderLogin();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '无法使用此链接登录，可改用邮件中的验证码',
    );
    expect(getStoredTenant()).toBeNull();
  });

  it('显式租户查询失败时不降级成 Host 登录', async () => {
    window.history.replaceState(null, '', '/user/login?tenant=demo');
    mocks.findTenantByName.mockRejectedValue(new Error('offline'));

    renderLogin();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '无法验证租户，请刷新后重试',
    );
    expect(mocks.startLogin).not.toHaveBeenCalled();
  });
});
