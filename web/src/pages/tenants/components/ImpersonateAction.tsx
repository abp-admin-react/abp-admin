import { App, Popconfirm } from 'antd';
import React from 'react';
import { impersonateTenant } from '@/abp/account';
import { applyImpersonatedTokens } from '@/abp/oidc';
import type { TenantDto } from '@/abp/types';

type ImpersonateActionProps = {
  record: TenantDto;
};

/** 以租户管理员身份模拟登录（全量刷新重建身份），从首页操作列抽离。 */
const ImpersonateAction: React.FC<ImpersonateActionProps> = ({ record }) => {
  const { message } = App.useApp();

  return (
    <Popconfirm
      title={`确认以租户「${record.name}」的身份登录？`}
      description="您将以该租户管理员的身份进入系统，可随时通过顶部提示条返回。"
      onConfirm={async () => {
        try {
          const result = await impersonateTenant(record.id);
          await applyImpersonatedTokens(result);
          message.success('已切换到模拟身份');
          // 全量刷新，使 initialState / 权限 / 菜单按新身份重建
          window.location.reload();
        } catch {
          // 错误由全局 errorHandler 统一提示
        }
      }}
    >
      <a>以此租户身份登录</a>
    </Popconfirm>
  );
};

export default ImpersonateAction;
