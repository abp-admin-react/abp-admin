import { Alert, App, Button } from 'antd';
import React, { useEffect, useState } from 'react';
import { backToMyAccount } from '@/abp/account';
import { applyImpersonatedTokens, getImpersonatorUserId } from '@/abp/oidc';

/**
 * T2.7 模拟登录提示条：处于模拟状态时固定在页面顶部，
 * 不可关闭（避免管理员忘了自己在模拟状态下操作），含「返回我的身份」按钮。
 * 判定依据：当前访问令牌带 impersonator_userid claim。
 */
const ImpersonationBanner: React.FC = () => {
  const { message } = App.useApp();
  const [impersonatorUserId, setImpersonatorUserId] = useState<string>();
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    getImpersonatorUserId()
      .then(setImpersonatorUserId)
      .catch(() => undefined);
  }, []);

  if (!impersonatorUserId) {
    return null;
  }

  const handleBack = async () => {
    setLoading(true);
    try {
      const result = await backToMyAccount();
      await applyImpersonatedTokens(result);
      message.success('已返回您的原始身份');
      // 全量刷新，使 initialState / 权限 / 菜单按原身份重建
      window.location.reload();
    } catch {
      // 错误由全局 errorHandler 统一提示
    } finally {
      setLoading(false);
    }
  };

  return (
    <Alert
      banner
      type="warning"
      closable={false}
      title="您正在以模拟身份操作，所有操作将记录到审计日志"
      action={
        <Button size="small" loading={loading} onClick={handleBack}>
          返回我的身份
        </Button>
      }
    />
  );
};

export default ImpersonationBanner;
