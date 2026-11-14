import {
  PageContainer,
  ProCard,
  ProForm,
  ProFormDigit,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Collapse, Empty, Spin } from 'antd';
import React, { useCallback, useEffect, useId, useState } from 'react';
import {
  getEmailSettings,
  getTimezone,
  getTimezones,
  sendTestEmail,
  updateEmailSettings,
  updateTimezone,
} from '@/abp/settings';
import { getSettingGroups } from '@/abp/settingUi';
import {
  dedicatedSettingGroupNames,
  tenantGeneralSettingGroupNames,
} from './channelGroups';
import SettingGroupPanel from './SettingGroupPanel';

const SettingsPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const formId = useId();

  // SettingUi 通用设置分组（T1.4）
  const [settingGroups, setSettingGroups] = useState<API.SettingGroup[]>([]);
  const [loadingGroups, setLoadingGroups] = useState(false);

  const loadSettingGroups = useCallback(async () => {
    if (!access.canManageSettings) return;
    setLoadingGroups(true);
    try {
      const groups = await getSettingGroups();
      setSettingGroups(
        (groups || []).filter((group) => {
          const name = group.groupName ?? '';
          return (
            !dedicatedSettingGroupNames.has(name) ||
            (!access.canManagePaymentSettings &&
              tenantGeneralSettingGroupNames.has(name))
          );
        }),
      );
    } catch (e: any) {
      message.error(e?.message || '加载设置失败');
    } finally {
      setLoadingGroups(false);
    }
  }, [access.canManageSettings, access.canManagePaymentSettings, message]);

  useEffect(() => {
    loadSettingGroups();
  }, [loadSettingGroups]);

  // SettingUi 通用渲染区：每个 SettingGroup 一个一级 Tab
  const settingUiItems = settingGroups.map((group) => ({
    key: `settingui-${group.groupName}`,
    label: group.groupDisplayName || group.groupName,
    children: <SettingGroupPanel group={group} onChanged={loadSettingGroups} />,
  }));

  // ABP 标准邮件与时区设置；语言统一在语言管理页维护。
  const customItems = [
    access.canManageEmailing
      ? {
          key: 'email',
          label: '邮件',
          children: (
            <>
              <ProForm
                formKey={`${formId}-email`}
                request={getEmailSettings}
                submitter={{
                  searchConfig: { submitText: '保存邮件设置' },
                  resetButtonProps: { style: { display: 'none' } },
                }}
                onFinish={async (values) => {
                  await updateEmailSettings(values);
                  message.success('邮件设置已保存');
                  return true;
                }}
              >
                <ProCard ghost gutter={[0, 12]} direction="column">
                  <ProCard
                    title="发件人"
                    size="small"
                    headerBordered
                    colSpan={24}
                  >
                    <ProFormText
                      name="defaultFromDisplayName"
                      label="默认发件人名称"
                      rules={[{ required: true, message: '请输入发件人名称' }]}
                    />
                    <ProFormText
                      name="defaultFromAddress"
                      label="默认发件人邮箱"
                      rules={[
                        { required: true, message: '请输入发件人邮箱' },
                        { type: 'email', message: '邮箱格式不正确' },
                      ]}
                    />
                  </ProCard>
                  <ProCard
                    title="SMTP"
                    size="small"
                    headerBordered
                    colSpan={24}
                  >
                    <ProFormText name="smtpHost" label="主机" />
                    <ProFormDigit
                      name="smtpPort"
                      label="端口"
                      min={1}
                      max={65535}
                    />
                    <ProFormSwitch name="smtpEnableSsl" label="启用 SSL" />
                    <ProFormSwitch
                      name="smtpUseDefaultCredentials"
                      label="使用默认凭据"
                    />
                    <ProFormText name="smtpDomain" label="域" />
                    <ProFormText name="smtpUserName" label="用户名" />
                    <ProFormText.Password
                      name="smtpPassword"
                      label="密码"
                      placeholder="留空表示不修改已保存密码"
                    />
                  </ProCard>
                </ProCard>
              </ProForm>
              {access.canManageEmailingTest ? (
                <Collapse
                  items={[
                    {
                      key: 'test-email',
                      label: '发送测试邮件',
                      children: (
                        <ProForm
                          submitter={{
                            searchConfig: { submitText: '发送测试邮件' },
                            resetButtonProps: { style: { display: 'none' } },
                          }}
                          onFinish={async (values) => {
                            await sendTestEmail({
                              senderEmailAddress: values.senderEmailAddress,
                              targetEmailAddress: values.targetEmailAddress,
                              subject: values.subject,
                              body: values.body,
                            });
                            message.success('测试邮件已发送');
                            return true;
                          }}
                        >
                          <ProFormText
                            name="senderEmailAddress"
                            label="发件人"
                            rules={[
                              { required: true, message: '请输入发件人' },
                              { type: 'email', message: '邮箱格式不正确' },
                            ]}
                          />
                          <ProFormText
                            name="targetEmailAddress"
                            label="收件人"
                            rules={[
                              { required: true, message: '请输入收件人' },
                              { type: 'email', message: '邮箱格式不正确' },
                            ]}
                          />
                          <ProFormText
                            name="subject"
                            label="主题"
                            rules={[{ required: true, message: '请输入主题' }]}
                          />
                          <ProFormTextArea name="body" label="正文" />
                        </ProForm>
                      ),
                    },
                  ]}
                />
              ) : null}
            </>
          ),
        }
      : null,
    access.canManageTimezone
      ? {
          key: 'timezone',
          label: '时区',
          children: (
            <ProForm
              formKey={`${formId}-timezone`}
              request={async () => ({
                timezone: (await getTimezone()) || 'Unspecified',
              })}
              submitter={{
                searchConfig: { submitText: '保存时区' },
                resetButtonProps: { style: { display: 'none' } },
              }}
              onFinish={async (values) => {
                await updateTimezone(values.timezone || 'Unspecified');
                message.success('时区已保存');
                return true;
              }}
            >
              <ProFormSelect
                name="timezone"
                label="时区"
                showSearch
                request={async () => {
                  const items = await getTimezones();
                  return [
                    { label: '未指定（跟随系统）', value: 'Unspecified' },
                    ...(items || []).map((item) => ({
                      label: item.name || item.value,
                      value: item.name || item.value,
                    })),
                  ];
                }}
              />
            </ProForm>
          ),
        }
      : null,
  ].filter(Boolean);

  const items = [...settingUiItems, ...customItems];

  return (
    <PageContainer title="通用设置">
      <Spin spinning={loadingGroups}>
        {items.length ? (
          <ProCard tabs={{ items: items as any }} />
        ) : (
          <ProCard>
            <Empty description="没有可管理的设置权限。" />
          </ProCard>
        )}
      </Spin>
    </PageContainer>
  );
};

export default SettingsPage;
