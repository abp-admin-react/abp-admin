import { Result } from 'antd';
import React from 'react';
import { getTenantNameFromHost } from '@/abp/tenant';

const TenantNotFound: React.FC = () => {
  const name = getTenantNameFromHost();
  // 剥离租户子域，回 Host 的 host:port（该行注释曾因编码事故变成问号串，与登录页同批事故）
  const { protocol, hostname, port } = window.location;
  const hostName = name && hostname.startsWith(`${name}.`) ? hostname.slice(name.length + 1) : hostname;
  const hostUrl = `${protocol}//${hostName}${port ? `:${port}` : ''}/`;
  return (
    <Result
      status="404"
      title="租户不存在"
      subTitle={`未找到租户「${name || ''}」，请检查子域名是否正确。`}
      extra={<a href={hostUrl}>返回 Host</a>}
    />
  );
};

export default TenantNotFound;
