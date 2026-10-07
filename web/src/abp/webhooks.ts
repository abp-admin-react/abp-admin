import { request } from '@umijs/max';

/** Webhook 订阅（与后端 WebhookSubscriptionDto 镜像；密钥永不回传） */
export type WebhookSubscription = {
  id: string;
  webhookUri: string;
  hasSecret: boolean;
  description: string | null;
  isActive: boolean;
  events: string[];
};

/** Webhook 发送记录（终态：成功/最终失败） */
export type WebhookSendRecord = {
  id: string;
  subscriptionId: string;
  eventName: string;
  payload: string;
  succeeded: boolean;
  responseStatusCode: number | null;
  responseBody: string | null;
  attemptCount: number;
  lastAttemptTime: string;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

function page(params: { current?: number; pageSize?: number }) {
  const maxResultCount = params.pageSize ?? 20;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return { SkipCount: skipCount, MaxResultCount: maxResultCount };
}

export async function getWebhookSubscriptions(params: {
  current?: number;
  pageSize?: number;
  filter?: string | null;
  isActive?: boolean | null;
}): Promise<PagedResult<WebhookSubscription>> {
  const result = await request<PagedResult<WebhookSubscription>>(
    '/api/app/webhook-subscription',
    {
      method: 'GET',
      params: {
        ...page(params),
        Filter: params.filter,
        IsActive: params.isActive,
      },
    },
  );
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

export async function createWebhookSubscription(params: {
  webhookUri: string;
  secret: string;
  description?: string;
  isActive: boolean;
  events: string[];
}): Promise<WebhookSubscription> {
  return request('/api/app/webhook-subscription', {
    method: 'POST',
    data: {
      WebhookUri: params.webhookUri,
      Secret: params.secret,
      Description: params.description,
      IsActive: params.isActive,
      Events: params.events,
    },
  });
}

export async function updateWebhookSubscription(
  id: string,
  params: {
    webhookUri: string;
    /** 留空 = 保持原密钥 */
    secret?: string;
    description?: string;
    isActive: boolean;
    events: string[];
  },
): Promise<WebhookSubscription> {
  return request(`/api/app/webhook-subscription/${id}`, {
    method: 'PUT',
    data: {
      WebhookUri: params.webhookUri,
      Secret: params.secret,
      Description: params.description,
      IsActive: params.isActive,
      Events: params.events,
    },
  });
}

export async function deleteWebhookSubscription(id: string): Promise<void> {
  return request(`/api/app/webhook-subscription/${id}`, { method: 'DELETE' });
}

export async function getWebhookSendRecords(params: {
  current?: number;
  pageSize?: number;
  subscriptionId?: string | null;
  eventName?: string | null;
  succeeded?: boolean | null;
}): Promise<PagedResult<WebhookSendRecord>> {
  const result = await request<PagedResult<WebhookSendRecord>>(
    '/api/app/webhook-send-record',
    {
      method: 'GET',
      params: {
        ...page(params),
        SubscriptionId: params.subscriptionId,
        EventName: params.eventName,
        Succeeded: params.succeeded,
      },
    },
  );
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

export async function resendWebhookSendRecord(id: string): Promise<void> {
  return request(`/api/app/webhook-send-record/${id}/resend`, { method: 'POST' });
}

export async function deleteWebhookSendRecord(id: string): Promise<void> {
  return request(`/api/app/webhook-send-record/${id}`, { method: 'DELETE' });
}
