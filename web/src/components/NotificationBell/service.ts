/**
 * 我的通知 API（T3.5）。包装生成的客户端（pnpm openapi 产物），
 * 组件只依赖本文件，不直接依赖生成代码的命名。
 */
import {
  getApiAppMyNotification,
  getApiAppMyNotificationUnreadCount,
  postApiAppMyNotificationMarkAllAsRead,
} from '@/services/abpadmin/myNotification';

/** react-query 缓存键（组件与推送处理器共用） */
export const myNotificationsQueryKey = ['my-notifications'] as const;

export interface MyNotification {
  id: string;
  title?: string | null;
  body?: string | null;
  creationTime: string;
  isRead: boolean;
}

export async function getMyNotifications(params: {
  skipCount?: number;
  maxResultCount?: number;
}) {
  return getApiAppMyNotification({
    SkipCount: params.skipCount ?? 0,
    MaxResultCount: params.maxResultCount ?? 20,
  }) as Promise<{ items?: MyNotification[]; totalCount?: number }>;
}

export async function getMyUnreadCount() {
  return getApiAppMyNotificationUnreadCount() as Promise<{ count?: number }>;
}

export async function markAllMyNotificationsAsRead() {
  return postApiAppMyNotificationMarkAllAsRead();
}
