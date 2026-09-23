import { Result } from 'antd';
import React from 'react';
import { getTenantNameFromHost } from '@/abp/tenant';

const TenantNotFound: React.FC = () => {
  const name = getTenantNameFromHost();
  return (
    <Result
      status="404"
      title="租户不存在"
      subTitle={`未找到租户「${name || ''}」，请检查子域名是否正确。`}
      extra={<a href="http://localhost:8000">返回 Host</a>}
    />
  );
};

export default TenantNotFound;
