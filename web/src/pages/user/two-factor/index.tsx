import { Helmet, history, useModel } from '@umijs/max';
import { App, Button, Card, Input, Space, Typography } from 'antd';
import { createStyles } from 'antd-style';
import React, { useEffect, useState } from 'react';
import {
  getTwoFactorStatus,
  sendTwoFactorCode,
  verifyTwoFactorCode,
} from '@/abp/account';
import Settings from '../../../../config/defaultSettings';

const useStyles = createStyles(() => ({
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

/**
 * T2.7 双因素验码页（React 版）。
 *
 * 注意：该页**不在 OIDC 登录流程内**。前端登录是后端 MVC 页（Volo.Abp.Account.Web.OpenIddict），
 * 双因素第二步由后端 `Pages/Account/TwoFactorVerification`（MVC）完成——开源包的
 * `LoginModel.TwoFactorLoginResultAsync` 直接抛 NotImplementedException，已由替换的
 * `AbpAdminLoginModel` 覆写跳转（验证结论见 docs/refactor/03-batch2-pro-parity.md T2.7 末节）。
 * 本页假定用户已登录并调用 AppService 的双因素端点，可作为将来「登录后再验证」场景的底子，
 * 当前登录流程不会跳转到此页。
 */
const TwoFactorVerification: React.FC = () => {
  const { styles } = useStyles();
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const [countdown, setCountdown] = useState(0);
  const [provider, setProvider] = useState<'Email' | 'Phone'>('Email');

  // initialState.currentUser 统一使用 userid 命名（见 app.tsx），不再用 as any 取不存在的 id 字段
  const currentUser = initialState?.currentUser;
  const userId = currentUser?.userid;

  useEffect(() => {
    // 检查用户是否已登录
    if (!userId) {
      message.error('请先登录');
      history.replace('/user/login');
      return;
    }

    // 检查用户是否启用了 2FA
    getTwoFactorStatus()
      .then((status) => {
        if (!status.twoFactorEnabled) {
          // 未启用 2FA，直接跳转到首页
          message.info('您未启用双因素认证');
          history.replace('/');
          return;
        }

        // 自动选择可用的验证方式
        if (status.emailConfirmed) {
          setProvider('Email');
        } else if (status.phoneNumberConfirmed) {
          setProvider('Phone');
        } else {
          message.error('您未确认邮箱或手机号，无法使用双因素认证');
          history.replace('/');
        }
      })
      .catch(() => {
        message.error('获取双因素认证状态失败');
        history.replace('/');
      });
  }, [userId, message]);

  useEffect(() => {
    if (countdown > 0) {
      const timer = setTimeout(() => setCountdown(countdown - 1), 1000);
      return () => clearTimeout(timer);
    }
    return undefined;
  }, [countdown]);

  const handleSendCode = async () => {
    if (!userId) return;

    setSending(true);
    try {
      // 收件人由服务端取当前登录用户，入参只传通道
      await sendTwoFactorCode({ provider });
      message.success(
        `验证码已发送到您的${provider === 'Email' ? '邮箱' : '手机'}`,
      );
      setCountdown(60);
    } catch {
      // 错误由全局 errorHandler 统一提示
    } finally {
      setSending(false);
    }
  };

  const handleVerify = async () => {
    if (!userId || !code) {
      message.error('请输入验证码');
      return;
    }

    setLoading(true);
    try {
      await verifyTwoFactorCode({ code, provider });
      message.success('验证成功');

      // 跳转到原来的目标页面或首页
      const redirect = sessionStorage.getItem('abp.redirect') || '/';
      sessionStorage.removeItem('abp.redirect');
      history.replace(redirect);
    } catch {
      // 错误由全局 errorHandler 统一提示
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className={styles.container}>
      <Helmet>
        <title>双因素认证 - {Settings.title}</title>
      </Helmet>
      <div style={{ flex: 1, padding: '64px 16px' }}>
        <Card style={{ maxWidth: 420, margin: '0 auto' }} title="双因素认证">
          <Typography.Paragraph type="secondary">
            您的账户已启用双因素认证，请输入发送到您
            {provider === 'Email' ? '邮箱' : '手机'}
            的验证码。
          </Typography.Paragraph>
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            <Button
              block
              loading={sending}
              disabled={countdown > 0}
              onClick={handleSendCode}
            >
              {countdown > 0 ? `重新发送 (${countdown}s)` : '发送验证码'}
            </Button>
            <Input
              placeholder="请输入 6 位验证码"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              onPressEnter={handleVerify}
              maxLength={6}
              size="large"
            />
            <Button
              type="primary"
              block
              loading={loading}
              disabled={!code || code.length !== 6}
              onClick={handleVerify}
            >
              验证
            </Button>
            <Button
              type="link"
              block
              onClick={() => history.push('/user/login')}
            >
              返回登录
            </Button>
          </Space>
        </Card>
      </div>
    </div>
  );
};

export default TwoFactorVerification;
