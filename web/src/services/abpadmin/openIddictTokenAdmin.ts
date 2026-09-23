// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 POST /api/app/open-iddict-token-admin/${param0}/revoke-authorization */
export async function postApiAppOpenIddictTokenAdminIdRevokeAuthorization(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppOpenIddictTokenAdminIdRevokeAuthorizationParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/app/open-iddict-token-admin/${param0}/revoke-authorization`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/open-iddict-token-admin/${param0}/revoke-token */
export async function postApiAppOpenIddictTokenAdminIdRevokeToken(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppOpenIddictTokenAdminIdRevokeTokenParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/app/open-iddict-token-admin/${param0}/revoke-token`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/open-iddict-token-admin/authorizations */
export async function getApiAppOpenIddictTokenAdminAuthorizations(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictTokenAdminAuthorizationsParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1OpenIddictAuthorizationDto>(
    "/api/app/open-iddict-token-admin/authorizations",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 手动清理过期令牌/授权。阈值=当前时间减 14 天保留期：刚兑换的 refresh token
依赖保留窗口做重放检测与级联吊销，立即清理会丢掉这层信号。
（后台 TokenCleanupBackgroundWorker 用同一 OpenIddict 语义周期清理，本方法用于即时运维。） POST /api/app/open-iddict-token-admin/prune */
export async function postApiAppOpenIddictTokenAdminPrune(options?: {
  [key: string]: any;
}) {
  return request<API.OpenIddictPruneResultDto>(
    "/api/app/open-iddict-token-admin/prune",
    {
      method: "POST",
      ...(options || {}),
    }
  );
}

/** 吊销某用户全部令牌与授权（等效强制其在所有客户端重新登录并重新授权）。 POST /api/app/open-iddict-token-admin/revoke-by-subject */
export async function postApiAppOpenIddictTokenAdminRevokeBySubject(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppOpenIddictTokenAdminRevokeBySubjectParams,
  options?: { [key: string]: any }
) {
  return request<number>("/api/app/open-iddict-token-admin/revoke-by-subject", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/open-iddict-token-admin/tokens */
export async function getApiAppOpenIddictTokenAdminTokens(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictTokenAdminTokensParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1OpenIddictTokenDto>(
    "/api/app/open-iddict-token-admin/tokens",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
