import {
  ProCard,
  ProForm,
  ProFormDigit,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Collapse } from 'antd';
import { useId } from 'react';
import {
  getEmailSettings,
  getTimezone,
  getTimezones,
  sendTestEmail,
  updateEmailSettings,
  updateTimezone,
} from '@/abp/settings';

export function EmailSettingsPanel() {
  const access = useAccess();
  const { message } = App.useApp();
  const formId = useId();
  return (
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
  );
}

export function TimezoneSettingsPanel() {
  const access = useAccess();
  const { message } = App.useApp();
  const formId = useId();
  return (
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
        extra={access.isHostContext === false ? '作用范围为当前租户' : '作用范围为宿主'}
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
  );
}
