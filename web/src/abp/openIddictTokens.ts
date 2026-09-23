import { request } from '@umijs/max';

export type OpenIddictTokenDto = {
  id: string;
  applicationId?: string | null;
  applicationClientId?: string | null;
  authorizationId?: string | null;
  subject?: string | null;
  referenceId?: string | null;
  status?: string | null;
  type?: string | null;
  creationDate?: string | null;
  expirationDate?: string | null;
  redemptionDate?: string | null;
};

export type OpenIddictAuthorizationDto = {
  id: string;
  applicationId?: string | null;
  applicationClientId?: string | null;
  subject?: string | null;
  status?: string | null;
  type?: string | null;
  scopes?: string | null;
  creationDate?: string | null;
};

const BASE = '/api/app/open-iddict-token-admin';

export async function getOpenIddictTokens(params: {
  current?: number;
  pageSize?: number;
  subject?: string;
  applicationId?: string;
  type?: string;
  status?: string;
}) {
  return request<{ items: OpenIddictTokenDto[]; totalCount: number }>(
    `${BASE}/tokens`,
    {
      method: 'GET',
      params: {
        Subject: params.subject || undefined,
        ApplicationId: params.applicationId || undefined,
        Type: params.type || undefined,
        Status: params.status || undefined,
        SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
        MaxResultCount: params.pageSize ?? 10,
      },
    },
  );
}

export async function getOpenIddictAuthorizations(params: {
  current?: number;
  pageSize?: number;
  subject?: string;
  applicationId?: string;
  status?: string;
}) {
  return request<{ items: OpenIddictAuthorizationDto[]; totalCount: number }>(
    `${BASE}/authorizations`,
    {
      method: 'GET',
      params: {
        Subject: params.subject || undefined,
        ApplicationId: params.applicationId || undefined,
        Status: params.status || undefined,
        SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
        MaxResultCount: params.pageSize ?? 10,
      },
    },
  );
}

// 吊销端点走 ABP 约定路由：Revoke 前缀不属于约定动词前缀 → 默认 POST，
// 路径为 {id}/revoke-token|revoke-authorization（与 swagger 一致，勿改回 PUT）
export async function revokeOpenIddictToken(id: string) {
  return request(`${BASE}/${id}/revoke-token`, { method: 'POST' });
}

export async function revokeOpenIddictAuthorization(id: string) {
  return request(`${BASE}/${id}/revoke-authorization`, { method: 'POST' });
}

export async function revokeOpenIddictTokensBySubject(subject: string) {
  return request<number>(`${BASE}/revoke-by-subject`, {
    method: 'POST',
    params: { subject },
  });
}

export async function pruneOpenIddictTokens() {
  return request<{ prunedTokens: number; prunedAuthorizations: number }>(
    `${BASE}/prune`,
    { method: 'POST' },
  );
}
