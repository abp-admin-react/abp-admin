/** 登录入口：裸域名进入 Host；子域名、?tenant= 和邮件链接仍指定租户。 */
import { Helmet, history } from '@umijs/max';
import { Alert, App, Button, Card, Input, Space, Spin, Typography } from 'antd';
import { createStyles } from 'antd-style';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { loginWithMagicLink } from '@/abp/account';
import { findTenantByName } from '@/abp/config';
import { applyTokensForNewSession, startLogin } from '@/abp/oidc';
import { syncTenantFromSubdomain } from '@/abp/subdomain';
import {
  isSubdomainTenantMode,
  type StoredTenant,
  setStoredTenant,
} from '@/abp/tenant';
import { DEFAULT_LANDING_PATH } from '@/utils/redirect';
import Settings from '../../../../config/defaultSettings';

const useStyles = createStyles(({ token }) => ({
  lang: {
    width: 42,
    height: 42,
    lineHeight: '42px',
    position: 'fixed',
    right: 16,
    borderRadius: token.borderRadius,
  },
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    overflow: 'auto',
    backgroundImage:
      "url('https://mdn.alipayobjects.com/yuyan_qk0oxh/afts/img/V-_oS6r-i7wAAAAAAAAAAAAAFl94AQBr')",
    backgroundSize: '100% 100%',
  },
}));

type LoginMode =
  | { kind: 'loading' | 'host' | 'host-retry' | 'subdomain' }
  | { kind: 'tenant'; tenant: NonNullable<StoredTenant> }
  | { kind: 'tenant-error'; message: string };

function readLoginEntry() {
  const params = new URLSearchParams(window.location.search);
  const token = params.get('magicLinkToken');
  const email = params.get('email');
  const tenantName = params.get('tenant')?.trim() || undefined;
  return {
    tenantName,
    retry: params.get('retry') === '1',
    magic: token && email ? { email, token, tenantName } : null,
  };
}

