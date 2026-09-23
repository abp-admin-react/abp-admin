import {
  PageContainer,
  ProCard,
  ProForm,
  ProFormText,
} from '@ant-design/pro-components';
import { useModel } from '@umijs/max';
import { App, Space } from 'antd';
import React, { useRef, useState } from 'react';
import {
  changeMyPassword,
  getMyProfile,
  type ProfileDto,
  updateMyProfile,
} from '@/abp/account';
import AvatarUpload from './AvatarUpload';
import AuthenticatorTab from './tabs/AuthenticatorTab';
import ContactConfirmSection from './tabs/ContactConfirmSection';
import DelegationTab from './tabs/DelegationTab';
import ExternalLoginsTab from './tabs/ExternalLoginsTab';
import LinkedAccountsTab from './tabs/LinkedAccountsTab';
import PasskeysTab from './tabs/PasskeysTab';
import PersonalDataTab from './tabs/PersonalDataTab';
import SecuritySettingsTab from './tabs/SecuritySettingsTab';

/**
 * 个人中心（重构报告问题 4：原先 887 行 / 8 个组件挤在单文件，
 * 已按 Tab 拆分到 ./tabs/，本文件只负责个人资料/修改密码两个内联表单与 Tab 组装）。
 */
const AccountCenter: React.FC = () => {
  const { message } = App.useApp();
  const { initialState, setInitialState } = useModel('@@initialState');
  const profileFormRef = useRef<any>(undefined);
  const [profile, setProfile] = useState<ProfileDto>();
  // 资料保存成功后递增，通知联系方式确认面板重拉确认状态
  const [contactRefreshKey, setContactRefreshKey] = useState(0);
  const settings = initialState?.settingValues ?? {};
  const canUpdateUserName =
    settings['Abp.Identity.User.IsUserNameUpdateEnabled'] !== 'false';
  const canUpdateEmail =
    settings['Abp.Identity.User.IsEmailUpdateEnabled'] !== 'false';

  return (
    <PageContainer>
      <ProCard
        tabs={{
          items: [
            {
              key: 'profile',
              label: '个人资料',
              children: (
                <Space
                  orientation="vertical"
                  size="large"
                  style={{ width: '100%' }}
                >
                  <AvatarUpload />
                  <ProForm
                    formRef={profileFormRef}
                    request={async () => {
                      const current = await getMyProfile();
                      setProfile(current);
                      return current;
                    }}
                    submitter={{ searchConfig: { submitText: '保存资料' } }}
                    onFinish={async (values) => {
                      const current = await getMyProfile();
                      await updateMyProfile({
                        userName: values.userName,
                        email: values.email,
                        name: values.name,
                        surname: values.surname,
                        phoneNumber: values.phoneNumber,
                        concurrencyStamp: current.concurrencyStamp,
                      });
                      message.success('资料已保存');
                      setContactRefreshKey((k) => k + 1);
                      setInitialState((state) => ({
                        ...state,
                        currentUser: state?.currentUser
                          ? {
                              ...state.currentUser,
                              name: values.name || values.userName,
                              userName: values.userName,
                              email: values.email,
                            }
                          : state?.currentUser,
                      }));
                      return true;
                    }}
                  >
                    <ProFormText
                      name="userName"
                      label="用户名"
                      disabled={!canUpdateUserName}
                      rules={[{ required: true, message: '请输入用户名' }]}
                    />
                    <ProFormText
                      name="email"
                      label="邮箱"
                      disabled={!canUpdateEmail}
                      rules={[
                        { required: true, message: '请输入邮箱' },
                        { type: 'email', message: '邮箱格式不正确' },
                      ]}
                    />
                    <ProFormText name="name" label="名" />
                    <ProFormText name="surname" label="姓" />
                    <ProFormText name="phoneNumber" label="手机号" />
                  </ProForm>
                  <ContactConfirmSection refreshKey={contactRefreshKey} />
                </Space>
              ),
            },
            profile?.isExternal || profile?.hasPassword === false
              ? null
              : {
                  key: 'password',
                  label: '修改密码',
                  children: (
                    <ProForm
                      submitter={{ searchConfig: { submitText: '修改密码' } }}
                      onFinish={async (values) => {
                        await changeMyPassword({
                          currentPassword: values.currentPassword,
                          newPassword: values.newPassword,
                        });
                        message.success('密码已修改');
                        return true;
                      }}
                    >
                      <ProFormText.Password
                        name="currentPassword"
                        label="当前密码"
                        rules={[{ required: true, message: '请输入当前密码' }]}
                      />
                      <ProFormText.Password
                        name="newPassword"
                        label="新密码"
                        rules={[{ required: true, message: '请输入新密码' }]}
                      />
                      <ProFormText.Password
                        name="confirmPassword"
                        label="确认新密码"
                        dependencies={['newPassword']}
                        rules={[
                          { required: true, message: '请再次输入新密码' },
                          ({ getFieldValue }) => ({
                            validator(_, value) {
                              if (
                                !value ||
                                getFieldValue('newPassword') === value
                              ) {
                                return Promise.resolve();
                              }
                              return Promise.reject(
                                new Error('两次输入的密码不一致'),
                              );
                            },
                          }),
                        ]}
                      />
                    </ProForm>
                  ),
                },
            {
              key: 'security',
              label: '安全设置',
              children: <SecuritySettingsTab />,
            },
            {
              key: 'logins',
              label: '账号关联',
              children: <ExternalLoginsTab />,
            },
            {
              key: 'linked-accounts',
              label: '多账号',
              children: <LinkedAccountsTab />,
            },
            {
              key: 'authenticator',
              label: 'Authenticator',
              children: <AuthenticatorTab />,
            },
            {
              key: 'passkeys',
              label: 'Passkey',
              children: <PasskeysTab />,
            },
            {
              key: 'delegation',
              label: '权限委托',
              children: <DelegationTab />,
            },
            {
              key: 'personal-data',
              label: '个人数据',
              children: <PersonalDataTab />,
            },
          ].filter(Boolean) as any,
        }}
      />
    </PageContainer>
  );
};

export default AccountCenter;
