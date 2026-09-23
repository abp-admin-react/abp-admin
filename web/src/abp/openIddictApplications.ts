import { request } from '@umijs/max';

/** 应用管理 DTO（与后端 OpenIddictApplicationDto 对齐；永远不含 ClientSecret/JWKS 等凭据材料） */
export type OpenIddictApplicationDto = {
  id: string;
  clientId?: string | null;
  displayName?: string | null;
  clientType?: string | null;
  applicationType?: string | null;
  consentType?: string | null;
  clientUri?: string | null;
  logoUri?: string | null;
  redirectUris?: string | null;
  postLogoutRedirectUris?: string | null;
  frontChannelLogoutUri?: string | null;
  allowAuthorizationCodeFlow?: boolean;
  allowImplicitFlow?: boolean;
  allowHybridFlow?: boolean;
  allowPasswordFlow?: boolean;
  allowClientCredentialsFlow?: boolean;
  allowRefreshTokenFlow?: boolean;
  allowTokenExchangeFlow?: boolean;
  allowDeviceAuthorizationFlow?: boolean;
  enableEndSessionEndpoint?: boolean;
  enablePushedAuthorizationEndpoint?: boolean;
  requirePkce?: boolean;
  requirePushedAuthorization?: boolean;
  scopes?: string[];
  extensionGrantTypes?: string[];
};

export type CreateOpenIddictApplicationInput = {
  clientId: string;
  displayName: string;
  clientType: string;
  applicationType: string;
  consentType: string;
  clientSecret?: string;
  jsonWebKeySet?: string;
  clientUri?: string;
  logoUri?: string;
  redirectUris?: string;
  postLogoutRedirectUris?: string;
  frontChannelLogoutUri?: string;
  allowAuthorizationCodeFlow?: boolean;
  allowImplicitFlow?: boolean;
  allowHybridFlow?: boolean;
  allowPasswordFlow?: boolean;
  allowClientCredentialsFlow?: boolean;
  allowRefreshTokenFlow?: boolean;
  allowTokenExchangeFlow?: boolean;
  allowDeviceAuthorizationFlow?: boolean;
  enableEndSessionEndpoint?: boolean;
  enablePushedAuthorizationEndpoint?: boolean;
  requirePkce?: boolean;
  requirePushedAuthorization?: boolean;
  scopes?: string[];
  extensionGrantTypes?: string[];
};

export type OpenIddictScopeDto = {
  id: string;
  name?: string | null;
  displayName?: string | null;
  description?: string | null;
  resources?: string | null;
};

/** scope 下拉列表项：托管 scope（数据库记录）+ 内置 scope（IsBuiltIn=true） */
export type OpenIddictScopeLookupDto = {
  name: string;
  displayName?: string | null;
  isBuiltIn: boolean;
};

type PagedResult<T> = { items: T[]; totalCount: number };

/** ProTable 分页参数 → ABP SkipCount/MaxResultCount */
function toSkipTake(params: { current?: number; pageSize?: number }) {
  const maxResultCount = params.pageSize ?? 10;
  return {
    SkipCount: ((params.current ?? 1) - 1) * maxResultCount,
    MaxResultCount: maxResultCount,
  };
}

export async function getOpenIddictApplications(params: {
  current?: number;
  pageSize?: number;
}) {
  return request<PagedResult<OpenIddictApplicationDto>>(
    '/api/app/open-iddict-application',
    {
      method: 'GET',
      params: toSkipTake(params),
    },
  );
}

export async function createOpenIddictApplication(
  data: CreateOpenIddictApplicationInput,
) {
  return request<OpenIddictApplicationDto>('/api/app/open-iddict-application', {
    method: 'POST',
    data,
  });
}

export async function updateOpenIddictApplication(
  id: string,
  data: CreateOpenIddictApplicationInput,
) {
  return request<OpenIddictApplicationDto>(
    `/api/app/open-iddict-application/${id}`,
    {
      method: 'PUT',
      data,
    },
  );
}

export async function deleteOpenIddictApplication(id: string) {
  return request(`/api/app/open-iddict-application/${id}`, {
    method: 'DELETE',
  });
}

export async function getOpenIddictScopes(params: {
  current?: number;
  pageSize?: number;
}) {
  return request<PagedResult<OpenIddictScopeDto>>(
    '/api/app/open-iddict-scope',
    {
      method: 'GET',
      params: toSkipTake(params),
    },
  );
}

export async function createOpenIddictScope(
  data: Pick<
    OpenIddictScopeDto,
    'name' | 'displayName' | 'description' | 'resources'
  >,
) {
  return request<OpenIddictScopeDto>('/api/app/open-iddict-scope', {
    method: 'POST',
    data,
  });
}

export async function updateOpenIddictScope(
  id: string,
  data: Pick<
    OpenIddictScopeDto,
    'name' | 'displayName' | 'description' | 'resources'
  >,
) {
  return request<OpenIddictScopeDto>(`/api/app/open-iddict-scope/${id}`, {
    method: 'PUT',
    data,
  });
}

export async function deleteOpenIddictScope(id: string) {
  return request(`/api/app/open-iddict-scope/${id}`, { method: 'DELETE' });
}

/** 托管 scope + 内置 scope（IsBuiltIn） */
export async function getOpenIddictScopeAll() {
  return request<{ items: OpenIddictScopeLookupDto[] }>(
    '/api/app/open-iddict-scope/all',
    { method: 'GET' },
  );
}

/** 按客户端的 token 生命周期覆盖（秒，null=使用服务器默认） */
export async function getOpenIddictApplicationTokenLifetime(id: string) {
  return request<Record<string, number | null>>(
    `/api/app/open-iddict-application/${id}/token-lifetime`,
    { method: 'GET' },
  );
}

export async function updateOpenIddictApplicationTokenLifetime(
  id: string,
  data: Record<string, number | null>,
) {
  return request<Record<string, number | null>>(
    `/api/app/open-iddict-application/${id}/token-lifetime`,
    { method: 'PUT', data },
  );
}

/** Client Credentials 代取 access token（secret 由调用者输入，不落地存储） */
export async function generateOpenIddictAccessToken(
  id: string,
  data: { clientSecret: string; scopes?: string[] },
) {
  return request<{
    accessToken: string;
    tokenType: string;
    expiresInSeconds: number;
    grantedScopes: string[];
  }>(`/api/app/open-iddict-application/${id}/generate-access-token`, {
    method: 'POST',
    data,
  });
}
