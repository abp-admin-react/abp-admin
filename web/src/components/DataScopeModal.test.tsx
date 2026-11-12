import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DataScopeType, type RoleDataScopeDto } from '@/abp/dataScope';
import DataScopeModal from './DataScopeModal';

const mocks = vi.hoisted(() => ({
  getRoleDataScopeByRoleName: vi.fn(),
  saveRoleDataScope: vi.fn(),
  getOrganizationUnits: vi.fn(),
}));

vi.mock('@umijs/max', () => ({ request: vi.fn() }));
vi.mock('@/abp/dataScope', async () => ({
  ...(await vi.importActual<typeof import('@/abp/dataScope')>('@/abp/dataScope')),
  getRoleDataScopeByRoleName: mocks.getRoleDataScopeByRoleName,
  saveRoleDataScope: mocks.saveRoleDataScope,
}));
vi.mock('@/abp/identityAdmin', () => ({
  getOrganizationUnits: mocks.getOrganizationUnits,
}));

function httpError(status: number) {
  return Object.assign(new Error(`HTTP ${status}`), { response: { status } });
}

function modal(roleName: string, onClose: () => void) {
  return (
    <AntdApp>
      <DataScopeModal
        open
        title={`数据范围 - ${roleName}`}
        roleName={roleName}
        onClose={onClose}
      />
    </AntdApp>
  );
}

const saveButton = () => screen.getByRole('button', { name: 'OK' });

describe('DataScopeModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getOrganizationUnits.mockResolvedValue([]);
    mocks.saveRoleDataScope.mockResolvedValue({ id: 'scope-1' });
  });

  it('404 未配置时保持空选择，必须主动选择范围才能保存', async () => {
    mocks.getRoleDataScopeByRoleName.mockRejectedValue(httpError(404));
    const onClose = vi.fn();
    render(modal('new-role', onClose));

    expect(await screen.findByText('该角色尚未配置数据范围')).toBeTruthy();
    expect(screen.getByText('请选择数据范围类型')).toBeTruthy();
    expect(screen.queryByText('全部数据')).toBeNull();
    // 非 Custom 不拉组织单元树
    expect(mocks.getOrganizationUnits).not.toHaveBeenCalled();
    await waitFor(() => expect(saveButton()).toBeEnabled());
    fireEvent.click(saveButton());

    await waitFor(() =>
      expect(screen.getAllByText('请选择数据范围类型').length).toBeGreaterThan(1),
    );
    expect(mocks.saveRoleDataScope).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.mouseDown(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('仅本人数据'));
    fireEvent.click(saveButton());
    await waitFor(() =>
      expect(mocks.saveRoleDataScope).toHaveBeenCalledWith({
        roleName: 'new-role',
        scopeType: DataScopeType.SelfOnly,
        customOrganizationUnitIds: [],
      }),
    );
    expect(mocks.getOrganizationUnits).not.toHaveBeenCalled();
  });

  it('仅在选择自定义组织单元时加载部门树', async () => {
    mocks.getRoleDataScopeByRoleName.mockRejectedValue(httpError(404));
    mocks.getOrganizationUnits.mockResolvedValue([
      { id: 'ou-1', displayName: '研发部', parentId: null },
    ]);
    render(modal('custom-role', vi.fn()));
    await screen.findByText('该角色尚未配置数据范围');
    expect(mocks.getOrganizationUnits).not.toHaveBeenCalled();

    fireEvent.mouseDown(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('自定义组织单元'));
    await waitFor(() => expect(mocks.getOrganizationUnits).toHaveBeenCalledOnce());
    expect(await screen.findByText('研发部')).toBeTruthy();
  });

  it('已配置时回填真实范围并按原值保存，不误报未配置', async () => {
    mocks.getRoleDataScopeByRoleName.mockResolvedValue({
      id: 'scope-1',
      scopeType: DataScopeType.SelfOnly,
      customOrganizationUnitIds: [],
    });
    render(modal('existing-role', vi.fn()));

    expect(await screen.findByText('仅本人数据')).toBeTruthy();
    expect(screen.queryByText('该角色尚未配置数据范围')).toBeNull();
    await waitFor(() => expect(saveButton()).toBeEnabled());
    fireEvent.click(saveButton());

    await waitFor(() =>
      expect(mocks.saveRoleDataScope).toHaveBeenCalledWith({
        roleName: 'existing-role',
        scopeType: DataScopeType.SelfOnly,
        customOrganizationUnitIds: [],
      }),
    );
  });

  it.each([403, 500])('读取 %d 时关闭且禁止保存，不当作未配置', async (status) => {
    mocks.getRoleDataScopeByRoleName.mockRejectedValue(httpError(status));
    const onClose = vi.fn();
    render(modal('existing-role', onClose));

    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(screen.queryByText('该角色尚未配置数据范围')).toBeNull();
    expect(saveButton()).toBeDisabled();
    fireEvent.click(saveButton());
    expect(mocks.saveRoleDataScope).not.toHaveBeenCalled();
  });

  it('切换角色读取中禁止保存上一角色的范围', async () => {
    mocks.getRoleDataScopeByRoleName.mockResolvedValueOnce({
      id: 'scope-old',
      scopeType: DataScopeType.All,
      customOrganizationUnitIds: [],
    });
    const onClose = vi.fn();
    const view = render(modal('old-role', onClose));
    await waitFor(() => expect(saveButton()).toBeEnabled());

    let finishRead!: (value: RoleDataScopeDto) => void;
    mocks.getRoleDataScopeByRoleName.mockImplementationOnce(
      () => new Promise<RoleDataScopeDto>((resolve) => { finishRead = resolve; }),
    );
    view.rerender(modal('next-role', onClose));
    expect(saveButton()).toBeDisabled();
    fireEvent.click(saveButton());
    expect(mocks.saveRoleDataScope).not.toHaveBeenCalled();
    expect(screen.queryByText('全部数据')).toBeNull();

    await act(async () => {
      finishRead({ scopeType: DataScopeType.CurrentOu });
    });
    await waitFor(() => expect(saveButton()).toBeEnabled());
    expect(screen.getByText('仅当前组织单元')).toBeTruthy();
  });
});
