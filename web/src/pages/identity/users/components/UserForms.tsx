import {
  ModalForm,
  type ProFormInstance,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Space } from 'antd';
import React, { useRef } from 'react';
import {
  createUser,
  getUserRoles,
  type IdentityRoleDto,
  type IdentityUserDto,
  resetUserPassword,
  updateUser,
} from '@/abp/identity';
import { isNotFound } from '@/requestErrorConfig';

/**
 * 用户管理页的新建/编辑/重置密码表单（自 index.tsx 拆分，见重构报告问题 20）。
 * 含随机密码生成器（仅重置弹窗暴露按钮）与表单值→DTO 的收窄提交。
 */

const PASSWORD_LENGTH = 16;
// 四类字符各一个集合，用于「每类至少 1 位」的保底；剔除易混淆的 l / I / O / 0 / 1，
// 便于管理员把口令抄录、转达给用户。特殊字符只取常见且无需转义的一组。
const PASSWORD_CHARSETS = [
  'abcdefghijkmnopqrstuvwxyz',
  'ABCDEFGHJKLMNPQRSTUVWXYZ',
  '23456789',
  '!@#$%^&*-_=+',
];

/**
 * 生成随机密码：前 4 位保证四类字符各占 1 个，其余从全集取样，
 * 最后 Fisher–Yates 洗牌消除固定位置；随机源用 crypto.getRandomValues
 * （密码材料不可用 Math.random），模集合长度取样的偏差在 2^-32 量级可忽略。
 */
export const generateRandomPassword = (): string => {
  const randoms = new Uint32Array(PASSWORD_LENGTH * 2);
  crypto.getRandomValues(randoms);
  const pool = PASSWORD_CHARSETS.join('');
  const chars = PASSWORD_CHARSETS.map(
    (set, index) => set[randoms[index] % set.length],
  );
  for (let i = PASSWORD_CHARSETS.length; i < PASSWORD_LENGTH; i++) {
    chars.push(pool[randoms[i] % pool.length]);
  }
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randoms[PASSWORD_LENGTH + i] % (i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join('');
};

/**
 * 把新生成的随机密码写入表单的 password 字段。
 * 单独成函数并导出：按钮→表单的接线（字段名、formRef 调用）是纯静默失败路径
 * （formRef 未挂上/字段改名时点了没反应、无任何报错），用假表单直接锚定。
 */
export const fillRandomPassword = (form?: ProFormInstance) => {
  form?.setFieldsValue({ password: generateRandomPassword() });
};

export const ResetPasswordForm: React.FC<{
  record: IdentityUserDto;
  onSuccess: () => void;
}> = ({ record, onSuccess }) => {
  const { message } = App.useApp();
  const formRef = useRef<ProFormInstance>(undefined);
  return (
    <ModalForm<{ password: string }>
      title={`重置密码 - ${record.userName}`}
      trigger={<a>重置密码</a>}
      formRef={formRef}
      modalProps={{ destroyOnHidden: true }}
      width={420}
      onFinish={async (values) => {
        try {
          await resetUserPassword(record, values.password);
        } catch (error) {
          // 404 = 该用户刚在别处被删除：全局层对 404 静默，页面层按契约自行提示并关弹窗刷新，
          // 否则弹窗停留、无任何反馈（「点了个没反应」）；其余错误照旧抛回全局提示并保持弹窗
          if (isNotFound(error)) {
            message.warning('该用户已被删除');
            onSuccess();
            return true;
          }
          throw error;
        }
        message.success('密码已重置');
        onSuccess();
        return true;
      }}
    >
      <ProFormText.Password
        name="password"
        label="新密码"
        rules={[{ required: true, message: '请输入新密码' }]}
        fieldProps={{
          // 阻止浏览器密码管理器把管理员自己的站点口令 autofill 进来，
          // 被误当成随机值提交、把目标用户的密码重置成管理员口令（GUI 实测复现过）
          autoComplete: 'new-password',
          addonAfter: (
            <a onClick={() => fillRandomPassword(formRef.current)}>随机密码</a>
          ),
        }}
      />
    </ModalForm>
  );
};

/** UserForm 的表单值形状：替代此前的 onFinish values: any，让表单项→DTO 的契约可编译期校验 */
type UserFormValues = {
  userName?: string;
  email?: string;
  name?: string;
  surname?: string;
  phoneNumber?: string;
  password?: string;
  roleNames?: string[];
  isActive?: boolean;
  lockoutEnabled?: boolean;
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
      onFinish={async (values: UserFormValues) => {
        // required 规则已挡空值，这里按分支收窄给 DTO 的必填字段；
        // 开关类字段的 undefined 按「未勾选」语义兜底为 false
        if (record) {
          if (!values.userName || !values.email) {
            return false;
          }
          await updateUser(record.id, {
            userName: values.userName,
            email: values.email,
            name: values.name,
            surname: values.surname,
            phoneNumber: values.phoneNumber,
            isActive: hideActive ? record.isActive : (values.isActive ?? false),
            lockoutEnabled: values.lockoutEnabled ?? false,
            roleNames: values.roleNames,
            concurrencyStamp: record.concurrencyStamp,
          });
        } else {
          if (!values.userName || !values.email || !values.password) {
            return false;
          }
          await createUser({
            userName: values.userName,
            email: values.email,
            password: values.password,
            name: values.name,
            surname: values.surname,
            phoneNumber: values.phoneNumber,
            isActive: values.isActive ?? false,
            lockoutEnabled: values.lockoutEnabled ?? false,
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
          // 同重置弹窗：阻止把管理员自己的站点口令 autofill 进新建用户的密码框
          fieldProps={{ autoComplete: 'new-password' }}
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
