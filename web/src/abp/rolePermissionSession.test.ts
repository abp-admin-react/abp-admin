import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AppInitialState } from '@/app';
import { refreshRolePermissionSession } from './rolePermissionSession';
import type { AbpApplicationConfiguration } from './types';

const mocks = vi.hoisted(() => ({
  getApplicationConfiguration: vi.fn(),
  getTenantId: vi.fn(),
}));

vi.mock('./config', () => ({
  getApplicationConfiguration: mocks.getApplicationConfiguration,
}));
vi.mock('./tenant', () => ({ getTenantId: mocks.getTenantId }));

function configuration(
  grantedPolicies: Record<string, boolean>,
  tenantId = 'tenant-a',
): AbpApplicationConfiguration {
  return {
    currentUser: { id: 'user-a', isAuthenticated: true, roles: ['Auditor'] },
    currentTenant: { id: tenantId },
    auth: { grantedPolicies },
  };
}

function session() {
  const initialState: AppInitialState = {
    currentUser: { userid: 'user-a', roles: ['Auditor'], avatar: '/avatar' },
    currentTenant: { id: 'tenant-a' },
    grantedPolicies: { Before: true },
    fetchUserInfo: async () => undefined,
  };
  let current: AppInitialState | undefined = initialState;
  const setInitialState = vi.fn<
    Parameters<typeof refreshRolePermissionSession>[2]
  >((update) => {
    current = update(current);
  });
  return {
    initialState,
    setInitialState,
    getCurrent: () => current,
    replace: (next: AppInitialState) => {
      current = next;
    },
  };
}

describe('refreshRolePermissionSession', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.getTenantId.mockReturnValue('tenant-a');
  });

  it('replaces access and invalidates the menu for a role the current user holds', async () => {
    const store = session();
    mocks.getApplicationConfiguration.mockResolvedValue(
      configuration({ After: true }),
    );

    await refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );

    expect(store.getCurrent()).toMatchObject({
      grantedPolicies: { After: true },
      rolePermissionRevision: 1,
      currentUser: { roles: ['Auditor'], avatar: '/avatar' },
    });
    expect(store.getCurrent()?.grantedPolicies).not.toHaveProperty('Before');
  });

  it('skips other roles and a tenant already switched in another tab', async () => {
    const store = session();
    await refreshRolePermissionSession(
      'Reviewer',
      store.initialState,
      store.setInitialState,
    );
    mocks.getTenantId.mockReturnValue('tenant-b');
    await refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );

    expect(mocks.getApplicationConfiguration).not.toHaveBeenCalled();
  });

  it('ignores a response for a different tenant', async () => {
    const store = session();
    mocks.getApplicationConfiguration.mockResolvedValue(
      configuration({ Outside: true }, 'tenant-b'),
    );

    await refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );

    expect(store.setInitialState).not.toHaveBeenCalled();
  });

  it('does not update a replacement session for the same user and tenant', async () => {
    const store = session();
    const nextSession = session().initialState;
    const pending = Promise.withResolvers<AbpApplicationConfiguration>();
    mocks.getApplicationConfiguration.mockReturnValue(pending.promise);

    const refresh = refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );
    store.replace(nextSession);
    pending.resolve(configuration({ OldSession: true }));
    await refresh;

    expect(store.getCurrent()).toBe(nextSession);
  });

  it('keeps the newest refresh when responses complete out of order', async () => {
    const store = session();
    const old = Promise.withResolvers<AbpApplicationConfiguration>();
    mocks.getApplicationConfiguration
      .mockReturnValueOnce(old.promise)
      .mockResolvedValueOnce(configuration({ New: true }));

    const previous = refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );
    await refreshRolePermissionSession(
      'Auditor',
      store.initialState,
      store.setInitialState,
    );
    old.resolve(configuration({ Old: true }));
    await previous;

    expect(store.getCurrent()?.grantedPolicies).toEqual({ New: true });
    expect(store.getCurrent()?.rolePermissionRevision).toBe(1);
  });
});
