/**
 * 定时作业 API（T0.3 生成的客户端之上的薄封装）。
 * 页面只调本文件，不直接依赖生成代码的命名。
 */
import {
  deleteApiAppScheduledJobId,
  getApiAppScheduledJob,
  getApiAppScheduledJobIdExecutions,
  getApiAppScheduledJobJobTypes,
  postApiAppScheduledJob,
  postApiAppScheduledJobIdSetEnabled,
  postApiAppScheduledJobIdTrigger,
  postApiAppScheduledJobPreviewCron,
  putApiAppScheduledJobId,
} from '@/services/abpadmin/scheduledJob';

export interface ScheduledJob {
  id: string;
  name: string;
  jobType: string;
  cronExpression: string;
  isEnabled: boolean;
  description?: string | null;
  payload?: string | null;
  lastRunTime?: string | null;
  lastRunSuccess?: boolean | null;
  lastRunMessage?: string | null;
  nextRunTime?: string | null;
}

export interface ScheduledJobExecution {
  id: string;
  scheduledJobId: string;
  startTime: string;
  endTime?: string | null;
  success: boolean;
  message?: string | null;
  durationMs: number;
  creationTime: string;
}

export interface JobTypeOption {
  jobType: string;
  displayName: string;
}

export interface CronPreview {
  isValid: boolean;
  errorMessage?: string | null;
  nextFireTimes?: string[];
}

export async function getScheduledJobs(params: {
  current?: number;
  pageSize?: number;
  name?: string;
  jobType?: string;
  isEnabled?: boolean;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return getApiAppScheduledJob({
    SkipCount: skipCount,
    MaxResultCount: maxResultCount,
    Name: params.name,
    JobType: params.jobType,
    IsEnabled: params.isEnabled,
  }) as Promise<{ items?: ScheduledJob[]; totalCount?: number }>;
}

export async function createScheduledJob(data: {
  name: string;
  jobType: string;
  cronExpression: string;
  isEnabled?: boolean;
  description?: string;
  payload?: string;
}) {
  return postApiAppScheduledJob(data) as Promise<ScheduledJob>;
}

export async function updateScheduledJob(
  id: string,
  data: {
    name: string;
    cronExpression: string;
    description?: string;
    payload?: string;
  },
) {
  return putApiAppScheduledJobId({ id }, data) as Promise<ScheduledJob>;
}

export async function deleteScheduledJob(id: string) {
  return deleteApiAppScheduledJobId({ id });
}

export async function setJobEnabled(id: string, isEnabled: boolean) {
  return postApiAppScheduledJobIdSetEnabled({
    id,
    isEnabled,
  }) as Promise<ScheduledJob>;
}

export async function triggerJob(id: string) {
  return postApiAppScheduledJobIdTrigger({ id });
}

export async function getExecutions(
  id: string,
  params: { current?: number; pageSize?: number },
) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return getApiAppScheduledJobIdExecutions({
    id,
    SkipCount: skipCount,
    MaxResultCount: maxResultCount,
  }) as Promise<{ items?: ScheduledJobExecution[]; totalCount?: number }>;
}

export async function getJobTypes() {
  // skipErrorHandler：失败由调用页局部提示（它是下拉为空的直接原因），
  // 避免全局 errorHandler 再弹一次形成双提示（umi 全局处理后仍会 reject 到调用方）
  return getApiAppScheduledJobJobTypes({
    skipErrorHandler: true,
  }) as Promise<{ items?: JobTypeOption[] }>;
}

export async function previewCron(cronExpression: string) {
  return postApiAppScheduledJobPreviewCron({
    cronExpression,
  }) as Promise<CronPreview>;
}
