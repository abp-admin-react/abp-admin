/** 登录入口负责选择租户、发起 OIDC 登录及处理邮件链接和验证码。 */
import { Helmet, history } from '@umijs/max';
import { Alert, App, Button, Card, Input, Space, Spin, Typography } from 'antd';
import { createStyles } from 'antd-style';
import React, { useEffect, useRef, useState } from 'react';
import { loginWithMagicLink } from '@/abp/account';
import { findTenantByName } from '@/abp/config';
import { applyTokensForNewSession, startLogin } from '@/abp/oidc';
import { hasRealTimeConnection, restartRealTime } from '@/abp/signalr';
import { syncTenantFromSubdomain } from '@/abp/subdomain';
import {
  getStoredTenant,
  getTenantNameFromHost,
  isSubdomainTenantMode,
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

const Login: React.FC = () => {
  const { styles } = useStyles();
  const { message } = App.useApp();
  const subdomainMode = isSubdomainTenantMode();
  const [tenantReady, setTenantReady] = useState(!subdomainMode);
  const [tenantName, setTenantName] = useState(
    () => getTenantNameFromHost() || getStoredTenant()?.name || '',
  );
  const [currentTenant, setCurrentTenant] = useState(() => getStoredTenant());
  const [loading, setLoading] = useState(false);

  const [magic, setMagic] = useState<{
    email: string;
    token: string;
    tenantName?: string;
  } | null>(null);
  const [magicWorking, setMagicWorking] = useState(false);
  const [magicError, setMagicError] = useState<string | null>(null);
  const [otpCode, setOtpCode] = useState('');
  // 邮件链接凭据只能消费一次，避免 effect 重跑导致重复提交。
  const magicStartedRef = useRef(false);

  useEffect(() => {
    if (!subdomainMode) {
      return;
    }
    syncTenantFromSubdomain().then((result) => {
      if (result.mode === 'missing') {
        history.replace('/tenant-not-found');
        return;
      }
      if (result.mode === 'tenant') {
        setCurrentTenant(result.tenant);
        setTenantName(result.tenant.name);
      }
      setTenantReady(true);
    });
  }, [subdomainMode]);

  // 切换租户时重连已存在的 SignalR 连接，避免沿用旧租户上下文。
  const reconnectRealTimeAfterTenantSwitch = () => {
    if (hasRealTimeConnection()) {
      void restartRealTime();
    }
  };

  const establishSession = async (
    result: Awaited<ReturnType<typeof loginWithMagicLink>>,
  ) => {
    await applyTokensForNewSession(result);
    message.success('登录成功，正在打开工作台…');
    window.location.replace(DEFAULT_LANDING_PATH);
  };

  const consumeMagicLink = async (
    email: string,
    token: string,
    tenantName?: string,
  ) => {
    setMagicWorking(true);
    setMagicError(null);
    try {
      if (tenantName) {
        // 换票请求使用本地租户状态生成 __tenant，先写入邮件指定的租户。
        const result = await findTenantByName(tenantName);
        if (!result.success || !result.tenantId || !result.isActive) {
          setMagicError('链接对应的租户不存在或未启用');
          return;
        }
        setStoredTenant({
          id: result.tenantId,
          name: result.name || tenantName,
        });
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
    if (magicStartedRef.current) {
      return;
    }
    const params = new URLSearchParams(window.location.search);
    const token = params.get('magicLinkToken');
    const email = params.get('email');
    if (!token || !email) {
      return;
    }
    magicStartedRef.current = true;
    const tenantName = params.get('tenant') || undefined;
    // 从浏览器历史移除一次性凭据，再发起换票。
    window.history.replaceState(null, '', '/user/login');
    setMagic({ email, token, tenantName });
    void consumeMagicLink(email, token, tenantName);
  }, []);

  const switchTenant = async () => {
    const name = tenantName.trim();
    if (!name) {
      setStoredTenant(null);
      setCurrentTenant(null);
      reconnectRealTimeAfterTenantSwitch();
      message.success('已切换到 Host');
      return;
    }
    try {
      const result = await findTenantByName(name);
      if (!result.success || !result.tenantId || !result.isActive) {
        message.error('租户不存在或未启用');
        return;
      }
      const tenant = { id: result.tenantId, name: result.name || name };
      setStoredTenant(tenant);
      setCurrentTenant(tenant);
      reconnectRealTimeAfterTenantSwitch();
      message.success(`当前租户：${tenant.name}`);
    } catch {
      message.error('查找租户失败，请确认后端已启动');
    }
  };

  const login = async () => {
    setLoading(true);
    try {
      await startLogin();
    } catch {
      setLoading(false);
      console.warn('[auth] 无法发起 OpenIddict 登录');
      message.error('无法打开登录页，请稍后重试');
    }
  };

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
            使用 ABP OpenIddict 授权码和 PKCE 登录。
            也可用子域名访问，例如 demo 租户：
            {window.location.hostname.startsWith('demo.')
              ? window.location.hostname
              : `demo.${window.location.hostname}`}
            {window.location.port ? `:${window.location.port}` : ''}
          </Typography.Paragraph>
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            {!subdomainMode && (
              <>
                <Input
                  placeholder="租户名称，留空表示 Host"
                  value={tenantName}
                  onChange={(e) => setTenantName(e.target.value)}
                  onPressEnter={switchTenant}
                />
                <Button block onClick={switchTenant}>
                  切换租户
                </Button>
              </>
            )}
            <Typography.Text>
              当前上下文：{currentTenant ? currentTenant.name : 'Host'}
              {subdomainMode ? '（子域名）' : ''}
            </Typography.Text>
            <Button
              type="primary"
              block
              loading={loading}
              disabled={subdomainMode && !tenantReady}
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
