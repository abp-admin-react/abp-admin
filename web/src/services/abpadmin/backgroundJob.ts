// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/background-job */
export async function getApiAppBackgroundJob(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppBackgroundJobParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1BackgroundJobDto>(
    "/api/app/background-job",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/background-job/${param0} */
export async function getApiAppBackgroundJobId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppBackgroundJobIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.BackgroundJobDto>(`/api/app/background-job/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/background-job/${param0} */
export async function deleteApiAppBackgroundJobId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppBackgroundJobIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/background-job/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/background-job/${param0}/abandon */
export async function postApiAppBackgroundJobIdAbandon(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppBackgroundJobIdAbandonParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/background-job/${param0}/abandon`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/background-job/${param0}/retry */
export async function postApiAppBackgroundJobIdRetry(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppBackgroundJobIdRetryParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/background-job/${param0}/retry`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/background-job/enqueue-test */
export async function postApiAppBackgroundJobEnqueueTest(
  body: API.EnqueueTestBackgroundJobDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/background-job/enqueue-test", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
