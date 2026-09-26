import { App, Button, Input, Space, Tag, Typography } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  confirmEmail,
  confirmPhoneNumber,
  getMyProfile,
  getTwoFactorStatus,
  type ProfileDto,
  sendEmailConfirmationCode,
  sendPhoneNumberConfirmationCode,
  type TwoFactorStatusDto,
} from '@/abp/account';
import { useAsyncData } from '@/hooks/useAsyncData';

/** 邮箱/手机两条确认通道各自独立的状态：输入码 + 互不阻塞的两个请求位 */
type ChannelState = {
  code: string;
  sending: boolean;
  confirming: boolean;
};

const initialChannel: ChannelState = {
  code: '',
  sending: false,
  confirming: false,
};

/**
 * 联系方式确认面板：改邮箱/手机号保存后，确认状态会被后端重置为未确认
 * （Identity SetEmailAsync/SetPhoneNumberAsync 语义），面板对未确认的联系方式
 * 提供「发送验证码 → 输入 → 确认」闭环。未确认的邮箱不能使用免密登录、
 * 不能接收双因素验证码，看到徽标应尽快确认。
 *
 * refreshKey 变化（资料保存后由父组件递增）时重新拉取状态。
 */
const ContactConfirmSection: React.FC<{ refreshKey?: number }> = ({
  refreshKey = 0,
}) => {
  const { message } = App.useApp();
  const { data, refresh } = useAsyncData(async () => {
    const [profile, status] = await Promise.all([
      getMyProfile(),
      getTwoFactorStatus(),
    ]);
    return { profile, status };
  });
  const [emailChannel, setEmailChannel] =
    useState<ChannelState>(initialChannel);
  const [phoneChannel, setPhoneChannel] =
    useState<ChannelState>(initialChannel);

  useEffect(() => {
    if (refreshKey > 0) {
      void refresh();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  const profile: ProfileDto | undefined = data?.profile;
  const status: TwoFactorStatusDto | undefined = data?.status;

  // 两个通道（邮箱/手机）共用一套状态机（审查轮去重）：send 阶段与 confirm 阶段各一份实现
  const runSend = useCallback(
    async (
      key: 'email' | 'phone',
      value: string | undefined,
      send: (v: string) => Promise<unknown>,
      okText: string,
    ) => {
      if (!value) {
        return;
      }
      const setChannel = key === 'email' ? setEmailChannel : setPhoneChannel;
      setChannel((s) => ({ ...s, sending: true }));
      try {
        await send(value);
        message.success(okText);
      } catch {
        // 错误由全局 errorHandler 统一提示（限流等）
      } finally {
        setChannel((s) => ({ ...s, sending: false }));
      }
    },
    [message],
  );

  const runConfirm = useCallback(
    async (
      key: 'email' | 'phone',
      value: string | undefined,
      code: string,
      confirm: (v: string, c: string) => Promise<unknown>,
      okText: string,
    ) => {
      if (!value || !code.trim()) {
        return;
      }
      const setChannel = key === 'email' ? setEmailChannel : setPhoneChannel;
      setChannel((s) => ({ ...s, confirming: true }));
      try {
        await confirm(value, code.trim());
        message.success(okText);
        setChannel(initialChannel);
        await refresh();
      } catch {
        // 错误由全局 errorHandler 统一提示（验证码无效等）
      } finally {
        setChannel((s) => ({ ...s, confirming: false }));
      }
    },
    [refresh, message],
  );

  const sendEmailCode = () =>
    runSend(
      'email',
      profile?.email,
      sendEmailConfirmationCode,
      '确认码已发送，请查收邮箱',
    );
  const submitEmailCode = () =>
    runConfirm(
      'email',
      profile?.email,
      emailChannel.code,
      confirmEmail,
      '邮箱已确认',
    );
  const sendPhoneCode = () =>
    runSend(
      'phone',
      profile?.phoneNumber,
      sendPhoneNumberConfirmationCode,
      '确认码已发送，请查收短信',
    );
  const submitPhoneCode = () =>
    runConfirm(
      'phone',
      profile?.phoneNumber,
      phoneChannel.code,
      confirmPhoneNumber,
      '手机号已确认',
    );

  // 操作区（发码/确认输入框）只在严格 === false 时出现：status 尚未加载（undefined）
  // 不得把未知当未确认而诱导重复确认。徽标是另一档：confirmed 非 true（含未加载）都显示
  // 「未确认」——保守提示，但不给操作入口
  const emailUnconfirmed = status?.emailConfirmed === false && !!profile?.email;
  const phoneUnconfirmed =
    status?.phoneNumberConfirmed === false && !!profile?.phoneNumber;
  const hasAnythingToShow = !!profile?.email || !!profile?.phoneNumber;

  if (!hasAnythingToShow) {
    return null;
  }

  const renderChannel = (
    label: string,
    value: string | undefined,
    confirmed: boolean | undefined,
    unconfirmed: boolean,
    channel: ChannelState,
    onCodeChange: (code: string) => void,
    onSend: () => void,
    onSubmit: () => void,
  ) => (
    <div>
      <Space wrap>
        <Typography.Text>
          {label}：{value || '未设置'}
        </Typography.Text>
        {value ? (
          confirmed ? (
            <Tag color="success">已确认</Tag>
          ) : (
            <Tag color="warning">未确认</Tag>
          )
        ) : null}
      </Space>
      {unconfirmed && (
        <Space wrap style={{ marginTop: 8 }}>
          <Input
            placeholder="6 位确认码"
            value={channel.code}
            onChange={(e) => onCodeChange(e.target.value)}
            onPressEnter={onSubmit}
            maxLength={8}
            style={{ width: 140 }}
          />
          <Button size="small" onClick={onSend} loading={channel.sending}>
            发送验证码
          </Button>
          <Button
            size="small"
            type="primary"
            onClick={onSubmit}
            loading={channel.confirming}
            disabled={!channel.code.trim()}
          >
            确认
          </Button>
        </Space>
      )}
    </div>
  );

  return (
    <Space orientation="vertical" size="small" style={{ width: '100%' }}>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        修改邮箱或手机号后需要重新确认；未确认的邮箱不能使用免密登录，也不能接收双因素验证码。
      </Typography.Paragraph>
      {renderChannel(
        '邮箱',
        profile?.email,
        status?.emailConfirmed,
        emailUnconfirmed,
        emailChannel,
        (code) => setEmailChannel((s) => ({ ...s, code })),
        sendEmailCode,
        submitEmailCode,
      )}
      {renderChannel(
        '手机号',
        profile?.phoneNumber,
        status?.phoneNumberConfirmed,
        phoneUnconfirmed,
        phoneChannel,
        (code) => setPhoneChannel((s) => ({ ...s, code })),
        sendPhoneCode,
        submitPhoneCode,
      )}
    </Space>
  );
};

export default ContactConfirmSection;