const Login: React.FC = () => {
  const { styles } = useStyles();
  const { message } = App.useApp();
  const subdomainMode = isSubdomainTenantMode();
  const [entry] = useState(readLoginEntry);
  const [mode, setMode] = useState<LoginMode>({ kind: 'loading' });
  const [currentTenant, setCurrentTenant] =
    useState<NonNullable<StoredTenant> | null>(null);
  const [loading, setLoading] = useState(false);
  const [loginError, setLoginError] = useState(false);
  const autoLoginStartedRef = useRef(false);

  const [magic, setMagic] = useState(entry.magic);
  const [magicWorking, setMagicWorking] = useState(!!magic);
  const [magicError, setMagicError] = useState<string | null>(null);
  const [otpCode, setOtpCode] = useState('');
  // 邮件链接凭据只能消费一次，避免 effect 重跑导致重复提交。
  const magicStartedRef = useRef(false);

  useEffect(() => {
    if (magic) {
      return;
    }
    let active = true;

    async function resolveLoginMode() {
      if (subdomainMode) {
        const result = await syncTenantFromSubdomain();
        if (!active) {
          return;
        }
        if (result.mode === 'missing') {
          history.replace('/tenant-not-found');
          return;
        }
        if (result.mode === 'tenant') {
          setCurrentTenant(result.tenant);
        }
        setMode({ kind: 'subdomain' });
        return;
      }

      if (entry.tenantName) {
        try {
          const result = await findTenantByName(entry.tenantName);
          if (!active) {
            return;
          }
          if (!result.success || !result.tenantId || !result.isActive) {
            setMode({ kind: 'tenant-error', message: '租户不存在或未启用' });
            return;
          }
          setMode({
            kind: 'tenant',
            tenant: {
              id: result.tenantId,
              name: result.name || entry.tenantName,
            },
          });
        } catch {
          if (active) {
            setMode({
              kind: 'tenant-error',
              message: '无法验证租户，请刷新后重试',
            });
          }
        }
        return;
      }

      // 裸域名不询问租户。先让出一次，被清理的 effect 不能再发起登录。
      await Promise.resolve();
      if (active) {
        setMode({ kind: entry.retry ? 'host-retry' : 'host' });
      }
    }

    void resolveLoginMode();
    return () => {
      active = false;
    };
  }, [entry.retry, entry.tenantName, magic, subdomainMode]);

  const establishSession = async (
    result: Awaited<ReturnType<typeof loginWithMagicLink>>,
  ) => {
    await applyTokensForNewSession(result);
    message.success('登录成功，正在打开工作台…');
    window.location.replace(DEFAULT_LANDING_PATH);
  };

  const rememberMagicTenant = async (tenantName?: string) => {
    if (!tenantName) {
      // Host 邮件没有租户。留下的旧租户会在换票之后继续附在 __tenant 上。
      setStoredTenant(null);
      return true;
    }

    const result = await findTenantByName(tenantName);
    if (!result.success || !result.tenantId || !result.isActive) {
      return false;
    }

    setStoredTenant({
      id: result.tenantId,
      name: result.name || tenantName,
    });
    return true;
  };

  const consumeMagicLink = async (
    email: string,
    token: string,
    tenantName?: string,
  ) => {
    setMagicWorking(true);
    setMagicError(null);
    try {
      if (!(await rememberMagicTenant(tenantName))) {
        setMagicError('链接对应的租户不存在或未启用');
        return;
      }
      const result = await loginWithMagicLink({
        email,
        magicLinkToken: token,
        tenantName,
      });
      await establishSession(result);
    } catch {
      console.warn('[auth] 邮件链接登录失败');
      setMagicError('无法使用此链接登录，可改用邮件中的验证码');
    } finally {
      setMagicWorking(false);
    }
  };

  const consumeOtp = async () => {
    if (!magic || !otpCode.trim()) {
      return;
    }
    setMagicWorking(true);
    setMagicError(null);
    try {
      if (!(await rememberMagicTenant(magic.tenantName))) {
        setMagicError('链接对应的租户不存在或未启用');
        return;
      }
      const result = await loginWithMagicLink({
        email: magic.email,
        code: otpCode.trim(),
        tenantName: magic.tenantName,
      });
      await establishSession(result);
    } catch {
      console.warn('[auth] 邮件验证码登录失败');
      setMagicError('验证码登录失败，请检查验证码或稍后重试');
    } finally {
      setMagicWorking(false);
    }
  };

  useEffect(() => {
    if (!magic || magicStartedRef.current) {
      return;
    }
    magicStartedRef.current = true;
    // 从地址栏去掉一次性凭据。租户留下，刷新后仍进这封邮件的租户，而不是 Host。
    const next = new URLSearchParams();
    if (magic.tenantName) {
      next.set('tenant', magic.tenantName);
    }
    const search = next.toString();
    window.history.replaceState(
      null,
      '',
      search ? `/user/login?${search}` : '/user/login',
    );
    void consumeMagicLink(magic.email, magic.token, magic.tenantName);
  }, [magic]);

  const automaticLogin = mode.kind === 'host' || mode.kind === 'tenant';
  const directLogin = automaticLogin || mode.kind === 'host-retry';

  const login = useCallback(async () => {
    setLoginError(false);
    setLoading(true);
    try {
      if (mode.kind === 'host' || mode.kind === 'host-retry') {
        setStoredTenant(null);
        await startLogin({ tenant: null, replace: directLogin });
      } else if (mode.kind === 'tenant') {
        setStoredTenant(mode.tenant);
        await startLogin({ tenant: mode.tenant, replace: true });
      } else if (currentTenant) {
        setStoredTenant(currentTenant);
        await startLogin({ tenant: currentTenant });
      } else {
        setStoredTenant(null);
        await startLogin({ tenant: null });
      }
    } catch {
      setLoading(false);
      console.warn('[auth] 无法发起 OpenIddict 登录');
      if (directLogin) {
        setLoginError(true);
      } else {
        message.error('无法打开登录页，请稍后重试');
      }
    }
  }, [mode, currentTenant, directLogin, message]);

  useEffect(() => {
    if (automaticLogin && !autoLoginStartedRef.current) {
      autoLoginStartedRef.current = true;
      void login();
    }
  }, [automaticLogin, login]);

  if (magic) {
    return (
      <div className={styles.container}>
        <Helmet>
          <title>登录 - {Settings.title}</title>
        </Helmet>
        <div style={{ flex: 1, padding: '64px 16px' }}>
          <Card style={{ maxWidth: 420, margin: '0 auto' }} title="AbpAdmin">
            {magicWorking ? (
              <Space
                orientation="vertical"
                size="middle"
                style={{ width: '100%', textAlign: 'center' }}
              >
                <Spin />
                <Typography.Text type="secondary">
                  正在通过邮件链接登录（{magic.email}）…
                </Typography.Text>
              </Space>
            ) : (
              <Space
                orientation="vertical"
                size="middle"
                style={{ width: '100%' }}
              >
                <Alert
                  type="error"
                  showIcon
                  title={magicError ?? '登录链接无效或已过期'}
                />
                <Typography.Text type="secondary">
                  邮件里还有 6
                  位验证码？输入后可直接登录（与链接共用一次有效凭据）：
                </Typography.Text>
                <Input
                  placeholder="6 位验证码"
                  maxLength={6}
                  value={otpCode}
                  onChange={(e) => setOtpCode(e.target.value)}
                  onPressEnter={consumeOtp}
                  inputMode="numeric"
                  pattern="[0-9]*"
                />
                <Button
                  type="primary"
                  block
                  loading={magicWorking}
                  disabled={!otpCode.trim()}
                  onClick={consumeOtp}
                >
                  用验证码登录
                </Button>
                <Button block onClick={() => setMagic(null)}>
                  返回账号密码登录
                </Button>
              </Space>
            )}
          </Card>
        </div>
      </div>
    );
  }

  if (mode.kind !== 'subdomain') {
    const tenantError = mode.kind === 'tenant-error' ? mode.message : null;
    const showRetry = loginError || (mode.kind === 'host-retry' && !loading);
    return (
      <div className={styles.container}>
        <Helmet>
          <title>登录 - {Settings.title}</title>
        </Helmet>
        <div style={{ flex: 1, padding: '64px 16px' }}>
          <Card style={{ maxWidth: 420, margin: '0 auto' }} title="AbpAdmin">
            <Space
              orientation="vertical"
              size="middle"
              style={{ width: '100%' }}
            >
              {!tenantError && !showRetry && (
                <Space role="status">
                  <Spin />
                  <Typography.Text>正在打开登录页…</Typography.Text>
                </Space>
              )}
              {tenantError && (
                <>
                  <Alert type="error" showIcon title={tenantError} />
                  <Button block href="/user/login">
                    返回登录
                  </Button>
                </>
              )}
              {loginError && (
                <Alert type="error" showIcon title="无法打开登录页" />
              )}
              {showRetry && (
                <Button type="primary" block onClick={login}>
                  重试登录
                </Button>
              )}
            </Space>
          </Card>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Helmet>
        <title>登录 - {Settings.title}</title>
      </Helmet>
      <div style={{ flex: 1, padding: '64px 16px' }}>
        <Card
          style={{ maxWidth: 420, margin: '0 auto' }}
          title="AbpAdmin"
          extra={
            <Button type="link" onClick={() => history.push('/')}>
              返回
            </Button>
          }
        >
          <Typography.Paragraph type="secondary">
            使用 ABP OpenIddict 授权码和 PKCE 登录。 也可用子域名访问，例如 demo
            租户：
            {window.location.hostname.startsWith('demo.')
              ? window.location.hostname
              : `demo.${window.location.hostname}`}
            {window.location.port ? `:${window.location.port}` : ''}
          </Typography.Paragraph>
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            <Typography.Text>
              当前上下文：{currentTenant ? currentTenant.name : 'Host'}
              {subdomainMode ? '（子域名）' : ''}
            </Typography.Text>
            <Button
              type="primary"
              block
              loading={loading}
              onClick={login}
              data-testid="abp-login"
            >
              使用 ABP 账号登录
            </Button>
          </Space>
        </Card>
      </div>
    </div>
  );
};

export default Login;
