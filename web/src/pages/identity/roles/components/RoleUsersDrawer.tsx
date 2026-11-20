import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  Drawer,
  Button as GridButton,
  Space as GridControls,
  type TableColumnsType,
  Tag,
} from 'antd';
import React from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import type { IdentityRoleDto, RoleUserDto } from '@/abp/identity';
import { queryRoleUsers } from '@/abp/identity';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

type RoleUsersDrawerProps = {
  role: IdentityRoleDto | undefined;
  onClose: () => void;
};

function isLocked(user: RoleUserDto) {
  return !!user.lockoutEnd && new Date(user.lockoutEnd).getTime() > Date.now();
}

/**
 * 角色下的用户（只读抽屉）。分派/改派去用户管理页；
 * 这里只回答「谁拥有这个角色」——排查权限问题时的反向视角。
 */
const RoleUsersDrawer: React.FC<RoleUsersDrawerProps> = ({ role, onClose }) => {
  const access = useAccess();
  const columns: TableColumnsType<RoleUserDto> = [
    { title: '用户名', dataIndex: 'userName', width: 180 },
    { title: '姓名', dataIndex: 'name', width: 120 },
    { title: '邮箱', dataIndex: 'email', ellipsis: true },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 90,
      render: (_, record) =>
        !record.isActive ? (
          <Tag>禁用</Tag>
        ) : isLocked(record) ? (
          <Tag color="orange">已锁定</Tag>
        ) : (
          <Tag color="green">正常</Tag>
        ),
    },
    {
      title: '账号创建时间',
      dataIndex: 'creationTime',
      width: 170,
      render: (v: string) => new Date(v).toLocaleString(),
    },
  ];

  const grid = useSearchableRemoteGrid<RoleUserDto>({
    columnDefs: toGridColumns<RoleUserDto>(columns, {
      pro: false,
      kinds: {
        email: access.canUpdateUsers ? 'text' : 'display',
        isActive: 'boolean',
        creationTime: 'date',
      },
    }),
    ariaLabel: '角色成员',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        role
          ? queryRoleUsers(role.id, input, signal)
          : Promise.resolve({ items: [], totalCount: 0 }),
      [role?.id],
    ),
  });

  return (
    <Drawer
      title={`角色成员 - ${role?.name ?? ''}`}
      open={!!role}
      onClose={onClose}
      size={720}
      destroyOnHidden
    >
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}
      </GridControls>
      <AgGridReact<RoleUserDto> {...grid.gridProps} />
    </Drawer>
  );
};

export default RoleUsersDrawer;
