import {
  ClearOutlined,
  DownloadOutlined,
  PlusOutlined,
  ReloadOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
import type {
  ColDef,
  GetRowIdParams,
  GridApi,
  ICellRendererParams,
} from 'ag-grid-community';
import { AgGridReact } from 'ag-grid-react';
import { App, Button, Checkbox, Popover, Space, Tag, Tooltip } from 'antd';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  exportIdentityUserQuery,
  getAssignableRoles,
  type IdentityRoleDto,
  queryIdentityUsers,
  type IdentityUserGridRowDto as User,
} from '@/abp/identity';
import ClaimModal from '@/components/ClaimModal';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import PermissionModal from '@/components/PermissionModal';
import {
  type RemoteColumn,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { inputHint } from '@/components/remoteGrid/validation';
import { ResetPasswordForm, UserForm } from './components/UserForms';
import UserRowActions from './components/UserRowActions';
import './index.less';

const statuses = {
  active: '正常',
  inactive: '停用',
  locked: '已冻结',
} as const;
const statusColors = {
  active: 'success',
  inactive: 'default',
  locked: 'warning',
} as const;
const dateFormat = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
  timeZone: 'UTC',
});
const userId = ({ data }: GetRowIdParams<User>) => data.id;
type Visibility = Record<string, boolean>;

function readVisibility(key: string): Visibility {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(key) ?? '{}');
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(
      Object.entries(value).filter(([, v]) => typeof v === 'boolean'),
    );
  } catch {
    return {};
  }
}

function yesNo({ data, value }: ICellRendererParams<User>) {
  if (!data) return null;
  return value == null ? (
    '—'
  ) : value ? (
    <Tag color="blue">是</Tag>
  ) : (
    <Tag>否</Tag>
  );
}

