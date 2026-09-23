import type { ProColumns } from '@ant-design/pro-components';
import { Popconfirm, Space, Tag } from 'antd';
import React from 'react';
import { toMenuIcon } from '@/abp/menuIcons';
import { MENU_TYPE, type MenuTreeDto } from '@/abp/menus';

type BuildMenuColumnsOptions = {
  canUpdateMenus: boolean;
  canAssignMenuRoles: boolean;
  canDeleteMenus: boolean;
  onEdit: (node: MenuTreeDto) => void;
  onAddChild: (node: MenuTreeDto) => void;
  onRoles: (node: MenuTreeDto) => void;
  onDelete: (node: MenuTreeDto) => void | Promise<void>;
};

/** 菜单管理页的表格列构建（从 index.tsx 抽离），操作回调由页面注入。 */
export function buildMenuColumns(
  opts: BuildMenuColumnsOptions,
): ProColumns<MenuTreeDto>[] {
  return [
    {
      title: '名称',
      dataIndex: 'title',
      render: (_, record) => (
        <Space>
          {toMenuIcon(record.icon)}
          <span>{record.title}</span>
          {record.name ? (
            <Tag style={{ marginInlineEnd: 0 }}>{record.name}</Tag>
          ) : null}
        </Space>
      ),
    },
    {
      title: '类型',
      dataIndex: 'type',
      width: 80,
      valueEnum: {
        [MENU_TYPE.Catalog]: { text: '目录' },
        [MENU_TYPE.Menu]: { text: '菜单' },
      },
    },
    { title: '路由地址', dataIndex: 'path', ellipsis: true },
    {
      title: '绑定权限',
      dataIndex: 'permissionName',
      ellipsis: true,
      renderText: (v) =>
        v ? (
          <Tag color="blue" style={{ marginInlineEnd: 0 }}>
            {v}
          </Tag>
        ) : (
          '-'
        ),
    },
    {
      title: '已分配角色',
      dataIndex: 'grantedRoles',
      ellipsis: true,
      renderText: (_, record) =>
        record.grantedRoles.length > 0 ? record.grantedRoles.join('、') : '-',
    },
    { title: '排序', dataIndex: 'orderNo', width: 70 },
    {
      title: '状态',
      dataIndex: 'isEnabled',
      width: 110,
      render: (_, record) => (
        <Space size={4}>
          {record.isEnabled ? <Tag color="success">启用</Tag> : <Tag>停用</Tag>}
          {record.isHide ? <Tag color="warning">隐藏</Tag> : null}
        </Space>
      ),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 240,
      render: (_, record) =>
        [
          opts.canUpdateMenus ? (
            <a key="edit" onClick={() => opts.onEdit(record)}>
              编辑
            </a>
          ) : null,
          opts.canUpdateMenus && record.type === MENU_TYPE.Catalog ? (
            <a key="add-child" onClick={() => opts.onAddChild(record)}>
              新增子级
            </a>
          ) : null,
          opts.canAssignMenuRoles && record.type === MENU_TYPE.Menu ? (
            <a key="roles" onClick={() => opts.onRoles(record)}>
              分配角色
            </a>
          ) : null,
          opts.canDeleteMenus && record.children.length === 0 ? (
            <Popconfirm
              key="delete"
              title="确认删除该菜单？"
              onConfirm={() => opts.onDelete(record)}
            >
              <a style={{ color: '#ff4d4f' }}>删除</a>
            </Popconfirm>
          ) : null,
        ].filter(Boolean),
    },
  ];
}
