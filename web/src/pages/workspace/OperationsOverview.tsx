import { ArrowRightOutlined, ReloadOutlined } from '@ant-design/icons';
import { useQueries } from '@tanstack/react-query';
import { history, Link, useLocation } from '@umijs/max';
import { Button, Skeleton, Tag } from 'antd';
import { createStyles } from 'antd-style';
import {
  type OperationAccess,
  recentRecordLimit,
  recentSources,
  taskSources,
} from './operationsData';

const numberFormat = new Intl.NumberFormat('zh-CN');
const dateFormat = new Intl.DateTimeFormat('zh-CN', {
  dateStyle: 'short',
  timeStyle: 'short',
});
const queryOptions = {
  staleTime: 30_000,
  retry: false,
  refetchOnWindowFocus: false,
};
const chainRefreshInterval = 5_000;

const useStyles = createStyles(({ css, token }) => ({
  overview: css`
    display: grid;
    gap: 16px;
  `,
  heading: css`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
  `,
  title: css`
    margin: 0;
    color: ${token.colorTextHeading};
    font-size: 18px;
    font-weight: 650;
  `,
  columns: css`
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    grid-template-areas: 'tasks' 'activity';
    gap: 16px;

    @media (min-width: 1140px) {
      grid-template-columns: minmax(0, 1fr) minmax(250px, 28%);
      grid-template-areas: 'activity tasks';
      align-items: start;
    }
  `,
  activityPanel: css`
    grid-area: activity;
    container-type: inline-size;
  `,
  tasksPanel: css`
    grid-area: tasks;
  `,
  panel: css`
    min-width: 0;
    padding: 16px;
    border: 1px solid ${token.colorBorderSecondary};
    border-radius: ${token.borderRadiusLG}px;
    background: ${token.colorBgContainer};
  `,
  actions: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
    margin-top: 12px;
    padding: 0;
    border: 0;
  `,
  list: css`
    margin: 12px 0 0;
    padding: 0;
    list-style: none;
  `,
  row: css`
    display: flex;
    min-width: 0;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 12px;
    padding: 9px 0;
    border-bottom: 1px solid ${token.colorBorderSecondary};

    &:last-child { border-bottom: 0; }
  `,
  activityRow: css`
    display: grid;
    grid-template-columns: 74px minmax(0, 1fr);

    & > :first-child { justify-self: start; }
    & > time { grid-column: 2; }

    @container (min-width: 580px) {
      grid-template-columns: 76px minmax(0, 1fr) minmax(105px, auto) minmax(76px, auto) auto;
      & > time { grid-column: auto; justify-self: end; }
    }
  `,
  main: css`
    flex: 1 1 130px;
    min-width: 0;
    overflow-wrap: anywhere;
  `,
  detail: css`
    display: block;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
  `,
  secondary: css`
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
  `,
  money: css`
    font-variant-numeric: tabular-nums;
    overflow-wrap: anywhere;
  `,
  status: css`
    color: ${token.colorTextSecondary};
    white-space: nowrap;
  `,
  links: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px 14px;
    margin-top: 14px;
  `,
  task: css`
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    width: 100%;
    color: ${token.colorText};

    &:hover { color: ${token.colorLink}; }
  `,
  taskCount: css`
    display: inline-flex;
    align-items: center;
    gap: 8px;
    color: ${token.colorLink};
    font-weight: 700;
    font-variant-numeric: tabular-nums;
  `,
}));

