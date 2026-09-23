import { BellOutlined, CheckOutlined } from '@ant-design/icons';
import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useIntl } from '@umijs/max';
import { Badge, Button, Drawer, Empty, List, Tooltip } from 'antd';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { useEffect, useState } from 'react';

// app.tsx 也注册了同一插件（extend 幂等）；组件自带注册是为了测试环境可独立渲染
dayjs.extend(relativeTime);

import {
  isRealTimeAvailable,
  onRealTimeAvailabilityChange,
} from '@/abp/signalr';
import {
  getMyNotifications,
  getMyUnreadCount,
  markAllMyNotificationsAsRead,
  myNotificationsQueryKey,
} from './service';

const PAGE_SIZE = 20;

/** SignalR 不可用时未读数的降级轮询间隔。 */
const UNREAD_POLL_INTERVAL_MS = 30_000;

/**
 * 通知铃铛（T3.5）。数据源是 IMyNotificationAppService（任何登录用户可用，不依赖 Manage）。
 * 实时推送只当"该刷新了"的信号（invalidate），真实数据始终来自 HTTP 拉取；
 * SignalR 不可用（isRealTimeAvailable=false）时未读数降级为 30 秒轮询。
 */
export default function NotificationBell() {
  const intl = useIntl();
  const [open, setOpen] = useState(false);
  const [realTime, setRealTime] = useState(isRealTimeAvailable());
  const queryClient = useQueryClient();

  useEffect(() => {
    const off = onRealTimeAvailabilityChange(setRealTime);
    return () => {
      off();
    };
  }, []);

  // 实时可用时不轮询；不可用时降级为 30 秒轮询
  const { data: unread } = useQuery({
    queryKey: [...myNotificationsQueryKey, 'unread-count'],
    queryFn: getMyUnreadCount,
    refetchInterval: realTime ? false : UNREAD_POLL_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useInfiniteQuery({
      queryKey: [...myNotificationsQueryKey, 'list'],
      queryFn: ({ pageParam }) =>
        getMyNotifications({ skipCount: pageParam, maxResultCount: PAGE_SIZE }),
      initialPageParam: 0,
      getNextPageParam: (lastPage, allPages) => {
        const loaded = allPages.reduce((n, p) => n + (p.items?.length ?? 0), 0);
        return loaded < (lastPage.totalCount ?? 0) ? loaded : undefined;
      },
      enabled: open,
    });

  const markAllMutation = useMutation({
    mutationFn: markAllMyNotificationsAsRead,
    onSuccess: () => {
      queryClient.setQueryData([...myNotificationsQueryKey, 'unread-count'], {
        count: 0,
      });
      queryClient.invalidateQueries({ queryKey: myNotificationsQueryKey });
    },
  });

  const items = data?.pages.flatMap((p) => p.items ?? []) ?? [];

  return (
    <>
      <Tooltip
        title={
          realTime
            ? intl.formatMessage({ id: 'component.notification.bell' })
            : intl.formatMessage({ id: 'component.notification.bell.polling' })
        }
      >
        <Badge count={unread?.count ?? 0} size="small" overflowCount={99}>
          <Button
            type="text"
            aria-label="notifications"
            icon={<BellOutlined />}
            onClick={() => setOpen(true)}
          />
        </Badge>
      </Tooltip>
      <Drawer
        title={intl.formatMessage({
          id: 'component.notification.drawer.title',
        })}
        size={420}
        open={open}
        onClose={() => setOpen(false)}
        extra={
          items.length > 0 ? (
            <Button
              size="small"
              icon={<CheckOutlined />}
              loading={markAllMutation.isPending}
              onClick={() => markAllMutation.mutate()}
            >
              {intl.formatMessage({ id: 'component.notification.markAllRead' })}
            </Button>
          ) : null
        }
      >
        {items.length > 0 ? (
          <List
            loading={isLoading}
            dataSource={items}
            loadMore={
              hasNextPage ? (
                <div style={{ textAlign: 'center', marginTop: 12 }}>
                  <Button
                    size="small"
                    loading={isFetchingNextPage}
                    onClick={() => fetchNextPage()}
                  >
                    {intl.formatMessage({
                      id: 'component.notification.loadMore',
                    })}
                  </Button>
                </div>
              ) : null
            }
            renderItem={(item) => (
              <List.Item>
                <List.Item.Meta
                  title={
                    <>
                      {!item.isRead && <Badge status="processing" />}{' '}
                      {item.title}
                    </>
                  }
                  description={
                    <>
                      <div>{item.body}</div>
                      <div style={{ color: 'rgba(0,0,0,0.45)', fontSize: 12 }}>
                        {dayjs(item.creationTime).fromNow()}
                      </div>
                    </>
                  }
                />
              </List.Item>
            )}
          />
        ) : (
          <Empty
            description={intl.formatMessage({
              id: 'component.notification.empty',
            })}
          />
        )}
      </Drawer>
    </>
  );
}
