const KEY = 'abp.tenant';

export type StoredTenant = {
  id: string;
  name: string;
} | null;

export function getTenantNameFromHost(): string | undefined {
  if (typeof window === 'undefined') {
    return undefined;
  }
  const host = window.location.hostname.toLowerCase();
  if (host === 'localhost' || host === '127.0.0.1') {
    return undefined;
  }
  if (host.endsWith('.localhost')) {
    const name = host.slice(0, -'.localhost'.length);
    if (name && name !== 'www') {
      return name;
    }
  }
  return undefined;
}

export function isSubdomainTenantMode() {
  return !!getTenantNameFromHost();
}

export function getStoredTenant(): StoredTenant {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as StoredTenant) : null;
  } catch {
    return null;
  }
}

export function setStoredTenant(tenant: StoredTenant) {
  if (!tenant) {
    localStorage.removeItem(KEY);
    return;
  }
  localStorage.setItem(KEY, JSON.stringify(tenant));
}

export function getTenantId(): string | undefined {
  return getStoredTenant()?.id;
}

export function getAbpHeaders(): Record<string, string> {
  const headers: Record<string, string> = {};
  const tenantId = getTenantId();
  if (tenantId) {
    headers.__tenant = tenantId;
  }
  return headers;
}
