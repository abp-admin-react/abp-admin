import { queryClient } from '@/queryClient';
import { myNotificationsQueryKey } from './service';

/**
 * T3.5 铃铛的推送处理器（在 RealTimeConnection 调 startRealTime 时挂载）。
 * 刻意"让缓存失效"而不是"把推送内容塞进列表"：推送可能丢/乱序/重复，
 * 真实数据始终来自 HTTP 拉取（规格 T3.2 定调的模式）。
 * ReceiveUnreadCount 是服务端顺带推的精确未读数，直接 setQueryData 省一次 refetch。
 */
export const notificationRealTimeHandlers: Record<
  string,
  (...args: any[]) => void
> = {
  ReceiveNotification: () => {
    queryClient.invalidateQueries({ queryKey: myNotificationsQueryKey });
  },
  ReceiveUnreadCount: (payload: { count?: number }) => {
    if (typeof payload?.count === 'number') {
      queryClient.setQueryData([...myNotificationsQueryKey, 'unread-count'], {
        count: payload.count,
      });
    } else {
      queryClient.invalidateQueries({ queryKey: myNotificationsQueryKey });
    }
  },
};
