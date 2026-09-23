// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/my-notification */
export async function getApiAppMyNotification(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppMyNotificationParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1MyNotificationDto>(
    "/api/app/my-notification",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/my-notification/mark-all-as-read */
export async function postApiAppMyNotificationMarkAllAsRead(options?: {
  [key: string]: any;
}) {
  return request<any>("/api/app/my-notification/mark-all-as-read", {
    method: "POST",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/my-notification/unread-count */
export async function getApiAppMyNotificationUnreadCount(options?: {
  [key: string]: any;
}) {
  return request<API.UnreadCountDto>("/api/app/my-notification/unread-count", {
    method: "GET",
    ...(options || {}),
  });
}
