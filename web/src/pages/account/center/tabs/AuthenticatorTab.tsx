import { App, Button, Input, Space, Typography } from 'antd';
import React, { useState } from 'react';
import {
  disableAuthenticator,
  enableAuthenticator,
  getAuthenticatorStatus,
  resetAuthenticatorKey,
} from '@/abp/accountSecurity';
import { useAsyncData } from '@/hooks/useAsyncData';

/** Authenticator Tab：TOTP 密钥管理与启用/关闭（自 index.tsx 拆分，见重构报告问题 4） */
const AuthenticatorTab: React.FC = () => {
  const { message } = App.useApp();
  const {
    data: status,
    loading,
    refresh,
  } = useAsyncData(() => getAuthenticatorStatus());
  const [sharedKey, setSharedKey] = useState<string>();
  const [uri, setUri] = useState<string>();
  const [codes, setCodes] = useState<string[]>([]);
  const [code, setCode] = useState('');

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Paragraph type="secondary">
        使用 Microsoft Authenticator / Google Authenticator 扫描密钥后输入 6
        位验证码启用。
      </Typography.Paragraph>
      <Space>
        <Button
          loading={loading}
          onClick={async () => {
            const result = await resetAuthenticatorKey();
            setSharedKey(result.sharedKey);
            setUri(result.authenticatorUri);
            message.success('已生成新密钥');
            await refresh();
          }}
        >
          生成密钥
        </Button>
        <Typography.Text>
          {status?.enabled ? 'Authenticator 已启用' : '未启用'}
        </Typography.Text>
      </Space>
      {sharedKey && (
        <>
          <Typography.Text copyable>{sharedKey}</Typography.Text>
          <Typography.Text type="secondary">{uri}</Typography.Text>
        </>
      )}
      <Input
        placeholder="6 位验证码"
        value={code}
        onChange={(e) => setCode(e.target.value)}
        style={{ maxWidth: 240 }}
      />
      <Space>
        <Button
          type="primary"
          onClick={async () => {
            const result = await enableAuthenticator(code);
            setCodes(result.recoveryCodes ?? []);
            message.success('Authenticator 已启用，请保存恢复码');
            setCode('');
            await refresh();
          }}
        >
          启用
        </Button>
        <Button
          danger
          onClick={async () => {
            await disableAuthenticator(code);
            setCodes([]);
            message.success('已关闭 Authenticator');
            setCode('');
            await refresh();
          }}
        >
          关闭
        </Button>
      </Space>
      {codes.length > 0 && (
        <>
          <Typography.Paragraph>
            恢复码（只显示一次）：{codes.join('  ')}
          </Typography.Paragraph>
          <Typography.Paragraph type="warning" style={{ marginBottom: 0 }}>
            每个恢复码只能使用一次。验证器应用不可用时，可在登录页的双因素验证步骤选择「使用恢复码登录」。
          </Typography.Paragraph>
        </>
      )}
    </Space>
  );
};

export default AuthenticatorTab;