function timestamp(value: string) {
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

function formattedTime(value: string) {
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? '—' : dateFormat.format(parsed);
}

export default function OperationsOverview({
  access,
  userId,
  tenantId,
}: {
  access: OperationAccess;
  userId?: string;
  tenantId?: string;
}) {
  const { cx, styles } = useStyles();
  const { pathname, search } = useLocation();
  const filter = new URLSearchParams(search).get('activity') ?? 'all';
  const activitySources = userId ? recentSources(access) : [];
  const todoSources = userId ? taskSources(access) : [];
  const activityQueries = useQueries({
    queries: activitySources.map((source) => ({
      ...queryOptions,
      queryKey: [
        'workbench',
        tenantId ?? 'host',
        userId,
        'activity',
        source.key,
      ],
      queryFn: source.load,
      refetchOnMount:
        source.key === 'withdrawals' || source.key === 'deposits'
          ? ('always' as const)
          : true,
      refetchInterval:
        source.key === 'withdrawals' || source.key === 'deposits'
          ? chainRefreshInterval
          : false,
    })),
  });
  const todoQueries = useQueries({
    queries: todoSources.map((source) => ({
      ...queryOptions,
      queryKey: ['workbench', tenantId ?? 'host', userId, 'tasks', source.key],
      queryFn: source.load,
      refetchOnMount: source.key === 'withdrawals' ? ('always' as const) : true,
      refetchInterval:
        source.key === 'withdrawals' || source.key === 'sweeps'
          ? chainRefreshInterval
          : false,
    })),
  });

  if (!activitySources.length && !todoSources.length) return null;
  const activeFilter = activitySources.some((source) => source.key === filter)
    ? filter
    : 'all';
  const activity = activitySources
    .map((source, index) => ({ source, query: activityQueries[index] }))
    .filter(
      ({ source }) => activeFilter === 'all' || source.key === activeFilter,
    );
  const records = activity
    .flatMap(({ source, query }) =>
      (query.data ?? []).map((record) => ({ ...record, source })),
    )
    .sort((a, b) => timestamp(b.createdAt) - timestamp(a.createdAt))
    .slice(0, recentRecordLimit);
  const activityLoading = activity.some(({ query }) => query.isPending);
  const taskLoading = todoQueries.some((query) => query.isPending);
  const activityErrors = activity
    .filter(({ query }) => query.isError)
    .map(({ source }) => source.label);
  const taskErrors = todoSources
    .filter((_, index) => todoQueries[index].isError)
    .map(({ label }) => label);
  const pendingTasks = todoSources.flatMap(({ alerts }, index) =>
    alerts.flatMap(({ label, path }, alertIndex) => {
      const count = todoQueries[index].data?.[alertIndex];
      return count ? [{ label, path, count }] : [];
    }),
  );
  const queries = [...activityQueries, ...todoQueries];

  function setFilter(key: string) {
    const query = new URLSearchParams(search);
    if (key === 'all') query.delete('activity');
    else query.set('activity', key);
    history.replace(query.size ? `${pathname}?${query}` : pathname);
  }

  return (
    <section className={styles.overview} aria-label="业务运营">
      <div className={styles.heading}>
        <h2 className={styles.title}>业务运营</h2>
        <Button
          size="small"
          icon={<ReloadOutlined />}
          loading={queries.some((query) => query.isFetching)}
          onClick={() => {
            for (const query of queries) void query.refetch();
          }}
        >
          刷新业务数据
        </Button>
      </div>
      <div
        className={
          activitySources.length && todoSources.length
            ? styles.columns
            : undefined
        }
      >
        {!!todoSources.length && (
          <section
            className={cx(styles.panel, styles.tasksPanel)}
            aria-labelledby="workbench-tasks"
          >
            <h3 className={styles.title} id="workbench-tasks">
              待处理
            </h3>
            {!!pendingTasks.length && (
              <ul className={styles.list}>
                {pendingTasks.map(({ label, path, count }) => (
                  <li className={styles.row} key={path}>
                    <Link to={path} className={styles.task}>
                      {label}
                      <span className={styles.taskCount}>
                        {numberFormat.format(count)}
                        <ArrowRightOutlined aria-hidden="true" />
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
            {taskLoading && (
              <Skeleton
                active
                title={false}
                paragraph={{ rows: 2 }}
                aria-label="加载待处理事项"
              />
            )}
            {!!taskErrors.length && (
              <p className={styles.secondary} role="status">
                暂时无法读取{taskErrors.join('、')}的待处理事项
              </p>
            )}
            {!pendingTasks.length && !taskLoading && !taskErrors.length && (
              <p className={styles.secondary}>当前没有待处理事项</p>
            )}
          </section>
        )}
        {!!activitySources.length && (
          <section
            className={cx(styles.panel, styles.activityPanel)}
            aria-labelledby="workbench-latest"
          >
            <div className={styles.heading}>
              <h3 className={styles.title} id="workbench-latest">
                最新动态
              </h3>
              {records.length > 0 && (
                <span className={styles.secondary}>
                  最近 {records.length} 条 · 按创建时间
                </span>
              )}
            </div>
            {activitySources.length > 1 && (
              <fieldset className={styles.actions} aria-label="记录类型">
                {[{ key: 'all', label: '全部' }, ...activitySources].map(
                  ({ key, label }) => (
                    <Button
                      key={key}
                      size="small"
                      type={activeFilter === key ? 'primary' : 'default'}
                      aria-pressed={activeFilter === key}
                      onClick={() => setFilter(key)}
                    >
                      {label}
                    </Button>
                  ),
                )}
              </fieldset>
            )}
            {!!records.length && (
              <ul className={styles.list}>
                {records.map((record) => (
                  <li
                    className={cx(styles.row, styles.activityRow)}
                    key={`${record.source.key}-${record.id}`}
                  >
                    <Tag>{record.source.label}</Tag>
                    <div className={styles.main}>
                      <Link to={record.path}>{record.label}</Link>
                      {record.detail && (
                        <span className={styles.detail} title={record.detail}>
                          {record.detail}
                        </span>
                      )}
                    </div>
                    <span className={styles.money}>{record.amount ?? '—'}</span>
                    <span className={styles.status}>{record.status}</span>
                    <time
                      className={styles.secondary}
                      dateTime={record.createdAt}
                    >
                      {formattedTime(record.createdAt)}
                    </time>
                  </li>
                ))}
              </ul>
            )}
            {activityLoading && (
              <Skeleton
                active
                title={false}
                paragraph={{ rows: 2 }}
                aria-label="加载最新动态"
              />
            )}
            {!!activityErrors.length && (
              <p role="status" className={styles.secondary}>
                暂时无法读取{activityErrors.join('、')}的最新记录
              </p>
            )}
            {!records.length && !activityLoading && !activityErrors.length && (
              <p className={styles.secondary}>暂无新增记录</p>
            )}
            <div className={styles.links}>
              {activity.map(({ source }) => (
                <Link key={source.key} to={source.path}>
                  查看{source.label}
                </Link>
              ))}
            </div>
          </section>
        )}
      </div>
    </section>
  );
}
