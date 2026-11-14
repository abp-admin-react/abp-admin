import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { history, useAccess, useLocation, useModel } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Descriptions,
  Drawer,
  Popconfirm,
  Space,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  approveBalanceAdjustment,
  type BalanceAdjustmentDto,
  type BalanceAdjustmentStatus,
  type CreateBalanceAdjustmentInput,
  createBalanceAdjustment,
  getBalanceAdjustment,
  getBalanceAdjustments,
  rejectBalanceAdjustment,
} from '@/abp/balanceAdjustments';
import { getUser, type IdentityUserDto } from '@/abp/identity';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { queryClient } from '@/queryClient';
import { isNotFound } from '@/requestErrorConfig';
import { formatForDisplay } from '@/utils/time';

const statusLabels = {
  0: { text: '待审批', color: 'processing' },
  1: { text: '已通过', color: 'success' },
  2: { text: '已拒绝', color: 'error' },
} as const;

const currencyLabels: Record<number, string> = { 3: 'USDT', 4: 'USDC' };
const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isValidAdjustmentAmount(value: string): boolean {
  if (!/^-?(?:0|[1-9]\d*)(?:\.\d{1,18})?$/.test(value) || !/[1-9]/.test(value))
    return false;
  const [whole, fraction = ''] = value.replace(/^-/, '').split('.');
  return (
    whole.length <= 10 && (whole !== '9999999999' || !/[1-9]/.test(fraction))
  );
}

