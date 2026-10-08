import { App, Drawer, Table, type TableColumnsType, Tag } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { getRoleUsers, type IdentityRoleDto, type RoleUserDto } from '@/abp/identity';

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
  const { message } = App.useApp();
  const [items, setItems] = useState<RoleUserDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10 });

  const load = useCallback(
    async (roleId: string, current: number, pageSize: number) => {
      setLoading(true);
      try {
        const res = await getRoleUsers({
          roleId,
          current,
          pageSize,
          sorting: 'UserName',
        });
        setItems(res.items);
        setTotal(res.totalCount);
      } catch {
        message.error('加载角色下用户失败');
      } finally {
        setLoading(false);
      }
    },
    [message],
  );

  useEffect(() => {
    if (!role) {
      return;
    }
    // 切换角色时重置到第一页，避免停留在越界页码
    setPagination({ current: 1, pageSize: 10 });
    load(role.id, 1, 10);
  }, [role?.id, load]);

  const columns: TableColumnsType<RoleUserDto> = [
    { title: '用户名', dataIndex: 'userName', width: 180 },
    { title: '姓名', dataIndex: 'name', width: 120 },
    { title: '邮箱', dataIndex: 'email', ellipsis: true },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 90,
      render: (_, record) =>
        !record.isActive
          ? <Tag>禁用</Tag>
          : isLocked(record)
            ? <Tag color="orange">已锁定</Tag>
            : <Tag color="green">正常</Tag>,
    },
    {
      title: '加入时间',
      dataIndex: 'creationTime',
      width: 170,
      render: (v: string) => new Date(v).toLocaleString(),
    },
  ];

  return (
    <Drawer
      title={`角色成员 - ${role?.name ?? ''}`}
      open={!!role}
      onClose={onClose}
      width={720}
      destroyOnHidden
    >
      <Table<RoleUserDto>
        rowKey="id"
        size="middle"
        loading={loading}
        columns={columns}
        dataSource={items}
        pagination={{
          current: pagination.current,
          pageSize: pagination.pageSize,
          total,
          showSizeChanger: true,
          showTotal: (t) => `共 ${t} 人`,
          onChange: (current, pageSize) => {
            setPagination({ current, pageSize });
            if (role) {
              load(role.id, current, pageSize);
            }
          },
        }}
      />
    </Drawer>
  );
};

export default RoleUsersDrawer;
