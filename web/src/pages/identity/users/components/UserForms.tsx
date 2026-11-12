import {
  ModalForm,
  type ProFormInstance,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Collapse } from 'antd';
import React, { useRef, useState } from 'react';
import {
  createUser,
  getUserDirectRoleNames,
  getUserEffectiveRoleNames,
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
  canManageRoles?: boolean;
  record?: IdentityUserDto;
  hideActive?: boolean;
  onSuccess: () => void;
}> = ({
  title,
  trigger,
  roles,
  canManageRoles = false,
  record,
  hideActive,
  onSuccess,
}) => {
  const { message } = App.useApp();
  const loadedRoles = useRef<Set<string> | undefined>(undefined);
  const inheritedRoles = useRef<Set<string>>(new Set());
  const [inheritedList, setInheritedList] = useState<string[]>([]);
  return (
    <ModalForm
      title={title}
      trigger={trigger}
      width={560}
      modalProps={{ destroyOnHidden: true }}
      request={async () => {
        loadedRoles.current = undefined;
        inheritedRoles.current = new Set();
        setInheritedList([]);
        if (!record) {
          return { isActive: true, lockoutEnabled: true, roleNames: [] };
        }
        // 已选只放直接角色。部门角色放进多选会让人以为勾上就能离开部门后保留，
        // 但 ABP SetRolesAsync 看到「已经通过部门持有」就不会写入 AbpUserRoles。
        if (!canManageRoles) {
          return { ...record, roleNames: undefined };
        }
        const [directRoles, effectiveNames] = await Promise.all([
          getUserDirectRoleNames(record.id),
          getUserEffectiveRoleNames(record.id),
        ]);
        const roleNames = directRoles.items ?? [];
        const directKeys = new Set(roleNames.map((name) => name.toLowerCase()));
        const inherited = effectiveNames.filter(
          (name) => !directKeys.has(name.toLowerCase()),
        );
        inheritedRoles.current = new Set(
          inherited.map((name) => name.toLowerCase()),
        );
        setInheritedList(inherited);
        loadedRoles.current = new Set(roleNames);
        return {
          ...record,
          roleNames,
        };
      }}
      onFinish={async (values: UserFormValues) => {
        // required 规则已挡空值，这里按分支收窄给 DTO 的必填字段；
        // 未展开更多选项时保留已有开关值；没有角色管理权限时不回写角色。
        if (record) {
          if (!values.userName || !values.email) {
            return false;
          }
          const previousRoles = loadedRoles.current;
          // ProForm 多选清空常得到 undefined 而非 []；用 ?? [] 才能把「清空全部」交给 SetRolesAsync。
          // 部门授予的名字即使被塞进表单也不提交：服务端会丢掉，勾选不会变成直接成员。
          const nextRoles = (values.roleNames ?? []).filter(
            (name) => !inheritedRoles.current.has(name.toLowerCase()),
          );
          const rolesChanged =
            canManageRoles &&
            !!previousRoles &&
            (previousRoles.size !== nextRoles.length ||
              nextRoles.some((name) => !previousRoles.has(name)) ||
              [...previousRoles].some((name) => !nextRoles.includes(name)));
          await updateUser(record.id, {
            userName: values.userName,
            email: values.email,
            name: values.name,
            surname: values.surname,
            phoneNumber: values.phoneNumber,
            isActive: hideActive
              ? record.isActive
              : (values.isActive ?? record.isActive),
            lockoutEnabled: values.lockoutEnabled ?? record.lockoutEnabled,
            ...(rolesChanged ? { roleNames: nextRoles } : {}),
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
            ...(canManageRoles ? { roleNames: values.roleNames } : {}),
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
      {!record && (
        <ProFormText.Password
          name="password"
          label="密码"
          rules={[{ required: true, message: '请输入密码' }]}
          // 同重置弹窗：阻止把管理员自己的站点口令 autofill 进新建用户的密码框
          fieldProps={{ autoComplete: 'new-password' }}
        />
      )}
      {canManageRoles && (
        <ProFormSelect
          name="roleNames"
          label="角色"
          mode="multiple"
          options={roles.map((item) => {
            const fromDepartment = inheritedList.some(
              (name) => name.toLowerCase() === item.name.toLowerCase(),
            );
            return {
              label: fromDepartment ? `${item.name}（部门授予）` : item.name,
              value: item.name,
              disabled: fromDepartment,
            };
          })}
          extra={
            inheritedList.length > 0
              ? `部门授予、离开部门即失效，不能在此勾选：${inheritedList.join('、')}`
              : '部门授予的角色自动生效，在部门中调整，不会变成这个人的直接角色。'
          }
        />
      )}
      {!hideActive && <ProFormSwitch name="isActive" label="启用账号" />}
      <Collapse
        items={[
          {
            key: 'more',
            label: '更多选项',
            forceRender: true,
            children: (
              <>
                <ProFormText name="name" label="名" />
                <ProFormText name="surname" label="姓" />
                <ProFormText name="phoneNumber" label="手机号" />
                <ProFormSwitch
                  name="lockoutEnabled"
                  label="允许登录失败锁定"
                  extra="连续登录失败达到系统阈值时暂时锁定账号。"
                />
              </>
            ),
          },
        ]}
      />
    </ModalForm>
  );
};
