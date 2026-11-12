import { DownloadOutlined } from '@ant-design/icons';
import {
  type ActionType,
  PageContainer,
  type ProColumns,
} from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
import { App, Button, Dropdown, type MenuProps, Tag } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { impersonateUser } from '@/abp/account';
import {
  deleteUser,
  enqueueExportUsers,
  exportUsers,
  getAssignableRoles,
  getTwoFactorStatuses,
  getUsers,
  type IdentityRoleDto,
  type IdentityUserDto,
  isUserLocked,
  lockUser,
  requireChangePasswordOnNextLogin,
  setUserTwoFactorEnabled,
  unlockUser,
} from '@/abp/identity';
import { applyImpersonatedTokens } from '@/abp/oidc';
import { sorterToAbpSorting } from '@/abp/sorting';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import ClaimModal from '@/components/ClaimModal';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import PermissionModal from '@/components/PermissionModal';
import { firstFilterValue, textFilter } from '@/components/tableColumnFilters';
import { ResetPasswordForm, UserForm } from './components/UserForms';

/**
 * 同步导出阈值（与后端 AuditLogExportConsts.SyncThreshold 一致）：
 * 结果集超过该值时同步导出接口返回 400，必须走异步导出。
 */
const SYNC_EXPORT_THRESHOLD = 1000;

const UsersPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  // 统一使用 App.useApp() 的 message（消费 ConfigProvider 主题上下文），不再用静态 message
  const { message, modal } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const access = useAccess();
  const currentUserId = initialState?.currentUser?.userid;
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [permissionTarget, setPermissionTarget] = useState<IdentityUserDto>();
  const [claimTarget, setClaimTarget] = useState<IdentityUserDto>();
  // 搜索表单引用：导出时读取当前 filter（与列表 request 的 filter: params.userName 同一来源）

  useEffect(() => {
    if (!access.canManageUserRoles) return;
    getAssignableRoles()
      .then((result) =>
        setRoles([
          ...new Map(
            (result.items || []).map((role) => [role.name, role]),
          ).values(),
        ]),
      )
      .catch(() => undefined);
  }, [access.canManageUserRoles]);

  const handleExport = async () => {
    // 导出内容与分流阈值必须同一 filter：与列表共用受控列头筛选状态，
    // 并用当前 filter 现查 total 再分流（window.open 不会抛异常，不能靠“失败再回退”）
    const filter = userNameFilter;
    try {
      const { totalCount } = await getUsers({
        current: 1,
        pageSize: 1,
        filter,
      });
      if (totalCount > SYNC_EXPORT_THRESHOLD) {
        const result = await enqueueExportUsers(filter);
        if (result.isQueued) {
          message.info(
            result.message || '导出任务已排队，完成后将通过邮件通知您。',
          );
        }
        return;
      }
    } catch {
      // 错误由全局 errorHandler 统一提示
      return;
    }

    exportUsers(filter);
    message.success('导出文件已开始下载');
  };

  // 列头筛选受控状态：导出与列表同口径（导出读同一份状态）
  const [filters, setFilters] = useState<Record<string, React.Key[] | null>>(
    {},
  );
  const userNameFilter = firstFilterValue(filters, 'userName');

  const columns: ProColumns<IdentityUserDto>[] = [
    {
      title: '用户名',
      dataIndex: 'userName',
      width: 160,
      sorter: 'UserName',
      filteredValue: filters.userName,
      ...textFilter('按用户名筛选'),
    },
    {
      title: '邮箱',
      dataIndex: 'email',
      width: 220,
      search: false,
      sorter: 'Email',
    },
    { title: '姓名', dataIndex: 'name', search: false },
    {
      title: '邮箱已确认',
      dataIndex: 'emailConfirmed',
      width: 110,
      search: false,
      render: (_, record) =>
        record.emailConfirmed ? <Tag color="green">是</Tag> : <Tag>否</Tag>,
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 90,
      search: false,
      render: (_, record) =>
        !record.isActive ? (
          <Tag>停用</Tag>
        ) : isUserLocked(record) ? (
          <Tag color="orange">已冻结</Tag>
        ) : (
          <Tag color="green">正常</Tag>
        ),
    },
    {
      title: '冻结',
      dataIndex: 'lockoutEnd',
      width: 90,
      search: false,
      render: (_, record) => (isUserLocked(record) ? '是' : '否'),
    },
    {
      title: '双因素',
      dataIndex: 'twoFactorEnabled',
      width: 100,
      search: false,
      render: (_, record) =>
        record.twoFactorEnabled == null ? (
          '—'
        ) : record.twoFactorEnabled ? (
          <Tag color="blue">是</Tag>
        ) : (
          <Tag>否</Tag>
        ),
    },
    {
      title: '失败次数',
      dataIndex: 'accessFailedCount',
      width: 100,
      search: false,
    },
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      width: 170,
      search: false,
      valueType: 'dateTime',
      sorter: 'CreationTime',
    },
    {
      title: '操作',
      valueType: 'option',
      width: 230,
      fixed: 'right',
      render: (_, record) => {
        const isSelf = record.id === currentUserId;
        const items: MenuProps['items'] = [];
        if (access.canManageUserPermissions) {
          items.push({
            key: 'permissions',
            type: 'group',
            label: '通常通过角色授权',
            children: [
              {
                key: 'extra-permissions',
                label: '额外权限',
                onClick: () => setPermissionTarget(record),
              },
            ],
          });
        }
        if (access.canUpdateUsers) {
          if (access.canManageClaimTypes) {
            items.push({
              key: 'claims',
              label: '声明（高级）',
              onClick: () => setClaimTarget(record),
            });
          }
          items.push({
            key: 'lock',
            label: isUserLocked(record) ? '解冻' : '冻结',
            onClick: () =>
              modal.confirm({
                title: isUserLocked(record)
                  ? '确认解冻该用户？'
                  : '确认冻结该用户？',
                onOk: async () => {
                  if (isUserLocked(record)) {
                    await unlockUser(record.id);
                    message.success('已解冻');
                  } else {
                    await lockUser(record.id);
                    message.success('已冻结');
                  }
                  actionRef.current?.reload();
                },
              }),
          });
          if (!isSelf) {
            items.push(
              {
                key: 'force-change-pwd',
                label: '要求改密',
                onClick: () =>
                  modal.confirm({
                    title: '确认要求该用户下次登录时修改密码？',
                    onOk: async () => {
                      await requireChangePasswordOnNextLogin(record.id);
                      message.success(
                        '已设置，该用户下次登录时将被要求修改密码',
                      );
                    },
                  }),
              },
              {
                key: 'two-factor',
                label:
                  record.twoFactorEnabled == null
                    ? '双因素（状态未知）'
                    : record.twoFactorEnabled
                      ? '禁用双因素'
                      : '启用双因素',
                disabled: record.twoFactorEnabled == null,
                onClick: () =>
                  modal.confirm({
                    title: record.twoFactorEnabled
                      ? '确认禁用该用户的双因素认证？'
                      : '确认启用该用户的双因素认证？',
                    content: record.twoFactorEnabled
                      ? '禁用后该用户登录将不再需要第二因子。'
                      : '启用前请确认该用户至少有一个已确认的邮箱或手机号，否则登录时无法接收验证码。',
                    onOk: async () => {
                      await setUserTwoFactorEnabled(
                        record.id,
                        !record.twoFactorEnabled,
                      );
                      message.success(
                        record.twoFactorEnabled
                          ? '已禁用双因素认证'
                          : '已启用双因素认证',
                      );
                      actionRef.current?.reload();
                    },
                  }),
              },
            );
          }
        }
        if (!isSelf && access.canImpersonateUsers) {
          items.push({
            key: 'impersonate',
            label: '模拟此用户',
            onClick: () =>
              modal.confirm({
                title: `确认以用户「${record.userName}」的身份登录？`,
                content:
                  '您将以该用户的身份进入系统，可随时通过顶部提示条返回。',
                onOk: async () => {
                  try {
                    const result = await impersonateUser(record.id);
                    await applyImpersonatedTokens(result);
                    message.success('已切换到模拟身份');
                    window.location.reload();
                  } catch {
                    // 错误由全局 errorHandler 统一提示
                  }
                },
              }),
          });
        }
        if (!isSelf && access.canDeleteUsers) {
          items.push({
            key: 'delete',
            label: '删除用户',
            danger: true,
            onClick: () =>
              modal.confirm({
                title: '确认删除该用户？',
                onOk: async () => {
                  await deleteUser(record.id);
                  message.success('已删除');
                  actionRef.current?.reload();
                },
              }),
          });
        }
        return [
          access.canUpdateUsers && (
            <UserForm
              key="edit"
              title="编辑用户"
              trigger={<a>编辑</a>}
              roles={roles}
              canManageRoles={access.canManageUserRoles}
              record={record}
              hideActive={isSelf}
              onSuccess={() => actionRef.current?.reload()}
            />
          ),
          access.canUpdateUsers && (
            <ResetPasswordForm
              key="reset"
              record={record}
              onSuccess={() => actionRef.current?.reload()}
            />
          ),
          items.length > 0 && (
            <Dropdown key="more" menu={{ items }} trigger={['click']}>
              <a>更多</a>
            </Dropdown>
          ),
        ];
      },
    },
  ];

  return (
    <PageContainer {...identityOrgHelpHeader('users', '用户')}>
      <AutoHeightProTable<IdentityUserDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        scroll={{ x: 'max-content' }}
        columnsState={{
          defaultValue: {
            emailConfirmed: { show: false },
            lockoutEnd: { show: false },
            twoFactorEnabled: { show: false },
            accessFailedCount: { show: false },
          },
        }}
        search={false}
        onChange={(_pagination, nextFilters) => {
          setFilters(nextFilters as Record<string, React.Key[] | null>);
        }}
        request={async (params, sorter) => {
          const result = await getUsers({
            current: params.current,
            pageSize: params.pageSize,
            filter: userNameFilter,
            sorting: sorterToAbpSorting(sorter),
          });
          const items = result.items ?? [];
          // Volo 用户列表契约不含 twoFactorEnabled：页级批量补齐，
          // 否则 2FA 列恒为「否」、开关恒发 true（六透镜审查 function-High）
          let twoFactorByUser: Record<string, boolean> = {};
          if (items.length > 0 && access.canUpdateUsers) {
            try {
              const statuses = await getTwoFactorStatuses(
                items.map((item) => item.id),
              );
              twoFactorByUser = Object.fromEntries(
                statuses.map((s) => [s.userId, s.twoFactorEnabled]),
              );
            } catch {
              // 状态补齐失败不挡列表；未知状态不开放切换，防止把已启用误判为未启用。
              twoFactorByUser = {};
            }
          }
          return {
            data: items.map((item) => ({
              ...item,
              twoFactorEnabled: twoFactorByUser[item.id],
            })),
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() => [
          access.canExportUsers && (
            <Button
              key="export"
              icon={<DownloadOutlined />}
              onClick={handleExport}
            >
              导出
            </Button>
          ),
          access.canCreateUsers && (
            <UserForm
              key="create"
              title="新建用户"
              trigger={<Button type="primary">新建用户</Button>}
              roles={roles}
              canManageRoles={access.canManageUserRoles}
              onSuccess={() => actionRef.current?.reload()}
            />
          ),
        ]}
      />
      <PermissionModal
        open={!!permissionTarget}
        title={`额外权限 - ${permissionTarget?.userName || ''}`}
        providerName="U"
        providerKey={permissionTarget?.id}
        onClose={() => setPermissionTarget(undefined)}
      />
      {claimTarget && (
        <ClaimModal
          key={claimTarget.id}
          title={`声明 - ${claimTarget.userName}`}
          userId={claimTarget.id}
          onClose={() => setClaimTarget(undefined)}
        />
      )}
    </PageContainer>
  );
};

export default UsersPage;