export default function BalanceAdjustmentsPage() {
  const { message } = App.useApp();
  const access = useAccess();
  const location = useLocation();
  const { initialState } = useModel('@@initialState');
  const currentUserId = initialState?.currentUser?.userid;
  const actionRef = useRef<ActionType>(undefined);
  const query = new URLSearchParams(location.search);
  const statusParam = query.get('status');
  const status =
    statusParam === 'all' || statusParam === '1' || statusParam === '2'
      ? statusParam
      : '0';
  const adjustmentId = query.get('adjustmentId');
  const [creating, setCreating] = useState(false);
  const [requestId, setRequestId] = useState(() => crypto.randomUUID());
  const [pendingInput, setPendingInput] =
    useState<CreateBalanceAdjustmentInput>();
  const [detail, setDetail] = useState<BalanceAdjustmentDto>();
  const [rejecting, setRejecting] = useState<BalanceAdjustmentDto>();
  const [loadFailed, setLoadFailed] = useState(false);
  const [reviewingId, setReviewingId] = useState<string>();
  const [targetUser, setTargetUser] = useState<IdentityUserDto>();
  const createFor = query.get('createFor');
  const requestedUserId =
    access.canCreateBalanceAdjustments &&
    createFor &&
    guidPattern.test(createFor)
      ? createFor
      : undefined;
  const targetUserId = pendingInput?.userId ?? requestedUserId;
  const detailVoucherId = detail?.ledgerVoucherId;

  useEffect(() => {
    if (requestedUserId) setCreating(true);
  }, [requestedUserId]);

  useEffect(() => {
    setDetail(undefined);
    if (!adjustmentId || !guidPattern.test(adjustmentId)) return;
    let active = true;
    getBalanceAdjustment(adjustmentId)
      .then((result) => {
        if (active) setDetail(result);
      })
      .catch((error: unknown) => {
        if (active && isNotFound(error)) message.error('调账申请不存在');
      });
    return () => {
      active = false;
    };
  }, [adjustmentId, message]);

  useEffect(() => {
    setTargetUser(undefined);
    if (!targetUserId || !access.canManageUsers) return;
    let active = true;
    getUser(targetUserId)
      .then((user) => {
        if (active) setTargetUser(user);
      })
      .catch(() => {
        /* The user ID remains visible if the account lookup fails. */
      });
    return () => {
      active = false;
    };
  }, [access.canManageUsers, targetUserId]);

  function closeCreateForm() {
    setCreating(false);
    if (requestedUserId) history.replace(location.pathname);
  }

  function recordResult(result: BalanceAdjustmentDto) {
    void queryClient.invalidateQueries({ queryKey: ['workbench'] });
    setDetail(result);
    actionRef.current?.reload();
  }

  async function approve(row: BalanceAdjustmentDto) {
    setReviewingId(row.id);
    try {
      recordResult(await approveBalanceAdjustment(row.id));
      message.success('已审批并调整余额');
    } catch (error) {
      if (isNotFound(error)) message.error('调账申请不存在，请刷新列表');
      actionRef.current?.reload();
    } finally {
      setReviewingId(undefined);
    }
  }

  const columns: ProColumns<BalanceAdjustmentDto>[] = [
    {
      title: '申请 ID',
      dataIndex: 'id',
      width: 200,
      search: false,
      ellipsis: true,
    },
    { title: '用户 ID', dataIndex: 'userId', width: 200, ellipsis: true },
    { title: '原因', dataIndex: 'filter', hideInTable: true },
    {
      title: '调整金额',
      dataIndex: 'amount',
      width: 180,
      search: false,
      render: (_, row) =>
        `${row.amount.startsWith('-') ? '' : '+'}${row.amount} ${currencyLabels[row.currency] ?? row.currency}`,
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      search: false,
      render: (_, row) => (
        <Tag color={statusLabels[row.status].color}>
          {statusLabels[row.status].text}
        </Tag>
      ),
    },
    {
      title: '原因',
      dataIndex: 'reason',
      width: 220,
      search: false,
      ellipsis: true,
    },
    {
      title: '申请人',
      dataIndex: 'applicantId',
      width: 180,
      search: false,
      ellipsis: true,
    },
    {
      title: '申请时间',
      dataIndex: 'creationTime',
      width: 180,
      search: false,
      render: (_, row) => formatForDisplay(row.creationTime),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 200,
      render: (_, row) => {
        const canReviewOwn =
          !!currentUserId &&
          (row.applicantId !== currentUserId ||
            access.canApproveOwnBalanceAdjustments);
        return (
          <Space>
            <Button type="link" size="small" onClick={() => setDetail(row)}>
              详情
            </Button>
            {row.status === 0 &&
              canReviewOwn &&
              access.canApproveBalanceAdjustments && (
                <Popconfirm
                  title="通过后将调整用户可用余额并记账，确认通过？"
                  onConfirm={() => approve(row)}
                >
                  <Button
                    type="link"
                    size="small"
                    loading={reviewingId === row.id}
                  >
                    通过
                  </Button>
                </Popconfirm>
              )}
            {row.status === 0 &&
              canReviewOwn &&
              access.canRejectBalanceAdjustments && (
                <Button
                  type="link"
                  size="small"
                  danger
                  onClick={() => setRejecting(row)}
                >
                  拒绝
                </Button>
              )}
          </Space>
        );
      },
    },
  ];

  return (
    <PageContainer
      title="余额调整审批"
      extra={
        access.canCreateBalanceAdjustments ? (
          <Button type="primary" onClick={() => setCreating(true)}>
            新建调账申请
          </Button>
        ) : undefined
      }
    >
      <Tabs
        activeKey={status}
        onChange={(value) => {
          query.set('status', value);
          history.push(`${location.pathname}?${query}`);
        }}
        items={[
          { key: '0', label: '待审批' },
          { key: '1', label: '已通过' },
          { key: '2', label: '已拒绝' },
          { key: 'all', label: '全部' },
        ]}
      />
      {loadFailed && (
        <Alert
          type="error"
          title="加载调账申请失败"
          showIcon
          action={
            <Button onClick={() => actionRef.current?.reload()}>重试</Button>
          }
        />
      )}
      <AutoHeightProTable<BalanceAdjustmentDto>
        actionRef={actionRef}
        rowKey="id"
        columns={columns}
        params={{ statusFilter: status }}
        request={async (params) => {
          try {
            const result = await getBalanceAdjustments({
              skipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 20),
              maxResultCount: params.pageSize ?? 20,
              userId: params.userId,
              filter: params.filter,
              status:
                status === 'all'
                  ? undefined
                  : (Number(status) as BalanceAdjustmentStatus),
            });
            setLoadFailed(false);
            return {
              data: result.items,
              total: result.totalCount,
              success: true,
            };
          } catch {
            setLoadFailed(true);
            return { data: [], total: 0, success: false };
          }
        }}
      />
      <ModalForm<{
        userId: string;
        currency: number;
        amount: string;
        reason: string;
      }>
        key={requestedUserId ?? 'manual'}
        title="新建调账申请"
        open={creating}
        onOpenChange={(open) => {
          if (open) setCreating(true);
          else closeCreateForm();
        }}
        modalProps={{ destroyOnHidden: true }}
        initialValues={pendingInput ?? { currency: 3, userId: requestedUserId }}
        submitter={{
          searchConfig: {
            submitText: pendingInput ? '重试原申请' : '提交申请',
          },
        }}
        onFinish={async (values) => {
          const input = pendingInput ?? {
            requestId,
            userId: requestedUserId ?? values.userId.trim(),
            currency: values.currency,
            amount: values.amount.trim(),
            reason: values.reason.trim(),
          };
          setPendingInput(input);
          try {
            await createBalanceAdjustment(input);
            void queryClient.invalidateQueries({ queryKey: ['workbench'] });
            closeCreateForm();
            setPendingInput(undefined);
            setRequestId(crypto.randomUUID());
            actionRef.current?.reload();
            message.success('调账申请已提交，等待审批');
            return true;
          } catch (error) {
            if (isNotFound(error))
              message.error('未找到对应用户，请核对用户 ID');
            const responseStatus = (error as { response?: { status?: number } })
              .response?.status;
            if (responseStatus && responseStatus >= 400 && responseStatus < 500)
              setPendingInput(undefined);
            return false;
          }
        }}
      >
        {pendingInput && (
          <Alert
            type="info"
            showIcon
            title="申请结果待确认，重试将沿用原申请内容和请求号。"
          />
        )}
        {targetUser && targetUser.id === targetUserId && (
          <Typography.Paragraph>
            调整对象：{targetUser.userName}
            {targetUser.email ? `（${targetUser.email}）` : ''}
          </Typography.Paragraph>
        )}
        <ProFormText
          name="userId"
          label="用户 ID"
          disabled={!!targetUserId}
          placeholder="填写用户 ID，或从用户余额页发起"
          rules={[
            {
              required: true,
              pattern: guidPattern,
              message: '请输入有效的用户 ID',
            },
          ]}
        />
        <ProFormSelect
          name="currency"
          label="币种"
          disabled={!!pendingInput}
          options={[
            { label: 'USDT', value: 3 },
            { label: 'USDC', value: 4 },
          ]}
          rules={[{ required: true }]}
        />
        <ProFormText
          name="amount"
          label="调整金额"
          disabled={!!pendingInput}
          placeholder="正数增加，负数扣减"
          rules={[
            { required: true, message: '请输入调整金额' },
            {
              validator: async (_, value) => {
                if (!isValidAdjustmentAmount(String(value ?? '').trim()))
                  throw new Error(
                    '请输入非零十进制数，最多 18 位小数，绝对值不超过 9999999999',
                  );
              },
            },
          ]}
        />
        <ProFormTextArea
          name="reason"
          label="调账原因"
          disabled={!!pendingInput}
          fieldProps={{ maxLength: 512, showCount: true }}
          rules={[
            { required: true, whitespace: true, message: '请填写调账原因' },
          ]}
        />
      </ModalForm>
      <ModalForm<{ reason: string }>
        title="拒绝调账申请"
        open={!!rejecting}
        onOpenChange={(open) => {
          if (!open) setRejecting(undefined);
        }}
        modalProps={{ destroyOnHidden: true }}
        onFinish={async (values) => {
          if (!rejecting) return false;
          try {
            recordResult(
              await rejectBalanceAdjustment(rejecting.id, values.reason.trim()),
            );
            setRejecting(undefined);
            message.success('已拒绝调账申请');
            return true;
          } catch (error) {
            if (isNotFound(error)) message.error('调账申请不存在，请刷新列表');
            actionRef.current?.reload();
            return false;
          }
        }}
      >
        <ProFormTextArea
          name="reason"
          label="拒绝原因"
          fieldProps={{ maxLength: 512 }}
          rules={[
            { required: true, whitespace: true, message: '请填写拒绝原因' },
          ]}
        />
      </ModalForm>
      <Drawer
        title="调账申请详情"
        size={600}
        open={!!detail}
        onClose={() => {
          setDetail(undefined);
          if (adjustmentId) {
            query.delete('adjustmentId');
            history.replace(
              query.size ? `${location.pathname}?${query}` : location.pathname,
            );
          }
        }}
      >
        {detail && (
          <Descriptions
            column={1}
            size="small"
            items={[
              { key: 'id', label: '申请 ID', children: detail.id },
              { key: 'user', label: '用户 ID', children: detail.userId },
              { key: 'account', label: '账户', children: '用户可用余额' },
              {
                key: 'amount',
                label: '调整金额',
                children: `${detail.amount} ${currencyLabels[detail.currency] ?? detail.currency}`,
              },
              { key: 'reason', label: '调账原因', children: detail.reason },
              {
                key: 'status',
                label: '状态',
                children: statusLabels[detail.status].text,
              },
              {
                key: 'applicant',
                label: '申请人',
                children: detail.applicantId,
              },
              {
                key: 'created',
                label: '申请时间',
                children: formatForDisplay(detail.creationTime),
              },
              {
                key: 'reviewer',
                label: '审批人',
                children: detail.reviewerId ?? '—',
              },
              {
                key: 'reviewed',
                label: '审批时间',
                children: detail.reviewedAt
                  ? formatForDisplay(detail.reviewedAt)
                  : '—',
              },
              {
                key: 'note',
                label: '审批意见',
                children: detail.reviewNote ?? '—',
              },
              {
                key: 'txn',
                label: '余额流水 ID',
                children: detail.balanceTxnId ?? '—',
              },
              {
                key: 'balance',
                label: '审批后可用余额',
                children:
                  detail.balanceAfter == null
                    ? '—'
                    : `${detail.balanceAfter} ${currencyLabels[detail.currency] ?? detail.currency}`,
              },
              {
                key: 'voucher',
                label: '记账凭证',
                children:
                  detailVoucherId && access.canViewFinanceVouchers ? (
                    <Button
                      type="link"
                      size="small"
                      onClick={() =>
                        history.push(
                          `/finance/transactions?voucherId=${encodeURIComponent(detailVoucherId)}`,
                        )
                      }
                    >
                      查看分录
                    </Button>
                  ) : (
                    (detail.ledgerVoucherId ?? '—')
                  ),
              },
            ]}
          />
        )}
      </Drawer>
    </PageContainer>
  );
}
