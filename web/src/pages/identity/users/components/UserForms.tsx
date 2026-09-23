import {
  ModalForm,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Space } from 'antd';
import React from 'react';
import {
  createUser,
  getUserRoles,
  type IdentityRoleDto,
  type IdentityUserDto,
  resetUserPassword,
  updateUser,
} from '@/abp/identity';

/**
 * 用户管理页的新建/编辑/重置密码表单（自 index.tsx 拆分，见重构报告问题 20）。
 * 纯结构移动，无逻辑变更。
 */

export const ResetPasswordForm: React.FC<{
  record: IdentityUserDto;
  onSuccess: () => void;
}> = ({ record, onSuccess }) => {
  const { message } = App.useApp();
  return (
    <ModalForm
      title={`重置密码 - ${record.userName}`}
      trigger={<a>重置密码</a>}
      modalProps={{ destroyOnHidden: true }}
      width={420}
      onFinish={async (values) => {
        await resetUserPassword(record, values.password);
        message.success('密码已重置');
        onSuccess();
        return true;
      }}
    >
      <ProFormText.Password
        name="password"
        label="新密码"
        rules={[{ required: true, message: '请输入新密码' }]}
      />
    </ModalForm>
  );
};

export const UserForm: React.FC<{
  title: string;
  trigger: React.ReactElement;
  roles: IdentityRoleDto[];
  record?: IdentityUserDto;
  hideActive?: boolean;
  onSuccess: () => void;
}> = ({ title, trigger, roles, record, hideActive, onSuccess }) => {
  const { message } = App.useApp();
  return (
    <ModalForm
      title={title}
      trigger={trigger}
      modalProps={{ destroyOnHidden: true }}
      request={async () => {
        if (!record) {
          return { isActive: true, lockoutEnabled: true, roleNames: [] };
        }
        const userRoles = await getUserRoles(record.id);
        return {
          ...record,
          roleNames: (userRoles.items || []).map((item) => item.name),
        };
      }}
      onFinish={async (values: any) => {
        if (record) {
          await updateUser(record.id, {
            userName: values.userName,
            email: values.email,
            name: values.name,
            surname: values.surname,
            phoneNumber: values.phoneNumber,
            isActive: hideActive ? record.isActive : values.isActive,
            lockoutEnabled: values.lockoutEnabled,
            roleNames: values.roleNames,
            concurrencyStamp: record.concurrencyStamp,
          });
        } else {
          await createUser({
            userName: values.userName,
            email: values.email,
            password: values.password,
            name: values.name,
            surname: values.surname,
            phoneNumber: values.phoneNumber,
            isActive: values.isActive,
            lockoutEnabled: values.lockoutEnabled,
            roleNames: values.roleNames,
          });
        }
        message.success('已保存');
        onSuccess();
        return true;
      }}
    >
      <ProFormText
        name="userName"
        label="用户名"
        rules={[{ required: true, message: '请输入用户名' }]}
      />
      <ProFormText
        name="email"
        label="邮箱"
        rules={[
          { required: true, message: '请输入邮箱' },
          { type: 'email', message: '邮箱格式不正确' },
        ]}
      />
      <ProFormText name="name" label="名" />
      <ProFormText name="surname" label="姓" />
      <ProFormText name="phoneNumber" label="手机号" />
      {!record && (
        <ProFormText.Password
          name="password"
          label="密码"
          rules={[{ required: true, message: '请输入密码' }]}
        />
      )}
      <ProFormSelect
        name="roleNames"
        label="角色"
        mode="multiple"
        options={roles.map((item) => ({ label: item.name, value: item.name }))}
      />
      <Space>
        {!hideActive && <ProFormSwitch name="isActive" label="启用" />}
        <ProFormSwitch name="lockoutEnabled" label="允许锁定" />
      </Space>
    </ModalForm>
  );
};
