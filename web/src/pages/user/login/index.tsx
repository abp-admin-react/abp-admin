import { Helmet, history } from '@umijs/max';
import { Alert, App, Button, Card, Input, Space, Spin, Typography } from 'antd';
import { createStyles } from 'antd-style';
import React, { useEffect, useRef, useState } from 'react';
import { loginWithMagicLink } from '@/abp/account';
import { findTenantByName } from '@/abp/config';
import { applyTokensForNewSession, startLogin } from '@/abp/oidc';
import { isRealTimeAvailable, restartRealTime } from '@/abp/signalr';
import { syncTenantFromSubdomain } from '@/abp/subdomain';
import {
  getStoredTenant,
  getTenantNameFromHost,
  isSubdomainTenantMode,
  setStoredTenant,
} from '@/abp/tenant';
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

  // ========== Magic Link 免密登录落地 ==========
  // 邮件链接形如 /user/login?magicLinkToken=...&email=...[&tenant=...]，
  // 检测到参数即进入落地状态机：自动解析租户 → 服务端消费凭据换令牌 → 建立会话跳转。
  // 链接失效时展示兜底验证码输入（邮件正文同时含 6 位 OTP，与链接共享一次性凭据记录）。
  const [magic, setMagic] = useState<{
    email: string;
    token: string;
    tenantName?: string;
  } | null>(null);
  const [magicWorking, setMagicWorking] = useState(false);
  const [magicError, setMagicError] = useState<string | null>(null);
  const [otpCode, setOtpCode] = useState('');
  // 一次性守卫：防止 effect 重跑导致同一凭据被消费两次（一次性 token 消费后第二次必失败）
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

  // T3.2：租户信息在 access token 的 claim 里，切租户后旧连接持有的还是
  // 旧租户上下文，必须断开重连。登录页通常尚未登录（无连接），
  // 仅在已有实时连接时重建，避免未登录时多一次注定 401 的尝试。
  const reconnectRealTimeAfterTenantSwitch = () => {
    if (isRealTimeAvailable()) {
      void restartRealTime({});
    }
  };

  // 「换票 → 建会话 → 跳转」的公共尾段（审查轮去重：魔法链接与 OTP 兜底两条路径共用）
  const establishSession = async (
    result: Awaited<ReturnType<typeof loginWithMagicLink>>,
  ) => {
    await applyTokensForNewSession(result);
    message.success('登录成功，正在进入系统…');
    window.location.replace('/administration');
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
        // 链接自带租户：先解析并写入本地租户状态，后续请求的 __tenant 头与服务端租户上下文一致
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
    } catch (error) {
      console.error('magic link 登录失败', error);
      setMagicError('登录链接无效或已过期，可改用邮件中的验证码登录');
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
    } catch (error) {
      console.error('OTP 登录失败', error);
      setMagicError('验证码无效或已过期');
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
    // 一次性凭据已在本地变量中：立即从地址栏/历史记录剥离（凭据是 bearer 性质，
    // 留在 URL 里会被历史记录、截图、分享泄露；失败态保留 OTP 兜底即可）
    window.history.replaceState(null, '', '/user/login');
    setMagic({ email, token, tenantName });
    void consumeMagicLink(email, token, tenantName);
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
      const redirect = new URLSearchParams(window.location.search).get(
        'redirect',
      );
      // 只接受站内相对路径：以 / 开头且非协议相对（//evil.com），
      // 防止登录后被 query 里的绝对 URL 带去钓鱼站（post-auth open redirect）
      if (redirect?.startsWith('/') && !redirect.startsWith('//')) {
        sessionStorage.setItem('abp.redirect', redirect);
      }
      await startLogin();
    } catch (error) {
      setLoading(false);
      const detail = error instanceof Error ? error.message : String(error);
      message.error(`无法跳转到 ABP 登录页：${detail}`);
      console.error(error);
    }
  };

  // Magic Link 落地视图：链接参数存在时接管整个页面（进行中 / 失败兜底两种形态）
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
                  message={magicError ?? '登录链接无效或已过期'}
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
            使用 ABP OpenIddict 授权码 + PKCE 登录。默认账号 admin / 1q2w3E*。
            也可用子域名访问，例如 http://demo.localhost:8000
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
