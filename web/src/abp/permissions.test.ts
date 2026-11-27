import { request } from '@umijs/max';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getPermissions,
  getRolePermissions,
  updatePermissions,
  updateRolePermissions,
} from './permissions';

vi.mock('@umijs/max', () => ({ request: vi.fn() }));

const changes = [{ name: 'Orders.Edit', isGranted: true }];
const path = '/api/permission-management/permissions';
const role = { id: 'role-1', name: '财务 & 审计' };
const rolePath = '/api/identity/roles/role-1/permissions';

describe('权限请求的上下文', () => {
  beforeEach(() => vi.clearAllMocks());

  it('角色读取和写入使用编辑器打开时的账号及租户', async () => {
    const context = { tenantId: 'editor-tenant', userId: 'editor-account' };
    const opts = {
      expectedTenantId: 'editor-tenant',
      expectedUserId: 'editor-account',
    };

    await getRolePermissions(role, context);
    await updateRolePermissions(role, changes, context);

    expect(request).toHaveBeenNthCalledWith(1, rolePath, {
      ...opts,
      method: 'GET',
      params: { ExpectedRoleName: role.name },
    });
    expect(request).toHaveBeenNthCalledWith(2, rolePath, {
      ...opts,
      method: 'PUT',
      data: { expectedRoleName: role.name, permissions: changes },
    });
  });

  it('宿主角色请求同样捕获明确的租户头状态', async () => {
    await getRolePermissions(role, { userId: 'host-account' });

    expect(request).toHaveBeenCalledWith(
      rolePath,
      expect.objectContaining({
        expectedTenantId: null,
        expectedUserId: 'host-account',
      }),
    );
  });

  it('未传上下文的通用用户权限请求沿用原接口选项', async () => {
    const params = { providerName: 'U', providerKey: 'target-account' };

    await getPermissions('U', 'target-account');
    await updatePermissions('U', 'target-account', changes);

    expect(request).toHaveBeenNthCalledWith(1, path, { method: 'GET', params });
    expect(request).toHaveBeenNthCalledWith(2, path, {
      method: 'PUT',
      params,
      data: { permissions: changes },
    });
  });
});
