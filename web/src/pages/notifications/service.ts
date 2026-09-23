/**
 * 通知管理 API（T3.5）。包装 pnpm openapi 生成客户端，页面只调本文件。
 * 管理侧走 INotificationManagementAppService（要 Manage 权限）；
 * 模块自带的只读端点（/api/notification-service/notification）不在页面使用。
 */
import {
  getApiAppNotificationManagement,
  getApiAppNotificationManagementId,
  getApiAppNotificationManagementIdBroadcast,
  postApiAppNotificationManagementBroadcast,
  postApiAppNotificationManagementIdRetry,
  postApiAppNotificationManagementSendToUsers,
} from '@/services/abpadmin/notificationManagement';

export interface NotificationListItem {
  id: string;
  userId: string;
  userName?: string | null;
  notificationInfoId: string;
  notificationMethod?: string | null;
  success?: boolean | null;
  completionTime?: string | null;
  failureReason?: string | null;
  creationTime: string;
  retryCount: number;
  finalSuccess?: boolean | null;
}

export interface NotificationAttempt {
  id: string;
  success?: boolean | null;
  completionTime?: string | null;
  failureReason?: string | null;
  creationTime: string;
}

export interface NotificationDetail extends NotificationListItem {
  retryForNotificationId?: string | null;
  notificationInfoProperties?: Record<string, unknown> | null;
  attempts?: NotificationAttempt[];
}

export interface NotificationBroadcast {
  id: string;
  targetType?: string | null;
  targetId?: string | null;
  notificationMethods?: string | null;
  title?: string | null;
  body?: string | null;
  totalCount: number;
  sentCount: number;
  failedCount: number;
  state?: string | null;
  creationTime: string;
  completionTime?: string | null;
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
  return getApiAppNotificationManagement({
    SkipCount: skipCount,
    MaxResultCount: maxResultCount,
    NotificationMethod: params.notificationMethod || undefined,
    Success: params.success,
    PendingOnly: params.pendingOnly,
    UserName: params.userName || undefined,
    CreationTimeStart: params.creationTimeStart,
    CreationTimeEnd: params.creationTimeEnd,
    IncludeRetries: params.includeRetries,
  }) as Promise<{ items?: NotificationListItem[]; totalCount?: number }>;
}

export async function getNotificationDetail(id: string) {
  return getApiAppNotificationManagementId({ id }) as Promise<NotificationDetail>;
}

export async function retryNotification(id: string) {
  return postApiAppNotificationManagementIdRetry({ id });
}

export async function broadcastNotification(data: BroadcastInput) {
  return postApiAppNotificationManagementBroadcast(data) as Promise<string>;
}

export async function sendNotificationToUsers(data: {
  userIds: string[];
  notificationMethods: string[];
  title: string;
  body: string;
  smsText?: string;
  smsProperties?: Record<string, unknown>;
}) {
  return postApiAppNotificationManagementSendToUsers(data);
}

export async function getBroadcast(id: string) {
  return getApiAppNotificationManagementIdBroadcast({
    id,
  }) as Promise<NotificationBroadcast>;
}
