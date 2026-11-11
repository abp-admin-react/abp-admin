import {
  ApartmentOutlined,
  ArrowRightOutlined,
  CalendarOutlined,
  DesktopOutlined,
  MenuOutlined,
  SettingOutlined,
  TeamOutlined,
  UserOutlined,
  UserSwitchOutlined,
} from '@ant-design/icons';
import {
  type MenuDataItem,
  PageContainer,
  RouteContext,
} from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Link, useAccess, useModel } from '@umijs/max';
import { Button, Skeleton } from 'antd';
import { createStyles } from 'antd-style';
import { useContext } from 'react';
import { getAuditLogErrorRate } from '@/abp/proModules';

const auditLogPath = '/administration/audit-logs';
const dateFormat = new Intl.DateTimeFormat('zh-CN', { dateStyle: 'full' });
const numberFormat = new Intl.NumberFormat('zh-CN');

const useStyles = createStyles(({ css, token }) => ({
  workbench: css`
    display: grid;
    gap: 22px;
  `,
  intro: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 20px 32px;
    padding: clamp(22px, 3vw, 42px);
    background:
      radial-gradient(ellipse at 92% 0%, ${token.colorPrimaryBg} 0%, transparent 65%),
      ${token.colorBgContainer};
    border: 1px solid ${token.colorBorderSecondary};
    border-radius: ${token.borderRadiusLG}px;
  `,
  title: css`
    margin: 0 0 6px;
    color: ${token.colorTextHeading};
    font-size: clamp(27px, 3vw, 36px);
    font-weight: 650;
    line-height: 1.2;
  `,
  welcome: css`
    margin: 0;
    color: ${token.colorTextSecondary};
    font-size: 15px;
    overflow-wrap: anywhere;
  `,
  context: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 14px;
    margin-top: 18px;
    color: ${token.colorTextSecondary};
    font-size: 13px;
  `,
  workspace: css`
    color: ${token.colorText};
    font-weight: 550;
    overflow-wrap: anywhere;
  `,
  columns: css`
    display: grid;
    gap: 20px;

    @media (min-width: 1140px) {
      grid-template-columns: minmax(0, 5fr) minmax(310px, 3fr);
      align-items: start;
    }
  `,
  panel: css`
    min-width: 0;
    padding: clamp(18px, 2vw, 24px);
    background: ${token.colorBgContainer};
    border: 1px solid ${token.colorBorderSecondary};
    border-radius: ${token.borderRadiusLG}px;
  `,
  panelHeader: css`
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: 6px 12px;
    margin-bottom: 16px;
  `,
  sectionTitle: css`
    margin: 0;
    color: ${token.colorTextHeading};
    font-size: 17px;
    font-weight: 650;
  `,
  secondary: css`
    margin: 0;
    color: ${token.colorTextSecondary};
    font-size: 13px;
    line-height: 1.6;
  `,
  shortcutGrid: css`
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 153px), 1fr));
    gap: 10px;
    padding: 0;
    margin: 0;
  `,
  shortcut: css`
    display: flex;
    height: 100%;
    align-items: flex-start;
    justify-content: space-between;
    gap: 6px;
    padding: 13px;
    color: ${token.colorText};
    border: 1px solid ${token.colorBorderSecondary};
    border-radius: ${token.borderRadius}px;
    text-decoration: none;

    &:hover, &:focus-visible {
      background: ${token.colorPrimaryBg};
      border-color: ${token.colorPrimaryBorder};
      color: ${token.colorText};
    }
  `,
  shortcutIcon: css`
    display: inline-grid;
    width: 34px;
    height: 34px;
    margin-bottom: 10px;
    place-items: center;
    color: ${token.colorPrimaryText};
    background: ${token.colorPrimaryBg};
    border-radius: ${token.borderRadius}px;
    font-size: 17px;
  `,
  shortcutTitle: css`
    display: block;
    margin-bottom: 3px;
    font-size: 14px;
    font-weight: 600;
  `,
  inlineLink: css`
    display: inline-flex;
    width: fit-content;
    align-items: center;
    gap: 8px;
    color: ${token.colorLink};
    font-weight: 550;
    text-decoration: none;

    &:hover {
      color: ${token.colorLinkHover};
    }
  `,
  accountLink: css`
    padding: 10px 14px;
    background: ${token.colorBgContainer};
    border: 1px solid ${token.colorPrimaryBorder};
    border-radius: ${token.borderRadius}px;
  `,
  metrics: css`
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
    padding: 0 0 16px;
    margin: 0 0 14px;
    border-bottom: 1px solid ${token.colorBorderSecondary};

    dd {
      margin: 4px 0 0;
      color: ${token.colorTextHeading};
      font-size: 27px;
      font-weight: 620;
      font-variant-numeric: tabular-nums;
      line-height: 1.2;
      overflow-wrap: anywhere;
    }
  `,
  ratio: css`
    margin: 0 0 20px;
  `,
  rail: css`
    height: 6px;
    margin-top: 8px;
    overflow: hidden;
    background: ${token.colorFillSecondary};
    border-radius: 99px;
  `,
  railFill: css`
    height: 100%;
    background: ${token.colorError};
  `,
  status: css`
    min-height: 92px;
    margin-bottom: 8px;
  `,
}));

