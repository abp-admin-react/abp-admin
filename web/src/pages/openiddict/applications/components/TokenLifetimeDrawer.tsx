import { DrawerForm, ProFormDigit } from '@ant-design/pro-components';
import { App } from 'antd';
import React from 'react';
import {
  getOpenIddictApplicationTokenLifetime,
  updateOpenIddictApplicationTokenLifetime,
} from '@/abp/openIddictApplications';

/** T2.9 规格：8 项 token lifetime 覆盖（OpenIddict 7.5.0 仅支持这 8 项，无 State token），单位秒 */
const LIFETIME_FIELDS = [
  { name: 'accessTokenLifetime', label: 'Access token' },
  { name: 'authorizationCodeLifetime', label: 'Authorization code' },
  { name: 'deviceCodeLifetime', label: 'Device code' },
  { name: 'identityTokenLifetime', label: 'Identity token' },
  { name: 'refreshTokenLifetime', label: 'Refresh token' },
  { name: 'userCodeLifetime', label: 'User code' },
  { name: 'requestTokenLifetime', label: 'Request token（PAR）' },
  { name: 'issuedTokenLifetime', label: 'Issued token' },
];

export type TokenLifetimeDrawerProps = {
  applicationId: string;
  clientId?: string | null;
};

/** Token 生命周期覆盖：8 项，单位秒，留空=移除覆盖使用服务器默认 */
const TokenLifetimeDrawer: React.FC<TokenLifetimeDrawerProps> = ({
  applicationId,
  clientId,
}) => {
  // DrawerForm 的内容组件同样是 React 组件，可直接用 App.useApp()（静态 message 不消费 App 上下文）
  const { message } = App.useApp();

  return (
    <DrawerForm
      title={`Token 生命周期 - ${clientId ?? ''}`}
      trigger={<a>Token 生命周期</a>}
      width={520}
      drawerProps={{ destroyOnHidden: true }}
      request={async () =>
        await getOpenIddictApplicationTokenLifetime(applicationId)
      }
      onFinish={async (values) => {
        await updateOpenIddictApplicationTokenLifetime(applicationId, values);
        message.success('已保存');
        return true;
      }}
    >
      {LIFETIME_FIELDS.map((field) => (
        <ProFormDigit
          key={field.name}
          name={field.name}
          label={`${field.label}（秒）`}
          min={1}
          placeholder="留空使用服务器默认"
          fieldProps={{ precision: 0, addonAfter: '秒' }}
        />
      ))}
    </DrawerForm>
  );
};

export default TokenLifetimeDrawer;
