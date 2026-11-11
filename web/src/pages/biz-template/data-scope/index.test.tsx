import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { describe, expect, it, vi } from 'vitest';
import access from '@/access';
import Page from './index';

const mocks = vi.hoisted(() => ({
  getAllRoles: vi.fn(),
  getBizDataScopeDemos: vi.fn(),
  getOrganizationUnits: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: () =>
    access({
      grantedPolicies: {
        'AbpAdmin.DataScopes.Manage': true,
        'AbpIdentity.Roles': true,
      },
    }),
  request: vi.fn(),
}));
vi.mock('@/abp/identity', () => ({ getAllRoles: mocks.getAllRoles }));
vi.mock('@/abp/identityAdmin', () => ({
  getOrganizationUnits: mocks.getOrganizationUnits,
}));
vi.mock('@/abp/bizDataScopeDemo', () => ({
  getBizDataScopeDemos: mocks.getBizDataScopeDemos,
  createBizDataScopeDemo: vi.fn(),
  deleteBizDataScopeDemo: vi.fn(),
}));
vi.mock('@/components/DataScopeModal', () => ({
  default: ({ open, roleName }: { open: boolean; roleName?: string }) =>
    open ? <div>正在配置：{roleName}</div> : null,
}));

describe('业务数据范围扩展入口', () => {
  it('保留原配置权限即可打开；未授业务示例权限时不请求示例数据', async () => {
    mocks.getAllRoles.mockResolvedValue({
      items: [{ id: 'r1', name: 'operator' }],
    });
    expect(
      access({
        grantedPolicies: {
          'AbpAdmin.DataScopes.Manage': true,
          'AbpIdentity.Roles': true,
        },
      }).canOpenDataScopeExtension,
    ).toBe(true);
    render(
      <AntdApp>
        <Page />
      </AntdApp>,
    );

    await waitFor(() => expect(mocks.getAllRoles).toHaveBeenCalledOnce());
    expect(mocks.getBizDataScopeDemos).not.toHaveBeenCalled();
    expect(mocks.getOrganizationUnits).not.toHaveBeenCalled();
    expect(screen.queryByText('新建')).toBeNull();
    fireEvent.mouseDown(screen.getByRole('combobox', { name: '示例角色' }));
    fireEvent.click(await screen.findByTitle('operator'));
    fireEvent.click(screen.getByRole('button', { name: '配置业务数据范围' }));
    expect(await screen.findByText('正在配置：operator')).toBeTruthy();
  });
});
