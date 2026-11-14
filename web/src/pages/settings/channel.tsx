import { PageContainer, ProCard } from '@ant-design/pro-components';
import { history, Link, useAccess, useLocation } from '@umijs/max';
import { Alert, Button, Empty, Spin, Tabs, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { getSettingGroups } from '@/abp/settingUi';
import { isNotFound } from '@/requestErrorConfig';
import {
  dedicatedSettingPages,
  orderAndPaymentSettings,
  selectSettingGroup,
  type SettingPanelDefinition,
} from './channelGroups';
import SettingGroupPanel from './SettingGroupPanel';

function ChannelSettingsContent({
  panels,
  columns,
}: {
  panels: readonly SettingPanelDefinition[];
  columns?: 1 | 2;
}) {
  const [groups, setGroups] = useState<API.SettingGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    setGroups([]);
    getSettingGroups()
      .then((availableGroups) => {
        if (active) {
          setGroups(
            panels.flatMap((panel) => {
              const group = selectSettingGroup(availableGroups, panel);
              return group ? [group] : [];
            }),
          );
        }
      })
      .catch((error: unknown) => {
        if (active) setFailed(!isNotFound(error));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [panels, revision]);

  const reload = () => setRevision((value) => value + 1);

  if (loading) return <Spin aria-label="加载设置" />;
  if (failed) {
    return (
      <Alert
        type="error"
        showIcon
        title="加载设置失败"
        action={<Button onClick={reload}>重试</Button>}
      />
    );
  }
  if (!groups.length) {
    return <Empty description="暂无可管理的设置。" />;
  }
  return (
    <SettingGroupPanel group={groups} columns={columns} onChanged={reload} />
  );
}

function SettingsForm({ path }: { path: string }) {
  const page = dedicatedSettingPages[path];
  return (
    <ProCard>
      {page.description && (
        <Typography.Paragraph type="secondary">
          {page.description}
        </Typography.Paragraph>
      )}
      <ChannelSettingsContent columns={page.columns} panels={page.panels} />
    </ProCard>
  );
}

export default function ChannelSettingsPage() {
  const access = useAccess();
  const { pathname, search } = useLocation();
  const grouped = pathname === '/configuration/payments';
  const view = new URLSearchParams(search).get('view');
  const section =
    orderAndPaymentSettings.find(({ key }) => key === view) ??
    orderAndPaymentSettings[0];
  const path = grouped ? section.path : pathname;
  const page = dedicatedSettingPages[path];
  const title = grouped ? '订单与收款设置' : page?.title;

  return (
    <PageContainer
      title={title}
      breadcrumb={title ? { items: [{ title }] } : undefined}
      extra={
        page?.recordsPath &&
        access.canViewCollectionPayments && (
          <Link to={page.recordsPath}>收款与退款</Link>
        )
      }
    >
      {!page || !access.canManagePaymentSettings ? (
        <Empty description="没有可管理的设置权限。" />
      ) : grouped ? (
        <Tabs
          activeKey={section.key}
          destroyOnHidden
          onChange={(key) => history.push(`${pathname}?view=${key}`)}
          items={orderAndPaymentSettings.map(
            ({ key, label, path: settingPath }) => ({
              key,
              label,
              children:
                key === section.key ? (
                  <SettingsForm path={settingPath} />
                ) : null,
            }),
          )}
        />
      ) : (
        <SettingsForm key={path} path={path} />
      )}
    </PageContainer>
  );
}
