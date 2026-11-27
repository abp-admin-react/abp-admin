import type { AppInitialState } from '@/app';
import { getApplicationConfiguration } from './config';
import { getTenantId } from './tenant';

type UpdateInitialState = (
  update: (state: AppInitialState | undefined) => AppInitialState | undefined,
) => Promise<void> | void;

let latestRefresh = 0;

/** 修改本人持有的角色后，重新读取有效授权；菜单通过 rolePermissionRevision 感知这次刷新。 */
export async function refreshRolePermissionSession(
  roleName: string,
  initialState: AppInitialState | undefined,
  setInitialState: UpdateInitialState,
) {
  const user = initialState?.currentUser;
  if (
    !initialState ||
    initialState.tenantMissing ||
    !user?.userid ||
    !user.roles?.includes(roleName)
  ) {
    return;
  }

  const tenantId = initialState.currentTenant?.id ?? undefined;
  const requestTenantId = getTenantId();
  // 共享存储的租户可能已被其他标签页切换，而此页模型仍是旧租户。
  if (requestTenantId && requestTenantId !== tenantId) return;

  const refresh = ++latestRefresh;
  const configuration = await getApplicationConfiguration();
  const refreshedUser = configuration.currentUser;
  if (refresh !== latestRefresh || getTenantId() !== requestTenantId) {
    return;
  }

  if (!refreshedUser?.isAuthenticated) {
    throw new Error('当前会话不可用');
  }
  if (
    refreshedUser.id !== user.userid ||
    (configuration.currentTenant?.id ?? undefined) !== tenantId
  ) {
    return;
  }

  const policies = configuration.auth?.grantedPolicies ?? {};
  await setInitialState((current) => {
    // 同一账号退出后重新登录，用户和租户 ID 都可能相同；新 initialState 有自己的 fetchUserInfo。
    if (
      refresh !== latestRefresh ||
      !current ||
      current.tenantMissing ||
      current.currentUser?.userid !== user.userid ||
      current.fetchUserInfo !== initialState.fetchUserInfo ||
      (current.currentTenant?.id ?? undefined) !== tenantId ||
      getTenantId() !== requestTenantId
    ) {
      return current;
    }

    return {
      ...current,
      currentUser: {
        ...current.currentUser,
        roles: refreshedUser.roles ?? [],
        access: policies['AbpIdentity.Users'] ? 'admin' : 'user',
      },
      grantedPolicies: policies,
      rolePermissionRevision: (current.rolePermissionRevision ?? 0) + 1,
    };
  });
}
