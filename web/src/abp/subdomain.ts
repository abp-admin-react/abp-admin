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
