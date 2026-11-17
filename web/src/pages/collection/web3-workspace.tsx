import { PageContainer } from '@ant-design/pro-components';
import { history, useLocation } from '@umijs/max';
import { Empty, Tabs } from 'antd';
import type { ReactNode } from 'react';

export type Web3Section = {
  key: string;
  label: string;
  render: () => ReactNode;
};

export function Web3Workspace({
  title,
  sections,
  extra,
  notice,
}: {
  title: string;
  sections: Web3Section[];
  extra?: ReactNode;
  notice?: ReactNode;
}) {
  const { pathname, search } = useLocation();
  const requested = new URLSearchParams(search).get('view');
  const active =
    sections.find((section) => section.key === requested) ?? sections[0];

  return (
    <PageContainer
      title={title}
      breadcrumb={{ items: [{ title }] }}
      extra={extra}
    >
      {notice}
      {active ? (
        <Tabs
          activeKey={active.key}
          destroyOnHidden
          onChange={(view) => {
            const query = new URLSearchParams(search);
            query.set('view', view);
            query.delete('status');
            query.delete('depositId');
            query.delete('withdrawalId');
            query.delete('sweepTaskId');
            history.push({ pathname, search: `?${query}` });
          }}
          items={sections.map((section) => ({
            key: section.key,
            label: section.label,
            children: section.key === active.key ? section.render() : null,
          }))}
        />
      ) : (
        <Empty description="当前账号没有可访问的栏目" />
      )}
    </PageContainer>
  );
}
