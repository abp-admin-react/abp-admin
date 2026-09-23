import { App, Button, Input, Modal, Space, Switch, Tag, Typography } from 'antd';
import React, { useState } from 'react';
import {
  getTwoFactorStatus,
  sendTwoFactorCode,
  setTwoFactorEnabled,
  type TwoFactorStatusDto,
} from '@/abp/account';
import { useAsyncData } from '@/hooks/useAsyncData';

/** 安全设置 Tab：双因素认证开关（自 index.tsx 拆分，见重构报告问题 4） */
const SecuritySettingsTab: React.FC = () => {
  const { message } = App.useApp();
  const {
    data: status,
    loading,
    refresh,
  } = useAsyncData<TwoFactorStatusDto>(() => getTwoFactorStatus());

  // 关闭 2FA 需先验证一次验证码（后端 SetTwoFactorEnabledAsync(enabled=false) 强校验）
  const [disableModalOpen, setDisableModalOpen] = useState(false);
  const [disableCode, setDisableCode] = useState('');
  const [disableLoading, setDisableLoading] = useState(false);
  const [codeSending, setCodeSending] = useState(false);

  const handleToggle = async (enabled: boolean) => {
    if (enabled) {
      try {
        await setTwoFactorEnabled(true);
        message.success('双因素认证已启用');
        await refresh();
      } catch {
        // 错误由全局 errorHandler 统一提示
      }
      return;
    }
    // 关闭：弹窗收集验证码，不直接关
    setDisableCode('');
    setDisableModalOpen(true);
  };

  const sendDisableCode = async () => {
    setCodeSending(true);
    try {
      await sendTwoFactorCode({});
      message.success('验证码已发送，请查收');
    } catch {
      // 错误由全局 errorHandler 统一提示
    } finally {
      setCodeSending(false);
    }
  };

  const confirmDisable = async () => {
    if (!disableCode.trim()) {
      return;
    }
    setDisableLoading(true);
    try {
      await setTwoFactorEnabled(false, disableCode.trim());
      message.success('双因素认证已禁用');
      setDisableModalOpen(false);
      await refresh();
    } catch {
      // 错误由全局 errorHandler 统一提示（验证码错误/限流）
    } finally {
      setDisableLoading(false);
    }
  };

  const canEnable = status?.emailConfirmed || status?.phoneNumberConfirmed;

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Title level={5}>双因素认证</Typography.Title>
      <Typography.Paragraph type="secondary">
        启用双因素认证后，登录时需要额外输入发送到您邮箱或手机号的验证码。关闭时需要验证一次验证码。
      </Typography.Paragraph>
      <Space>
        <Switch
          checked={status?.twoFactorEnabled ?? false}
          loading={loading}
          disabled={!canEnable && !status?.twoFactorEnabled}
          onChange={handleToggle}
        />
        <Typography.Text>
          {status?.twoFactorEnabled ? '已启用' : '已禁用'}
        </Typography.Text>
      </Space>
      {!canEnable && !status?.twoFactorEnabled && (
        <Typography.Text type="warning">
          启用双因素认证前，请先确认您的邮箱或手机号。
        </Typography.Text>
      )}
      <Space orientation="vertical" size="small">
        <Typography.Text>
          邮箱验证状态：
          {status?.emailConfirmed ? (
            <Tag color="success">已确认</Tag>
          ) : (
            <Tag color="warning">未确认</Tag>
          )}
        </Typography.Text>
        <Typography.Text>
          手机号验证状态：
          {status?.phoneNumberConfirmed ? (
            <Tag color="success">已确认</Tag>
          ) : (
            <Tag color="warning">未确认</Tag>
          )}
        </Typography.Text>
      </Space>

      <Modal
        title="关闭双因素认证"
        open={disableModalOpen}
        onCancel={() => setDisableModalOpen(false)}
        footer={null}
      >
        <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            关闭双因素认证会降低账号安全性。已确认邮箱/手机号的用户请点击「发送验证码」并输入收到的验证码；
            仅启用验证器的用户可直接输入验证器应用中的 6 位验证码。
          </Typography.Paragraph>
          <Space>
            <Input
              placeholder="6 位验证码"
              value={disableCode}
              onChange={(e) => setDisableCode(e.target.value)}
              onPressEnter={confirmDisable}
              style={{ width: 200 }}
              maxLength={8}
            />
            {(status?.emailConfirmed || status?.phoneNumberConfirmed) && (
              <Button onClick={sendDisableCode} loading={codeSending}>
                发送验证码
              </Button>
            )}
          </Space>
          <Button
            type="primary"
            danger
            block
            loading={disableLoading}
            disabled={!disableCode.trim()}
            onClick={confirmDisable}
          >
            确认关闭
          </Button>
        </Space>
      </Modal>
    </Space>
  );
};

export default SecuritySettingsTab;
