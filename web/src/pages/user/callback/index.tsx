/**
 * OIDC 授权码回调页（redirect_uri = /user/callback）。
 *
 * - completeLogin() 用 query 换令牌并写入 userStore；失败回登录并展示错。
 * - 成功后进入原访问地址，直接登录时进入工作台。
 * - 不在此页再 startLogin，避免循环跳转。
 */
import { history } from '@umijs/max';
import { Result, Spin } from 'antd';
import React, { useEffect, useState } from 'react';
import { completeLogin } from '@/abp/oidc';
import { consumeIntendedRedirect } from '@/utils/redirect';

const Callback: React.FC = () => {
  const [error, setError] = useState<string>();

  useEffect(() => {
    completeLogin()
      .then(() => {
        // 双重过滤：login 侧已过滤，这里再校验一次（防手工改写 sessionStorage）。
        window.location.replace(consumeIntendedRedirect());
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
              history.replace('/user/login');
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
