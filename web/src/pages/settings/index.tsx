import { PageContainer, ProCard } from '@ant-design/pro-components';
import { history, Link, useAccess, useLocation } from '@umijs/max';
import { App, Empty, Space, Spin } from 'antd';
import { useCallback, useEffect, useState } from 'react';
import { getSettingGroups } from '@/abp/settingUi';
import {
  getUnclaimedChannelSettings,
  tenantGeneralSettingGroupNames,
} from './channelGroups';
import SettingGroupPanel from './SettingGroupPanel';

const ownerLocations: Record<string, [string, string]> = {
  Account: ['登录策略', '/administration/identity/security-logs?settings=policy'],
  Saas: ['租户策略', '/administration/saas/tenants?settings=policy'],
  Audit: ['保留策略', '/administration/audit-logs?settings=retention'],
  Mail: ['发送渠道', '/system/notifications?settings=channels'],
  Sms: ['发送渠道', '/system/notifications?settings=channels'],
};

export default function SettingsPage() {
  const access = useAccess();
  const { pathname, search } = useLocation();
  const groupFromUrl = new URLSearchParams(search).get('group');
  const { message } = App.useApp();
  const [settingGroups, setSettingGroups] = useState<API.SettingGroup[]>([]);
  const [loadingGroups, setLoadingGroups] = useState(false);

  const loadSettingGroups = useCallback(async () => {
    if (!access.canManageSettings) return;
    setLoadingGroups(true);
    try {
      setSettingGroups((await getSettingGroups()) ?? []);
    } catch (e: any) {
      message.error(e?.message || '加载设置失败');
    } finally {
      setLoadingGroups(false);
    }
  }, [access.canManageSettings, message]);

  useEffect(() => {
    loadSettingGroups();
  }, [loadSettingGroups]);

  const ownerLinks = new Map<string, string>();
  const items = settingGroups.flatMap((group) => {
    const name = group.groupName;
    if (!name) return [];
    const location = ownerLocations[name];
    if (location && (name !== 'Saas' || access.canManageTenantPolicy)) {
      const [label, href] = location;
      ownerLinks.set(href, label);
      return [];
    }
    const available =
      !access.canManagePaymentSettings && tenantGeneralSettingGroupNames.has(name)
        ? group
        : getUnclaimedChannelSettings(group);
    if (!available) return [];
    return [{
      key: name,
      label: available.groupDisplayName || name,
      children: <SettingGroupPanel group={available} onChanged={loadSettingGroups} />,
    }];
  });
  if (access.canManageEmailing) ownerLinks.set('/system/notifications?settings=channels', '发送渠道');
  if (access.canManageTimezone) ownerLinks.set('/administration/localization/languages?settings=timezone', '默认时区');
  const selected = items.find(({ key }) => key === groupFromUrl)?.key;

  return (
    <PageContainer title="扩展配置">
      <Spin spinning={loadingGroups}>
        {items.length ? (
          <ProCard tabs={{
            items,
            activeKey: selected,
            destroyOnHidden: true,
            onChange: (group) => {
              const query = new URLSearchParams(search);
              query.set('group', group);
              history.replace(`${pathname}?${query}`);
            },
          }} />
        ) : !ownerLinks.size && !loadingGroups ? (
          <ProCard>
            <Empty description="没有可管理的设置权限。" />
          </ProCard>
        ) : null}
        {!!ownerLinks.size && (
          <ProCard title="功能内配置" style={{ marginTop: items.length ? 12 : 0 }}>
            <Space wrap size="middle">
              {[...ownerLinks].map(([href, label]) => (
                <Link key={href} to={href}>{label}</Link>
              ))}
            </Space>
          </ProCard>
        )}
      </Spin>
    </PageContainer>
  );
}
