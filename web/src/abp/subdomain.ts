/**
 * 子域名 → 租户同步。
 *
 * - 从 hostname 解析租户名（getTenantNameFromHost），再 findTenantByName。
 * - 返回：host（无子域）/ tenant（写入 localStorage）/ missing（登录前应去 /tenant-not-found）。
 * - 只在登录页等入口调用；不要在每个路由重复打 findTenant。
 */
import { findTenantByName } from './config';
import {
  getTenantNameFromHost,
  type StoredTenant,
  setStoredTenant,
} from './tenant';

export async function syncTenantFromSubdomain(): Promise<
  | { mode: 'host' }
  | { mode: 'tenant'; tenant: NonNullable<StoredTenant> }
  | { mode: 'missing'; name: string }
> {
  const name = getTenantNameFromHost();
  if (!name) {
    return { mode: 'host' };
  }
  try {
    const result = await findTenantByName(name);
    if (!result.success || !result.tenantId || result.isActive === false) {
      setStoredTenant(null);
      return { mode: 'missing', name };
    }
    const tenant = { id: result.tenantId, name: result.name || name };
    setStoredTenant(tenant);
    return { mode: 'tenant', tenant };
  } catch {
    setStoredTenant(null);
    return { mode: 'missing', name };
  }
}
