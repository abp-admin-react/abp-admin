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

/** 菜单管理页的表格列构建（从 index.tsx 抽离），操作回调由页面注入。
 * 全列定宽 + 表格 scroll.x：内容总宽超出窄视口时出横向滚动条，
 * 避免"名称逐字竖排/权限标签被裁没"这类挤压变形（宽屏下列宽照常拉伸）。
 * MENU_TABLE_SCROLL_X 必须等于下方各列 width 之和，增删列时同步维护。 */
export const MENU_TABLE_SCROLL_X = 1180;

export function buildMenuColumns(
  opts: BuildMenuColumnsOptions,
): ProColumns<MenuTreeDto>[] {
  return [
    {
      title: '名称',
      dataIndex: 'title',
      // 宽度需容纳层级缩进（antd 每级 15px）+ 展开钮 + 图标 + 最长标题；
      // name（国际化 key 尾段）不在主列表展示，查看/编辑入口在表单的"国际化 key"字段
      width: 200,
      render: (_, record) => (
        <Space>
          {toMenuIcon(record.icon)}
          {/* nowrap：固定表格布局下禁止标题折行，超宽交由列宽兜底 */}
          <span style={{ whiteSpace: 'nowrap' }}>{record.title}</span>
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
    { title: '路由地址', dataIndex: 'path', width: 190, ellipsis: true },
    {
      title: '绑定权限',
      dataIndex: 'permissionName',
      width: 210,
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
      width: 130,
      ellipsis: true,
      renderText: (_, record) =>
        record.grantedRoles.length > 0 ? record.grantedRoles.join('、') : '-',
    },
    { title: '排序', dataIndex: 'orderNo', width: 64 },
    {
      title: '状态',
      dataIndex: 'isEnabled',
      width: 100,
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
      width: 206,
      // 按钮可见性与后端校验同口径：新增子级仅目录（后端无父类型强校验，UI 先挡）、
      // 分配角色仅菜单类型、删除仅叶子节点（后端 MenuHasChildren 兜底）
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
