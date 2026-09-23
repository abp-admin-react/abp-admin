// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/scheduled-job */
export async function getApiAppScheduledJob(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppScheduledJobParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1ScheduledJobDto>("/api/app/scheduled-job", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/scheduled-job */
export async function postApiAppScheduledJob(
  body: API.CreateScheduledJobDto,
  options?: { [key: string]: any }
) {
  return request<API.ScheduledJobDto>("/api/app/scheduled-job", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/scheduled-job/${param0} */
export async function getApiAppScheduledJobId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppScheduledJobIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ScheduledJobDto>(`/api/app/scheduled-job/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/scheduled-job/${param0} */
export async function putApiAppScheduledJobId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppScheduledJobIdParams,
  body: API.UpdateScheduledJobDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ScheduledJobDto>(`/api/app/scheduled-job/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/scheduled-job/${param0} */
export async function deleteApiAppScheduledJobId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppScheduledJobIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/scheduled-job/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/scheduled-job/${param0}/executions */
export async function getApiAppScheduledJobIdExecutions(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppScheduledJobIdExecutionsParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PagedResultDto1ScheduledJobExecutionDto>(
    `/api/app/scheduled-job/${param0}/executions`,
    {
      method: "GET",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/scheduled-job/${param0}/set-enabled */
export async function postApiAppScheduledJobIdSetEnabled(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppScheduledJobIdSetEnabledParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ScheduledJobDto>(
    `/api/app/scheduled-job/${param0}/set-enabled`,
    {
      method: "POST",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/scheduled-job/${param0}/trigger */
export async function postApiAppScheduledJobIdTrigger(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppScheduledJobIdTriggerParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/scheduled-job/${param0}/trigger`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/scheduled-job/job-types */
export async function getApiAppScheduledJobJobTypes(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1ScheduledJobTypeDto>(
    "/api/app/scheduled-job/job-types",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/scheduled-job/preview-cron */
export async function postApiAppScheduledJobPreviewCron(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppScheduledJobPreviewCronParams,
  options?: { [key: string]: any }
) {
  return request<API.CronPreviewDto>("/api/app/scheduled-job/preview-cron", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