function collectMenuPaths(items: MenuDataItem[], paths = new Set<string>()) {
  for (const item of items) {
    if (item.hideInMenu) continue;
    if (item.path) paths.add(item.path);
    if (item.children) collectMenuPaths(item.children, paths);
  }
  return paths;
}

export default function WorkbenchPage() {
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const { menuData } = useContext(RouteContext);
  const { styles, cx } = useStyles();
  const user = initialState?.currentUser;
  const tenant = initialState?.currentTenant;
  // 沿用侧栏当前菜单树，再按页面权限筛选，避免首页额外请求菜单。
  const paths = collectMenuPaths(menuData ?? []);
  const shortcuts = [
    {
      title: '用户',
      detail: '管理登录账号',
      path: '/administration/identity/users',
      Icon: TeamOutlined,
      allowed: access.canManageUsers,
    },
    {
      title: '角色',
      detail: '分配功能权限',
      path: '/administration/identity/roles',
      Icon: UserSwitchOutlined,
      allowed: access.canManageRoles,
    },
    {
      title: '部门',
      detail: '查看组织和成员',
      path: '/administration/identity/organization-units',
      Icon: ApartmentOutlined,
      allowed: access.canManageOrganizationUnits,
    },
    {
      title: '菜单管理',
      detail: '调整导航和可见性',
      path: '/administration/menus',
      Icon: MenuOutlined,
      allowed: access.canManageMenus,
    },
    {
      title: '系统设置',
      detail: '管理系统参数',
      path: '/administration/settings',
      Icon: SettingOutlined,
      allowed: access.canManageSettings,
    },
    {
      title: '服务监控',
      detail: '查看资源占用',
      path: '/system/server-monitor',
      Icon: DesktopOutlined,
      allowed: access.canViewServerMonitor,
    },
  ].filter(({ allowed, path }) => allowed && paths.has(path));
  const canViewAudit = access.canManageAuditLogs && paths.has(auditLogPath);
  // 审计统计按租户和账号缓存，且仅在卡片可见时请求。
  const audit = useQuery({
    queryKey: ['workbench-audit', tenant?.id ?? 'host', user?.userid],
    queryFn: getAuditLogErrorRate,
    enabled: canViewAudit && !!user?.userid,
    staleTime: 60_000,
    retry: false,
  });

  return (
    <PageContainer title={false} subTitle={false} breadcrumbRender={false}>
      <div className={styles.workbench}>
        <header className={styles.intro}>
          <div>
            <h1 className={styles.title}>工作台</h1>
            <p className={styles.welcome}>
              你好，{user?.name || user?.userName || '用户'}
            </p>
            <div className={styles.context}>
              <span className={styles.workspace}>
                {tenant?.isAvailable ? tenant.name : '平台管理'}
              </span>
              <span>
                <CalendarOutlined aria-hidden="true" />{' '}
                {dateFormat.format(new Date())}
              </span>
            </div>
          </div>
          <Link
            to="/account/center"
            className={cx(styles.inlineLink, styles.accountLink)}
          >
            <UserOutlined aria-hidden="true" /> 个人中心
            <ArrowRightOutlined aria-hidden="true" />
          </Link>
        </header>

        <div className={canViewAudit ? styles.columns : undefined}>
          <section
            className={styles.panel}
            aria-labelledby="workbench-shortcuts"
          >
            <div className={styles.panelHeader}>
              <h2 className={styles.sectionTitle} id="workbench-shortcuts">
                常用入口
              </h2>
            </div>
            {shortcuts.length ? (
              <ul className={styles.shortcutGrid}>
                {shortcuts.map(({ path, title, detail, Icon }) => (
                  <li key={path}>
                    <Link
                      to={path}
                      className={styles.shortcut}
                      aria-label={title}
                    >
                      <span>
                        <span className={styles.shortcutIcon}>
                          <Icon aria-hidden="true" />
                        </span>
                        <span className={styles.shortcutTitle}>{title}</span>
                        <span className={styles.secondary}>{detail}</span>
                      </span>
                      <ArrowRightOutlined
                        className={styles.secondary}
                        aria-hidden="true"
                      />
                    </Link>
                  </li>
                ))}
              </ul>
            ) : (
              <p className={styles.secondary}>从左侧菜单进入已授权的功能。</p>
            )}
          </section>

          {canViewAudit && (
            <section className={styles.panel} aria-labelledby="workbench-audit">
              <div className={styles.panelHeader}>
                <h2 className={styles.sectionTitle} id="workbench-audit">
                  审计概况
                </h2>
                <span className={styles.secondary}>最近 7 天</span>
              </div>
              {audit.isPending ? (
                <div
                  className={styles.status}
                  role="status"
                  aria-label="正在加载审计统计"
                >
                  <Skeleton active title={false} paragraph={{ rows: 2 }} />
                </div>
              ) : audit.isError ? (
                <div className={styles.status}>
                  <p className={styles.secondary} role="status">
                    暂时无法读取审计统计
                  </p>
                  <Button
                    type="link"
                    size="small"
                    loading={audit.isFetching}
                    onClick={() => void audit.refetch()}
                  >
                    重试
                  </Button>
                </div>
              ) : audit.data.totalCount === 0 ? (
                <p className={cx(styles.secondary, styles.status)}>
                  暂无审计记录
                </p>
              ) : (
                <>
                  <dl className={styles.metrics}>
                    <div>
                      <dt className={styles.secondary}>审计记录</dt>
                      <dd>{numberFormat.format(audit.data.totalCount)}</dd>
                    </div>
                    <div>
                      <dt className={styles.secondary}>错误记录</dt>
                      <dd>{numberFormat.format(audit.data.errorCount)}</dd>
                    </div>
                  </dl>
                  <div className={styles.ratio}>
                    <span className={styles.secondary}>
                      错误率 {numberFormat.format(audit.data.errorRate)}%
                    </span>
                    <div className={styles.rail} aria-hidden="true">
                      <div
                        className={styles.railFill}
                        style={{ width: `${audit.data.errorRate}%` }}
                      />
                    </div>
                  </div>
                </>
              )}
              <Link to={auditLogPath} className={styles.inlineLink}>
                查看审计日志 <ArrowRightOutlined aria-hidden="true" />
              </Link>
            </section>
          )}
        </div>
      </div>
    </PageContainer>
  );
}