function UserList({ preferenceKey }: { preferenceKey: string }) {
  const api = useRef<GridApi<User> | undefined>(undefined);
  const { message } = App.useApp();
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const currentUserId = initialState?.currentUser?.userid;
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [editing, setEditing] = useState<User>();
  const [resetting, setResetting] = useState<User>();
  const [permissions, setPermissions] = useState<User>();
  const [claims, setClaims] = useState<User>();
  const [exporting, setExporting] = useState(false);
  const [initialVisibility] = useState(() => readVisibility(preferenceKey));
  const [visibility, setVisibility] = useState(initialVisibility);

  useEffect(() => {
    let current = true;
    if (access.canManageUserRoles) {
      getAssignableRoles()
        .then(({ items }) => {
          if (current)
            setRoles([
              ...new Map(items.map((role) => [role.name, role])).values(),
            ]);
        })
        .catch(() => undefined);
    }
    return () => {
      current = false;
    };
  }, [access.canManageUserRoles]);

  const reload = useCallback(() => api.current?.purgeInfiniteCache(), []);
  const columns = useMemo<RemoteColumn<User>[]>(() => {
    const list: RemoteColumn<User>[] = [
      {
        field: 'userName',
        headerName: '用户名',
        minWidth: 140,
        width: 212,
        pinned: 'left',
        lockVisible: true,
        initialSort: 'asc',
        cellRenderer: ({ value, data }: ICellRendererParams<User>) =>
          data && (
            <span className="identity-account">
              <span className="identity-account-initial" aria-hidden="true">
                {(value ?? '').slice(0, 1).toUpperCase()}
              </span>
              <strong>{value}</strong>
            </span>
          ),
      },
      {
        field: 'name',
        headerName: '姓名',
        width: 140,
        minWidth: 110,
        flex: 0.8,
      },
      {
        field: 'email',
        headerName: '邮箱',
        minWidth: 180,
        flex: 1.35,
        kind: access.canUpdateUsers ? 'text' : 'display',
      },
      {
        field: 'status',
        headerName: '状态',
        width: 136,
        choices: statuses,
        cellRenderer: ({ data }: ICellRendererParams<User>) =>
          data && (
            <Tag color={statusColors[data.status]}>{statuses[data.status]}</Tag>
          ),
      },
      {
        field: 'creationTime',
        headerName: '创建时间（UTC）',
        kind: 'date',
        width: 194,
        valueFormatter: ({ value }) =>
          value ? dateFormat.format(new Date(value)) : '',
      },
      {
        field: 'phoneNumber',
        headerName: '手机号',
        width: 160,
        initialHide: true,
        kind: access.canUpdateUsers ? 'text' : 'display',
      },
      {
        field: 'emailConfirmed',
        headerName: '邮箱已确认',
        width: 120,
        initialHide: true,
        kind: 'boolean',
        cellRenderer: yesNo,
      },
      {
        field: 'isLocked',
        headerName: '冻结',
        width: 106,
        initialHide: true,
        kind: 'boolean',
        cellRenderer: yesNo,
      },
      {
        field: 'twoFactorEnabled',
        headerName: '双因素',
        width: 110,
        initialHide: true,
        kind: access.canUpdateUsers ? 'boolean' : 'display',
        cellRenderer: yesNo,
      },
      {
        field: 'accessFailedCount',
        headerName: '失败次数',
        width: 112,
        initialHide: true,
        kind: 'number',
      },
      {
        field: 'id',
        headerName: '用户 ID',
        width: 300,
        initialHide: true,
        kind: 'uuid',
      },
    ];
    if (
      access.canUpdateUsers ||
      access.canManageUserPermissions ||
      access.canImpersonateUsers ||
      access.canDeleteUsers
    ) {
      list.push({
        colId: 'actions',
        headerName: '操作',
        pinned: 'right',
        lockVisible: true,
        kind: 'display',
        width: access.canUpdateUsers ? 208 : 94,
        minWidth: 80,
        cellRenderer: ({ data }: ICellRendererParams<User>) =>
          data && (
            <UserRowActions
              record={data}
              onEdit={setEditing}
              onReset={setResetting}
              onPermissions={setPermissions}
              onClaims={setClaims}
              onReload={reload}
            />
          ),
      });
    }
    return list;
  }, [
    access.canUpdateUsers,
    access.canManageUserPermissions,
    access.canImpersonateUsers,
    access.canDeleteUsers,
    reload,
  ]);
  const columnDefs = useMemo(
    () =>
      columns.map((col) =>
        col.field && !col.lockVisible && initialVisibility[col.field] != null
          ? { ...col, initialHide: !initialVisibility[col.field] }
          : col,
      ),
    [columns, initialVisibility],
  );
  const { gridProps, searchBox, hasFilters, clearFilters, getQuery } =
    useSearchableRemoteGrid<User>({
      columnDefs,
      query: queryIdentityUsers,
      getRowId: userId,
      ariaLabel: '用户管理',
      search: { label: '搜索用户' },
    });
  const [ready, setReady] = useState(false);

  function updateVisibility(column: ColDef<User>, visible: boolean) {
    if (!column.field) return;
    api.current?.setColumnsVisible([column.field], visible);
    const next = { ...visibility, [column.field]: visible };
    setVisibility(next);
    try {
      localStorage.setItem(preferenceKey, JSON.stringify(next));
    } catch {
      /* 浏览器不允许持久化时，列设置仍在本页生效。 */
    }
  }
  async function handleExport() {
    const input = getQuery();
    if (!input || exporting) return;
    const problem = inputHint(input, columns);
    if (problem) {
      message.warning(problem);
      return;
    }
    setExporting(true);
    try {
      const result = await exportIdentityUserQuery(input);
      if (result.isQueued)
        message.info(
          result.message || '导出任务已排队，完成后将通过邮件通知您。',
        );
      else message.success('导出文件已开始下载');
    } catch {
      // 请求错误由管理端统一提示。
    } finally {
      setExporting(false);
    }
  }

  return (
    <PageContainer {...identityOrgHelpHeader('users', '用户')}>
      <div className="identity-user-grid">
        <div className="identity-user-toolbar">
          {searchBox}
          <Space size={8} wrap>
            {hasFilters && (
              <Button icon={<ClearOutlined />} onClick={clearFilters}>
                清除筛选
              </Button>
            )}
            <Popover
              trigger="click"
              placement="bottomRight"
              title="显示列"
              content={
                <div className="identity-column-list">
                  {columns.map(
                    (col) =>
                      col.field &&
                      !col.lockVisible && (
                        <Checkbox
                          key={col.field}
                          checked={visibility[col.field] ?? !col.initialHide}
                          onChange={(e) =>
                            updateVisibility(col, e.target.checked)
                          }
                        >
                          {col.headerName}
                        </Checkbox>
                      ),
                  )}
                </div>
              }
            >
              <Button aria-label="列设置" icon={<SettingOutlined />}>
                列设置
              </Button>
            </Popover>
            <Tooltip title="刷新">
              <Button
                aria-label="刷新"
                icon={<ReloadOutlined />}
                onClick={reload}
              />
            </Tooltip>
            {access.canExportUsers && (
              <Button
                icon={<DownloadOutlined />}
                loading={exporting}
                disabled={!ready}
                onClick={handleExport}
              >
                导出
              </Button>
            )}
            {access.canCreateUsers && (
              <UserForm
                title="新建用户"
                trigger={
                  <Button type="primary" icon={<PlusOutlined />}>
                    新建用户
                  </Button>
                }
                roles={roles}
                canManageRoles={access.canManageUserRoles}
                onSuccess={reload}
              />
            )}
          </Space>
        </div>
        <AgGridReact<User>
          {...gridProps}
          rowHeight={46}
          onGridReady={(e) => {
            api.current = e.api;
            gridProps.onGridReady?.(e);
            setReady(true);
          }}
        />
      </div>
      {editing && (
        <UserForm
          title="编辑用户"
          record={editing}
          onClose={() => setEditing(undefined)}
          hideActive={editing.id === currentUserId}
          roles={roles}
          canManageRoles={access.canManageUserRoles}
          onSuccess={reload}
        />
      )}
      {resetting && (
        <ResetPasswordForm
          record={resetting}
          onSuccess={reload}
          onClose={() => setResetting(undefined)}
        />
      )}
      <PermissionModal
        open={!!permissions}
        title={`额外权限 - ${permissions?.userName || ''}`}
        providerName="U"
        providerKey={permissions?.id}
        onClose={() => setPermissions(undefined)}
      />
      {claims && (
        <ClaimModal
          key={claims.id}
          title={`声明 - ${claims.userName}`}
          userId={claims.id}
          onClose={() => setClaims(undefined)}
        />
      )}
    </PageContainer>
  );
}

export default function UsersPage() {
  const { initialState } = useModel('@@initialState');
  const preferenceKey = `identity-users:columns:v1:${initialState?.currentTenant?.id ?? 'host'}:${initialState?.currentUser?.userid ?? 'anonymous'}`;
  return <UserList key={preferenceKey} preferenceKey={preferenceKey} />;
}
