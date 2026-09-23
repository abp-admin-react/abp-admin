import { DownloadOutlined, UploadOutlined } from '@ant-design/icons';
import {
  type ActionType,
  PageContainer,
  type ProColumns,
  type ProFormInstance,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
import { App, Button, Modal, Popconfirm, Table, Tag, Upload } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { impersonateUser } from '@/abp/account';
import {
  deleteUser,
  downloadImportFailureReport,
  downloadImportTemplate,
  enqueueExportUsers,
  exportUsers,
  getAllRoles,
  getTwoFactorStatuses,
  getUsers,
  type IdentityRoleDto,
  type IdentityUserDto,
  importUsers,
  isUserLocked,
  lockUser,
  requireChangePasswordOnNextLogin,
  type UserImportResultDto,
  setUserTwoFactorEnabled,
  unlockUser,
} from '@/abp/identity';
import { applyImpersonatedTokens } from '@/abp/oidc';
import ClaimModal from '@/components/ClaimModal';
import PermissionModal from '@/components/PermissionModal';
import { ResetPasswordForm, UserForm } from './components/UserForms';

/**
 * 同步导出阈值（与后端 AuditLogExportConsts.SyncThreshold 一致）：
 * 结果集超过该值时同步导出接口返回 400，必须走异步导出。
 */
const SYNC_EXPORT_THRESHOLD = 1000;

const UsersPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  // 统一使用 App.useApp() 的 message（消费 ConfigProvider 主题上下文），不再用静态 message
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const access = useAccess();
  const currentUserId = initialState?.currentUser?.userid;
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [permissionTarget, setPermissionTarget] = useState<IdentityUserDto>();
  const [claimTarget, setClaimTarget] = useState<IdentityUserDto>();
  const [importResult, setImportResult] = useState<UserImportResultDto>();
  const [importModalOpen, setImportModalOpen] = useState(false);
  // 搜索表单引用：导出时读取当前 filter（与列表 request 的 filter: params.userName 同一来源）
  const searchFormRef = useRef<ProFormInstance>(undefined);

  useEffect(() => {
    getAllRoles()
      .then((result) => setRoles(result.items || []))
      .catch(() => undefined);
  }, []);

  const handleExport = async () => {
    // 导出内容与分流阈值必须同一 filter：表单值可能比上次列表请求新（改了条件未点查询），
    // 故用当前 filter 现查 total 再分流（window.open 不会抛异常，不能靠“失败再回退”）
    const filter = searchFormRef.current?.getFieldsValue()?.userName as
      | string
      | undefined;
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

  const handleImport = async (file: File) => {
    try {
      const result = await importUsers(file);
      setImportResult(result);
      setImportModalOpen(true);
      if (result.failedCount === 0) {
        message.success(`全部 ${result.totalCount} 条导入成功`);
      } else {
        message.warning(
          `导入完成：成功 ${result.succeededCount} 条，失败 ${result.failedCount} 条`,
        );
      }
      actionRef.current?.reload();
    } catch {
      message.error('导入失败，请检查文件格式');
    }
    return false; // 阻止 Upload 默认上传行为
  };

  const columns: ProColumns<IdentityUserDto>[] = [
    { title: '用户名', dataIndex: 'userName' },
    { title: '邮箱', dataIndex: 'email', search: false },
    { title: '姓名', dataIndex: 'name', search: false },
    {
      title: '邮箱已确认',
      dataIndex: 'emailConfirmed',
      search: false,
      render: (_, record) =>
        record.emailConfirmed ? <Tag color="green">是</Tag> : <Tag>否</Tag>,
    },
    {
      title: '启用',
      dataIndex: 'isActive',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    {
      title: '冻结',
      search: false,
      render: (_, record) => (isUserLocked(record) ? '是' : '否'),
    },
    {
      title: '双因素',
      dataIndex: 'twoFactorEnabled',
      search: false,
      render: (_, record) =>
        record.twoFactorEnabled ? <Tag color="blue">是</Tag> : <Tag>否</Tag>,
    },
    {
      title: '失败次数',
      dataIndex: 'accessFailedCount',
      search: false,
    },
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      search: false,
      valueType: 'dateTime',
    },
    {
      title: '操作',
      valueType: 'option',
      width: 360,
      render: (_, record) => {
        const isSelf = record.id === currentUserId;
        return [
          <UserForm
            key="edit"
            title="编辑用户"
            trigger={<a>编辑</a>}
            roles={roles}
            record={record}
            hideActive={isSelf}
            onSuccess={() => actionRef.current?.reload()}
          />,
          <a key="perms" onClick={() => setPermissionTarget(record)}>
            权限
          </a>,
          <a key="claims" onClick={() => setClaimTarget(record)}>
            声明
          </a>,
          <ResetPasswordForm
            key="reset"
            record={record}
            onSuccess={() => actionRef.current?.reload()}
          />,
          <Popconfirm
            key="lock"
            title={
              isUserLocked(record) ? '确认解冻该用户？' : '确认冻结该用户？'
            }
            onConfirm={async () => {
              if (isUserLocked(record)) {
                await unlockUser(record.id);
                message.success('已解冻');
              } else {
                await lockUser(record.id);
                message.success('已冻结');
              }
              actionRef.current?.reload();
            }}
          >
            <a>{isUserLocked(record) ? '解冻' : '冻结'}</a>
          </Popconfirm>,
          !isSelf && access.canImpersonateUsers && (
            <Popconfirm
              key="impersonate"
              title={`确认以用户「${record.userName}」的身份登录？`}
              description="您将以该用户的身份进入系统，可随时通过顶部提示条返回。"
              onConfirm={async () => {
                try {
                  const result = await impersonateUser(record.id);
                  await applyImpersonatedTokens(result);
                  message.success('已切换到模拟身份');
                  // 全量刷新，使 initialState / 权限 / 菜单按新身份重建
                  window.location.reload();
                } catch {
                  // 错误由全局 errorHandler 统一提示
                }
              }}
            >
              <a>模拟此用户</a>
            </Popconfirm>
          ),
          !isSelf && (
            <Popconfirm
              key="force-change-pwd"
              title="确认要求该用户下次登录时修改密码？"
              onConfirm={async () => {
                await requireChangePasswordOnNextLogin(record.id);
                message.success('已设置，该用户下次登录时将被要求修改密码');
              }}
            >
              <a>要求改密</a>
            </Popconfirm>
          ),
          !isSelf && (
            <Popconfirm
              key="two-factor"
              title={
                record.twoFactorEnabled
                  ? '确认禁用该用户的双因素认证？'
                  : '确认启用该用户的双因素认证？'
              }
              description={
                record.twoFactorEnabled
                  ? '禁用后该用户登录将不再需要第二因子。'
                  : '启用前请确认该用户至少有一个已确认的邮箱或手机号，否则登录时无法接收验证码。'
              }
              onConfirm={async () => {
                await setUserTwoFactorEnabled(record.id, !record.twoFactorEnabled);
                message.success(
                  record.twoFactorEnabled ? '已禁用双因素认证' : '已启用双因素认证',
                );
                actionRef.current?.reload();
              }}
            >
              <a>{record.twoFactorEnabled ? '禁用双因素' : '启用双因素'}</a>
            </Popconfirm>
          ),
          !isSelf && (
            <Popconfirm
              key="delete"
              title="确认删除该用户？"
              onConfirm={async () => {
                await deleteUser(record.id);
                message.success('已删除');
                actionRef.current?.reload();
              }}
            >
              <a>删除</a>
            </Popconfirm>
          ),
        ];
      },
    },
  ];

  return (
    <PageContainer>
      <ProTable<IdentityUserDto>
        rowKey="id"
        actionRef={actionRef}
        formRef={searchFormRef}
        columns={columns}
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          const result = await getUsers({
            current: params.current,
            pageSize: params.pageSize,
            filter: params.userName,
          });
          const items = result.items ?? [];
          // Volo 用户列表契约不含 twoFactorEnabled：页级批量补齐，
          // 否则 2FA 列恒为「否」、开关恒发 true（六透镜审查 function-High）
          let twoFactorByUser: Record<string, boolean> = {};
          if (items.length > 0) {
            try {
              const statuses = await getTwoFactorStatuses(
                items.map((item) => item.id),
              );
              twoFactorByUser = Object.fromEntries(
                statuses.map((s) => [s.userId, s.twoFactorEnabled]),
              );
            } catch {
              // 状态补齐失败不挡列表：列按未知渲染，操作仍可用（切换成功后 reload 会重试）
              twoFactorByUser = {};
            }
          }
          return {
            data: items.map((item) => ({
              ...item,
              twoFactorEnabled: twoFactorByUser[item.id] ?? false,
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
          access.canImportUsers && (
            <Upload
              key="import"
              // .xls（BIFF 老格式）MiniExcel 不支持、服务端明确拒绝：不再宣告，避免选中即整单失败
              accept=".xlsx,.csv"
              showUploadList={false}
              beforeUpload={(file) => {
                handleImport(file);
                return false;
              }}
            >
              <Button icon={<UploadOutlined />}>导入</Button>
            </Upload>
          ),
          access.canImportUsers && (
            <Button key="template" onClick={downloadImportTemplate}>
              下载模板
            </Button>
          ),
          <UserForm
            key="create"
            title="新建用户"
            trigger={<Button type="primary">新建用户</Button>}
            roles={roles}
            onSuccess={() => actionRef.current?.reload()}
          />,
        ]}
      />
      <PermissionModal
        open={!!permissionTarget}
        title={`权限 - ${permissionTarget?.userName || ''}`}
        providerName="U"
        providerKey={permissionTarget?.id}
        onClose={() => setPermissionTarget(undefined)}
      />
      <ClaimModal
        open={!!claimTarget}
        title={`声明 - ${claimTarget?.userName || ''}`}
        userId={claimTarget?.id}
        onClose={() => setClaimTarget(undefined)}
      />
      <Modal
        title="导入结果"
        open={importModalOpen}
        onCancel={() => setImportModalOpen(false)}
        footer={[
          importResult?.failureReportId && (
            <Button
              key="download-failures"
              onClick={() => {
                downloadImportFailureReport(importResult.failureReportId!);
              }}
            >
              下载失败明细
            </Button>
          ),
          <Button
            key="close"
            type="primary"
            onClick={() => setImportModalOpen(false)}
          >
            关闭
          </Button>,
        ]}
        width={640}
      >
        {importResult && (
          <>
            <p>
              共 {importResult.totalCount} 条，成功{' '}
              <span style={{ color: '#52c41a' }}>
                {importResult.succeededCount}
              </span>{' '}
              条，失败{' '}
              <span style={{ color: '#ff4d4f' }}>
                {importResult.failedCount}
              </span>{' '}
              条
            </p>
            {importResult.errors.length > 0 && (
              <Table
                dataSource={importResult.errors}
                rowKey="rowNumber"
                size="small"
                pagination={false}
                columns={[
                  { title: '行号', dataIndex: 'rowNumber', width: 60 },
                  { title: '用户名', dataIndex: 'userName', width: 120 },
                  { title: '失败原因', dataIndex: 'errorMessage' },
                ]}
              />
            )}
          </>
        )}
      </Modal>
    </PageContainer>
  );
};

export default UsersPage;
