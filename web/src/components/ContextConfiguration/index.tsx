import { PageContainer } from '@ant-design/pro-components';
import { Link, useLocation } from '@umijs/max';
import type { ReactNode } from 'react';

export function useConfigurationLocation() {
  const { pathname, search } = useLocation();
  const query = new URLSearchParams(search);
  const setting = query.get('settings');

  const link = (section?: string) => {
    const next = new URLSearchParams(query);
    if (section) next.set('settings', section);
    else {
      next.delete('settings');
      next.delete('settingsChannel');
    }
    return next.size ? `${pathname}?${next}` : pathname;
  };

  return { setting, link };
}

export function ConfigurationLink({
  to,
  children,
}: {
  to: string;
  children: ReactNode;
}) {
  return <Link to={to}>{children}</Link>;
}

export function ConfigurationPage({
  parent,
  title,
  returnTo,
  children,
}: {
  parent: string;
  title: string;
  returnTo?: string;
  children: ReactNode;
}) {
  return (
    <PageContainer
      title={title}
      breadcrumb={{ items: [{ title: parent }, { title }] }}
      extra={
        returnTo ? (
          <ConfigurationLink to={returnTo}>返回{parent}</ConfigurationLink>
        ) : undefined
      }
    >
      {children}
    </PageContainer>
  );
}
