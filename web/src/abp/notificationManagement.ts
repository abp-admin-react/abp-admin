/**
 * 通知管理 API，对齐 INotificationManagementAppService。
 */
import { request } from '@umijs/max';

const path = '/api/app/notification-management';

export interface NotificationListItem {
  id: string;
  userId: string;
  userName: string | null;
  notificationInfoId: string;
  notificationMethod: string | null;
  success: boolean | null;
  completionTime: string | null;
  failureReason: string | null;
  creationTime: string;
  retryCount: number;
  finalSuccess: boolean | null;
}

export interface NotificationAttempt {
  id: string;
  success: boolean | null;
  completionTime: string | null;
  failureReason: string | null;
  creationTime: string;
}

export interface NotificationDetail extends NotificationListItem {
  retryForNotificationId: string | null;
  notificationInfoProperties: Record<string, unknown> | null;
  attempts: NotificationAttempt[];
}

export interface NotificationBroadcast {
  id: string;
  targetType: string | null;
  targetId: string | null;
  notificationMethods: string | null;
  title: string | null;
  body: string | null;
  totalCount: number;
  sentCount: number;
  failedCount: number;
  state: string | null;
  creationTime: string;
  completionTime: string | null;
}

export interface BroadcastInput {
  targetType: string;
  targetId?: string;
  notificationMethods: string[];
  title: string;
  body: string;
  smsText?: string;
  smsProperties?: Record<string, unknown>;
}

export async function getNotifications(params: {
  current?: number;
  pageSize?: number;
  notificationMethod?: string;
  success?: boolean;
  pendingOnly?: boolean;
  userName?: string;
  creationTimeStart?: string;
  creationTimeEnd?: string;
  includeRetries?: boolean;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<{ items: NotificationListItem[]; totalCount: number }>(path, {
    params: {
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      NotificationMethod: params.notificationMethod || undefined,
      Success: params.success,
      PendingOnly: params.pendingOnly,
      UserName: params.userName || undefined,
      CreationTimeStart: params.creationTimeStart,
      CreationTimeEnd: params.creationTimeEnd,
      IncludeRetries: params.includeRetries,
    },
  });
}

export async function getNotificationDetail(id: string) {
  return request<NotificationDetail>(`${path}/${id}`);
}

export async function retryNotification(id: string) {
  return request<void>(`${path}/${id}/retry`, { method: 'POST' });
}

export async function broadcastNotification(data: BroadcastInput) {
  return request<string>(`${path}/broadcast`, { method: 'POST', data });
}

export async function sendNotificationToUsers(data: {
  userIds: string[];
  notificationMethods: string[];
  title: string;
  body: string;
  smsText?: string;
  smsProperties?: Record<string, unknown>;
  /** 宿主发往租户用户时显式传入（ABP CurrentTenant.Change）；有 `__tenant` 头时可省略 */
  tenantId?: string;
}) {
  return request<void>(`${path}/send-to-users`, { method: 'POST', data });
}

export async function getBroadcast(id: string) {
  return request<NotificationBroadcast>(`${path}/${id}/broadcast`);
}
