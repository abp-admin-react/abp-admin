// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/notification-service/notification */
export async function getApiNotificationServiceNotification(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiNotificationServiceNotificationParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1NotificationDto>(
    "/api/notification-service/notification",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/notification-service/notification/${param0} */
export async function getApiNotificationServiceNotificationId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiNotificationServiceNotificationIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.NotificationDto>(
    `/api/notification-service/notification/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
