import { SearchOutlined } from '@ant-design/icons';
import {
  Button,
  Input,
  Popconfirm,
  Space,
  type TableColumnsType,
  Tag,
} from 'antd';
import type { ColumnType } from 'antd/es/table';
import { toMenuIcon } from '@/abp/menuIcons';
import { MENU_TYPE, type MenuTreeDto } from '@/abp/menus';
import { type MenuFilters, type MenuRow, menuPermissions } from './menuTree';

type MenuActions = {
  canCreateMenus: boolean;
  canUpdateMenus: boolean;
  canDeleteMenus: boolean;
  onEdit: (node: MenuTreeDto) => void;
  onAddChild: (node: MenuTreeDto) => void;
  onDelete: (node: MenuTreeDto) => void | Promise<void>;
};

const collator = new Intl.Collator('zh-CN', {
  numeric: true,
  sensitivity: 'base',
});

// Ant Table 提供筛选控件，条件统一交给 queryMenuTree，保留没有直接命中的祖先。
function searchColumn(
  label: string,
): Pick<ColumnType<MenuRow>, 'filterDropdown' | 'filterIcon'> {
  return {
    filterIcon: (active) => (
      <SearchOutlined
        aria-label={`筛选${label}`}
        style={{ color: active ? 'var(--ant-color-primary)' : undefined }}
      />
    ),
    filterDropdown: ({
      selectedKeys,
      setSelectedKeys,
      confirm,
      clearFilters,
    }) => (
      <Space orientation="vertical" style={{ padding: 8 }}>
        <Input
          aria-label={`查询${label}`}
          placeholder={`输入${label}`}
          value={String(selectedKeys[0] ?? '')}
          allowClear
          onChange={(event) =>
            setSelectedKeys(event.target.value ? [event.target.value] : [])
          }
          onPressEnter={() => confirm()}
        />
        <Space>
          <Button type="primary" size="small" onClick={() => confirm()}>
            筛选
          </Button>
          <Button
            size="small"
            onClick={() =>
              clearFilters?.({ confirm: true, closeDropdown: true })
            }
          >
            重置
          </Button>
        </Space>
      </Space>
    ),
  };
}

export function buildMenuColumns(
  actions: MenuActions,
  filters: MenuFilters,
): TableColumnsType<MenuRow> {
  return [
    {
      title: '名称',
      key: 'title',
      width: 248,
      dataIndex: ['menu', 'title'],
      ...searchColumn('名称'),
      filteredValue: filters.title ?? null,
      sorter: (a, b) => collator.compare(a.menu.title, b.menu.title),
      render: (_, { menu }) => (
        <Space>
          {toMenuIcon(menu.icon)}
          <span>{menu.title}</span>
        </Space>
      ),
    },
    {
      title: '类型',
      key: 'type',
      width: 100,
      dataIndex: ['menu', 'type'],
      filters: [
        { text: '目录', value: MENU_TYPE.Catalog },
        { text: '菜单', value: MENU_TYPE.Menu },
      ],
      filteredValue: filters.type ?? null,
      sorter: (a, b) => a.menu.type - b.menu.type,
      render: (value: number) =>
        value === MENU_TYPE.Catalog ? '目录' : '菜单',
    },
    {
      title: '路由地址',
      key: 'path',
      dataIndex: ['menu', 'path'],
      width: 200,
      ellipsis: true,
      ...searchColumn('路由地址'),
      filteredValue: filters.path ?? null,
      sorter: (a, b) => collator.compare(a.menu.path ?? '', b.menu.path ?? ''),
    },
    {
      title: '绑定权限',
      key: 'permissionName',
      width: 230,
      ellipsis: true,
      ...searchColumn('绑定权限'),
      filteredValue: filters.permissionName ?? null,
      sorter: (a, b) =>
        collator.compare(
          menuPermissions(a.menu).join(' / '),
          menuPermissions(b.menu).join(' / '),
        ),
      render: (_, { menu }) =>
        menu.anyPermissionNames?.length ? (
          <span title={menu.anyPermissionNames.join(' / ')}>
            任一权限 · {menu.anyPermissionNames.join(' / ')}
          </span>
        ) : menu.permissionName ? (
          <Tag color="blue">{menu.permissionName}</Tag>
        ) : (
          '—'
        ),
    },
    {
      title: '排序',
      key: 'orderNo',
      dataIndex: ['menu', 'orderNo'],
      width: 92,
      ...searchColumn('排序值'),
      filteredValue: filters.orderNo ?? null,
      sorter: (a, b) => a.menu.orderNo - b.menu.orderNo,
    },
    {
      title: '状态',
      key: 'isEnabled',
      width: 126,
      filters: [
        { text: '启用', value: 'true' },
        { text: '停用', value: 'false' },
      ],
      filteredValue: filters.isEnabled ?? null,
      sorter: (a, b) => Number(a.menu.isEnabled) - Number(b.menu.isEnabled),
      render: (_, { menu }) => (
        <Space size={4}>
          {menu.isEnabled ? <Tag color="success">启用</Tag> : <Tag>停用</Tag>}
          {menu.isHide && <Tag color="warning">隐藏</Tag>}
        </Space>
      ),
    },
    {
      title: '操作',
      key: 'actions',
      width: 160,
      render: (_, { menu }) => (
        <Space size={8}>
          {actions.canUpdateMenus && (
            <Button
              size="small"
              type="link"
              style={{ padding: 0 }}
              onClick={() => actions.onEdit(menu)}
            >
              编辑
            </Button>
          )}
          {actions.canCreateMenus && menu.type === MENU_TYPE.Catalog && (
            <Button
              size="small"
              type="link"
              style={{ padding: 0 }}
              onClick={() => actions.onAddChild(menu)}
            >
              新增子级
            </Button>
          )}
          {actions.canDeleteMenus && menu.children.length === 0 && (
            <Popconfirm
              title="确认删除该菜单？"
              onConfirm={() => actions.onDelete(menu)}
            >
              <Button size="small" type="link" danger style={{ padding: 0 }}>
                删除
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];
}
