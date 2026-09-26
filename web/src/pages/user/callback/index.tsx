import { history } from '@umijs/max';
import { Result, Spin } from 'antd';
import React, { useEffect, useState } from 'react';
import { completeLogin } from '@/abp/oidc';

const Callback: React.FC = () => {
  const [error, setError] = useState<string>();

  useEffect(() => {
    completeLogin()
      .then(() => {
        const raw = sessionStorage.getItem('abp.redirect');
        sessionStorage.removeItem('abp.redirect');
        // 双保险：login 侧已过滤，这里再校验一次（防其它入口写入 sessionStorage）
        const redirect =
          raw && raw.startsWith('/') && !raw.startsWith('//')
            ? raw
            : '/administration';
        window.location.replace(redirect);
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
      <Spin size="large" tip="正在完成登录..." />
    </div>
  );
};

export default Callback;
