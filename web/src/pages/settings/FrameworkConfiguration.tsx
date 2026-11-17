import { ProCard } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { Space } from 'antd';
import type { ReactNode } from 'react';
import {
  ConfigurationLink,
  ConfigurationPage,
  useConfigurationLocation,
} from '@/components/ContextConfiguration';
import { ChannelSettingsContent } from './channel';
import {
  EmailSettingsPanel,
  TimezoneSettingsPanel,
} from './StandardSettingPanels';

const accountPanels = [{ groupName: 'Account' }];
const tenantPanels = [{ groupName: 'Saas' }];
const auditPanels = [{ groupName: 'Audit' }];
const smsPanels = [{ groupName: 'Sms', title: '短信' }];
const legacyNotificationPanels = [
  { groupName: 'Mail', title: '邮件' },
  ...smsPanels,
];

type ConfigurationOwner =
  | 'login'
  | 'tenant'
  | 'audit'
  | 'notification'
  | 'timezone';

const owners = {
  login: { parent: '登录安全日志', title: '登录策略', section: 'policy' },
  tenant: { parent: '租户管理', title: '租户策略', section: 'policy' },
  audit: { parent: '接口审计日志', title: '保留策略', section: 'retention' },
  notification: { parent: '通知管理', title: '发送渠道', section: 'channels' },
  timezone: { parent: '语言与区域', title: '默认时区', section: 'timezone' },
};

export default function FrameworkConfiguration({
  owner,
  children,
}: {
  owner: ConfigurationOwner;
  children: (configurationEntry?: ReactNode) => ReactNode;
}) {
  const access = useAccess();
  const { setting, link } = useConfigurationLocation();
  const definition = owners[owner];
  const canConfigure = {
    login: access.canManageSettings,
    tenant: access.canManageTenantPolicy,
    audit: access.canManageSettings,
    notification: access.canManageNotificationChannels,
    timezone: access.canManageTimezone,
  }[owner];
  const canViewRecords = {
    login: access.canManageSecurityLogs,
    tenant: access.canManageTenants,
    audit: access.canManageAuditLogs,
    notification: access.canManageNotifications,
    timezone: access.canManageLanguages,
  }[owner];

  if (canConfigure && (setting === definition.section || !canViewRecords)) {
    return (
      <ConfigurationPage
        parent={definition.parent}
        title={definition.title}
        returnTo={canViewRecords ? link() : undefined}
      >
        <ProCard>
          {owner === 'login' && <ChannelSettingsContent panels={accountPanels} />}
          {owner === 'tenant' && <ChannelSettingsContent panels={tenantPanels} />}
          {owner === 'audit' && <ChannelSettingsContent panels={auditPanels} />}
          {owner === 'timezone' && <TimezoneSettingsPanel />}
          {owner === 'notification' && (
            <Space orientation="vertical" size="large" style={{ width: '100%' }}>
              {access.canManageEmailing && <EmailSettingsPanel />}
              {access.canManageSettings && (
                <ChannelSettingsContent
                  panels={access.canManageEmailing ? smsPanels : legacyNotificationPanels}
                />
              )}
            </Space>
          )}
        </ProCard>
      </ConfigurationPage>
    );
  }

  return children(
    canConfigure ? (
      <ConfigurationLink to={link(definition.section)}>{definition.title}</ConfigurationLink>
    ) : undefined,
  );
}
