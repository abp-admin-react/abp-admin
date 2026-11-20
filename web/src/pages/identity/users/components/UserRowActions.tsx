import { DownOutlined } from '@ant-design/icons';
import { useAccess, useModel } from '@umijs/max';
import { App, Button, Dropdown, type MenuProps, Space } from 'antd';
import { impersonateUser } from '@/abp/account';
import {
  deleteUser,
  type IdentityUserGridRowDto,
  lockUser,
  requireChangePasswordOnNextLogin,
  setUserTwoFactorEnabled,
  unlockUser,
} from '@/abp/identity';
import { applyImpersonatedTokens } from '@/abp/oidc';

type Target = (user: IdentityUserGridRowDto) => void;
type Props = {
  record: IdentityUserGridRowDto;
  onEdit: Target;
  onReset: Target;
  onPermissions: Target;
  onClaims: Target;
  onReload: () => void;
};

export default function UserRowActions({
  record,
  onEdit,
  onReset,
  onPermissions,
  onClaims,
  onReload,
}: Props) {
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const currentUserId = initialState?.currentUser?.userid;
  const { modal, message } = App.useApp();
  const isSelf = record.id === currentUserId;
  const items: MenuProps['items'] = [];
  if (access.canManageUserPermissions) {
    items.push({
      key: 'permissions',
      type: 'group',
      label: '通常通过角色授权',
      children: [
        {
          key: 'extra-permissions',
          label: '额外权限',
          onClick: () => onPermissions(record),
        },
      ],
    });
  }
  if (access.canUpdateUsers) {
    if (access.canManageClaimTypes) {
      items.push({
        key: 'claims',
        label: '声明（高级）',
        onClick: () => onClaims(record),
      });
    }
    items.push({
      key: 'lock',
      label: record.isLocked ? '解冻' : '冻结',
      onClick: () =>
        modal.confirm({
          title: record.isLocked ? '确认解冻该用户？' : '确认冻结该用户？',
          onOk: async () => {
            if (record.isLocked) {
              await unlockUser(record.id);
              message.success('已解冻');
            } else {
              await lockUser(record.id);
              message.success('已冻结');
            }
            onReload();
          },
        }),
    });
    if (!isSelf) {
      items.push(
        {
          key: 'force-change-pwd',
          label: '要求改密',
          onClick: () =>
            modal.confirm({
              title: '确认要求该用户下次登录时修改密码？',
              onOk: async () => {
                await requireChangePasswordOnNextLogin(record.id);
                message.success('已设置，该用户下次登录时将被要求修改密码');
              },
            }),
        },
        {
          key: 'two-factor',
          label:
            record.twoFactorEnabled == null
              ? '双因素（状态未知）'
              : record.twoFactorEnabled
                ? '禁用双因素'
                : '启用双因素',
          disabled: record.twoFactorEnabled == null,
          onClick: () =>
            modal.confirm({
              title: record.twoFactorEnabled
                ? '确认禁用该用户的双因素认证？'
                : '确认启用该用户的双因素认证？',
              content: record.twoFactorEnabled
                ? '禁用后该用户登录将不再需要第二因子。'
                : '启用前请确认该用户至少有一个已确认的邮箱或手机号，否则登录时无法接收验证码。',
              onOk: async () => {
                await setUserTwoFactorEnabled(
                  record.id,
                  !record.twoFactorEnabled,
                );
                message.success(
                  record.twoFactorEnabled
                    ? '已禁用双因素认证'
                    : '已启用双因素认证',
                );
                onReload();
              },
            }),
        },
      );
    }
  }
  if (!isSelf && access.canImpersonateUsers) {
    items.push({
      key: 'impersonate',
      label: '模拟此用户',
      onClick: () =>
        modal.confirm({
          title: `确认以用户「${record.userName}」的身份登录？`,
          content: '您将以该用户的身份进入系统，可随时通过顶部提示条返回。',
          onOk: async () => {
            try {
              const result = await impersonateUser(record.id);
              await applyImpersonatedTokens(result);
              message.success('已切换到模拟身份');
              window.location.reload();
            } catch {
              // 错误由全局 errorHandler 统一提示
            }
          },
        }),
    });
  }
  if (!isSelf && access.canDeleteUsers) {
    items.push({
      key: 'delete',
      label: '删除用户',
      danger: true,
      onClick: () =>
        modal.confirm({
          title: '确认删除该用户？',
          onOk: async () => {
            await deleteUser(record.id);
            message.success('已删除');
            onReload();
          },
        }),
    });
  }
  return (
    <Space size={8} className="identity-user-actions">
      {access.canUpdateUsers && (
        <>
          <Button type="link" size="small" onClick={() => onEdit(record)}>
            编辑
          </Button>
          <Button type="link" size="small" onClick={() => onReset(record)}>
            重置密码
          </Button>
        </>
      )}
      {!!items.length && (
        <Dropdown menu={{ items }} trigger={['click']}>
          <Button type="link" size="small">
            更多 <DownOutlined />
          </Button>
        </Dropdown>
      )}
    </Space>
  );
}
