// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/notification-management */
export async function getApiAppNotificationManagement(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppNotificationManagementParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1NotificationListItemDto>(
    "/api/app/notification-management",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/notification-management/${param0} */
export async function getApiAppNotificationManagementId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppNotificationManagementIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.NotificationDetailDto>(
    `/api/app/notification-management/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/notification-management/${param0}/broadcast */
export async function getApiAppNotificationManagementIdBroadcast(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppNotificationManagementIdBroadcastParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.NotificationBroadcastDto>(
    `/api/app/notification-management/${param0}/broadcast`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/notification-management/${param0}/retry */
export async function postApiAppNotificationManagementIdRetry(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppNotificationManagementIdRetryParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/notification-management/${param0}/retry`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/notification-management/broadcast */
export async function postApiAppNotificationManagementBroadcast(
  body: API.BroadcastNotificationInput,
  options?: { [key: string]: any }
) {
  return request<string>("/api/app/notification-management/broadcast", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/notification-management/send-to-users */
export async function postApiAppNotificationManagementSendToUsers(
  body: API.SendToUsersNotificationInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/notification-management/send-to-users", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
