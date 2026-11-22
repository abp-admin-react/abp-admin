/** OIDC 回调先建立会话，再进入工作台。失败时显示错误和返回登录入口。 */
import { history } from '@umijs/max';
import { Result, Spin } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { completeLogin } from '@/abp/oidc';
import { getStoredTenant } from '@/abp/tenant';
import { DEFAULT_LANDING_PATH } from '@/utils/redirect';

const Callback: React.FC = () => {
  const [error, setError] = useState<string>();
  const started = useRef(false);

  useEffect(() => {
    if (started.current) {
      return;
    }
    started.current = true;
    completeLogin()
      .then(() => {
        window.location.replace(DEFAULT_LANDING_PATH);
      })
      .catch((err: Error) => {
        setError(err.message || '登录回调失败');
      });
  }, []);

  if (error) {
    return (
      <Result
        status="error"
        title="登录失败"
        subTitle={error}
        extra={
          <a
            onClick={() => {
              const tenantName = getStoredTenant()?.name;
              const search = tenantName
                ? `?${new URLSearchParams({ tenant: tenantName })}`
                : '';
              history.replace(`/user/login${search}`);
            }}
          >
            返回登录
          </a>
        }
      />
    );
  }

  return (
    <div style={{ paddingTop: 120, textAlign: 'center' }}>
      <Spin size="large" description="正在完成登录..." />
    </div>
  );
};

export default Callback;
