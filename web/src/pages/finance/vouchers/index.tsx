import type { ProFormInstance } from '@ant-design/pro-components';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProCard,
  type ProColumns,
  ProFormDigit,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { Link, useAccess, useLocation, useModel } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Descriptions,
  Drawer,
  Popconfirm,
  Space,
  Tag,
} from 'antd';
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import {
  ADJUST_STATUS_LABEL,
  type AdjustStatus,
  approveAdjust,
  createAdjust,
  deleteAdjust,
  FINANCE_ERROR,
  type FinManualAdjustDto,
  type FinVoucherDto,
  financeErrorCode,
  financeErrorFields,
  financeErrorText,
  finOwnerTypeName,
  getAdjusts,
  getVoucher,
  getVouchers,
  rejectAdjust,
  reopenAdjust,
  resolveFinanceUserId,
  reverseAdjust,
  reverseVoucher,
  submitAdjust,
  updateAdjust,
} from '@/abp/finance';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { financeHelpHeader } from '@/components/FinanceHelp';
import {
  FinanceEmpty,
  FinanceStateView,
  useFinanceLoad,
} from '@/components/FinanceState';
import { isValidRecordId } from '@/pages/collection/record-focus';
import { isNotFound } from '@/requestErrorConfig';
import { formatForDisplay } from '@/utils/time';
import {
  balanceAdjustmentUrl,
  voucherBusinessTypes,
  voucherEntriesUrl,
  voucherSourceUrl,
  depositRecordUrl,
  withdrawalRecordUrl,
} from './navigation';

type TabKey = 'vouchers' | 'drafts' | 'pending';

const VouchersPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const currentUser = initialState?.currentUser as
    | { id?: string; userid?: string; userName?: string; name?: string }
    | undefined;
  const meId = resolveFinanceUserId(currentUser);
  const meName = currentUser?.name || currentUser?.userName || '当前用户';

  const voucherRef = useRef<ActionType>(undefined);
  const draftRef = useRef<ActionType>(undefined);
  const pendingRef = useRef<ActionType>(undefined);
  const adjustFormRef = useRef<ProFormInstance>(undefined);
  const [pendingFieldErrors, setPendingFieldErrors] = useState<string[]>([]);

  /** 服务端字段名与表单字段名按大小写无关方式匹配。 */
  const ADJUST_FORM_FIELDS = [
    'amount',
    'currency',
    'debitAccountCode',
    'debitOwnerType',
    'debitOwnerId',
    'creditAccountCode',
    'reason',
  ];
  const highlightAdjustFields = (fields: string[]) => {
    const names = fields
      .map((f) =>
        ADJUST_FORM_FIELDS.find((n) => n.toLowerCase() === f.toLowerCase()),
      )
      .filter(Boolean) as string[];
    if (!names.length) return;
    adjustFormRef.current?.setFields(
      names.map((name) => ({ name, errors: ['请检查此项'] })),
    );
    adjustFormRef.current?.scrollToField?.(names[0]);
  };
  const [tab, setTab] = useState<TabKey>('vouchers');
  const [detail, setDetail] = useState<FinVoucherDto>();
  const voucherRequest = useRef(0);
  const [createOpen, setCreateOpen] = useState(false);
  const [editingAdjust, setEditingAdjust] = useState<FinManualAdjustDto>();
  const [adjustDetail, setAdjustDetail] = useState<FinManualAdjustDto>();
  const [rejecting, setRejecting] = useState<FinManualAdjustDto>();
  const load = useFinanceLoad();
  const canCreate = !!access.canCreateFinanceAdjust;
  const canApprove = !!access.canApproveFinanceAdjust;
  const canSubmit = !!access.canSubmitFinanceAdjust;
  const canReject = !!access.canRejectFinanceAdjust;
  const canApproveOwn = !!access.canApproveOwnFinanceAdjust;
  const canReverse = !!access.canReverseFinanceAdjust;
  const canViewWithdrawal =
    access.canViewWeb3Operations && access.canAdminWithdrawals;
  const canViewDeposit =
    access.canViewWeb3Operations &&
    (access.canViewCollections || access.canAdminNetwork);

  const auditor = useMemo(() => ({ id: meId, name: meName }), [meId, meName]);

  const reloadAdjusts = () => {
    draftRef.current?.reload();
    pendingRef.current?.reload();
    voucherRef.current?.reload();
  };

  /** 单据状态已变化时，重新读取列表并关闭过期详情。 */
  const onActionError = (e: unknown) => {
    message.error(financeErrorText(e));
    if (financeErrorCode(e) === FINANCE_ERROR.InvalidStatus) {
      reloadAdjusts();
      setAdjustDetail(undefined);
    }
  };

  const openVoucher = useCallback(
    async (id: string) => {
      const request = ++voucherRequest.current;
      try {
        const result = await getVoucher(id);
        if (request === voucherRequest.current) setDetail(result);
      } catch (error) {
        if (request === voucherRequest.current) {
          message.error(
            isNotFound(error) ? '找不到该张会计凭证' : financeErrorText(error),
          );
        }
      }
    },
    [message],
  );

  useEffect(() => {
    if (!pendingFieldErrors.length || !editingAdjust) return;
    const t = setTimeout(() => {
      highlightAdjustFields(pendingFieldErrors);
      setPendingFieldErrors([]);
    }, 50);
    return () => clearTimeout(t);
  }, [pendingFieldErrors, editingAdjust]);

  const location = useLocation();
  const search = new URLSearchParams(location.search);
  const bizType = search.get('bizType');
  const recordType =
    bizType === 'deposit' || bizType === 'withdraw' ? bizType : undefined;
  const recordLabel = recordType === 'deposit' ? '充值单' : '提现单';
  const deepVoucherId = recordType ? null : search.get('voucherId');
  const bizRefId = search.get('bizRefId');
  const focusedRecordId =
    recordType && bizRefId && isValidRecordId(bizRefId)
      ? bizRefId.toLowerCase()
      : undefined;
  const invalidRecordFilter = recordType && !focusedRecordId;
  const canViewRecord =
    recordType === 'deposit' ? canViewDeposit : canViewWithdrawal;
  useEffect(() => {
    setDetail(undefined);
    if (deepVoucherId || recordType) setTab('vouchers');
    if (deepVoucherId) {
      void openVoucher(deepVoucherId);
    }
    return () => {
      voucherRequest.current++;
    };
  }, [deepVoucherId, recordType, focusedRecordId, openVoucher]);

  const statusTag = (s: AdjustStatus) => {
    const cfg = ADJUST_STATUS_LABEL[s];
    return <Tag color={cfg.color}>{cfg.text}</Tag>;
  };

  const adjustColumns = (
    mode: 'drafts' | 'pending',
  ): ProColumns<FinManualAdjustDto>[] => [
    { title: '单号', dataIndex: 'adjustNo', width: 120 },
    { title: '摘要', dataIndex: 'summary', ellipsis: true },
    {
      title: '金额',
      dataIndex: 'amount',
      width: 100,
      render: (_: unknown, r: FinManualAdjustDto) =>
        `${r.amount.toFixed(2)} ${r.currency}`,
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_: unknown, r: FinManualAdjustDto) => statusTag(r.status),
    },
    { title: '申请人', dataIndex: 'applicantName', width: 100 },
    ...(mode === 'drafts'
      ? [
          {
            title: '驳回意见',
            dataIndex: 'rejectReason',
            ellipsis: true,
            render: (_: unknown, r: FinManualAdjustDto) =>
              r.rejectReason ? (
                <span style={{ color: '#d46b08' }}>{r.rejectReason}</span>
              ) : (
                '-'
              ),
          },
        ]
      : []),
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      search: false,
      render: (_: unknown, r: any) => formatForDisplay(r.creationTime),
      width: 170,
    },
    {
      title: '操作',
      valueType: 'option' as const,
      width: 240,
      render: (_: unknown, r: FinManualAdjustDto) => {
        const acts: React.ReactNode[] = [
          <a key="detail" onClick={() => setAdjustDetail(r)}>
            详情
          </a>,
        ];
        const isApplicant = r.applicantId === meId;
        if (mode === 'drafts' && canCreate && isApplicant) {
          if (r.status === 'Draft') {
            acts.push(
              <a key="edit" onClick={() => setEditingAdjust(r)}>
                编辑
              </a>,
              <Popconfirm
                key="del"
                title="确认删除该草稿？"
                onConfirm={async () => {
                  try {
                    await deleteAdjust(r.id);
                    message.success('已删除');
                    reloadAdjusts();
                  } catch (e) {
                    onActionError(e);
                  }
                }}
              >
                <a>删除</a>
              </Popconfirm>,
              ...(canSubmit
                ? [
                    <Popconfirm
                      key="submit"
                      title="提交后进入待审批，不可再改，确认？"
                      onConfirm={async () => {
                        try {
                          await submitAdjust(r.id);
                          message.success('已提交审批');
                          reloadAdjusts();
                        } catch (e) {
                          onActionError(e);
                          const bad = financeErrorFields(e);
                          if (bad.length && canCreate) {
                            setEditingAdjust(r);
                            setPendingFieldErrors(bad);
                          }
                        }
                      }}
                    >
                      <a>提交</a>
                    </Popconfirm>,
                  ]
                : []),
            );
          }
          // 驳回单据先回到草稿才能编辑。
          if (r.status === 'Rejected') {
            acts.push(
              <Popconfirm
                key="reopen"
                title="单据将回到草稿，可修改后再提交。驳回意见会保留。"
                onConfirm={async () => {
                  try {
                    await reopenAdjust(r.id, { id: meId, canCreate });
                    message.success('已回到草稿');
                    reloadAdjusts();
                  } catch (e) {
                    onActionError(e);
                  }
                }}
              >
                <a>重新编辑</a>
              </Popconfirm>,
            );
          }
        }
        // 本人单据需要额外的自批权限。
        if (
          mode === 'pending' &&
          (canApprove || canReject) &&
          r.status === 'Pending'
        ) {
          if (r.applicantId === meId && !canApproveOwn) {
            acts.push(
              <Tag key="self" color="orange">
                本人发起
              </Tag>,
            );
          } else {
            if (canApprove)
              acts.push(
                <Popconfirm
                  key="ok"
                  title="审批通过将生成凭证并过账，确认？"
                  onConfirm={async () => {
                    try {
                      await approveAdjust(r.id, auditor);
                      message.success('已审批并过账');
                      reloadAdjusts();
                    } catch (e) {
                      onActionError(e);
                    }
                  }}
                >
                  <a>审批</a>
                </Popconfirm>,
              );
            if (canReject)
              acts.push(
                <a key="reject" onClick={() => setRejecting(r)}>
                  驳回
                </a>,
              );
          }
        }
        if (canReverse && r.status === 'Posted') {
          acts.push(
            <Popconfirm
              key="adj-rev"
              title="冲销这笔账簿调账，确认？"
              onConfirm={async () => {
                try {
                  await reverseAdjust(r.id);
                  message.success('已冲销');
                  reloadAdjusts();
                } catch (e) {
                  onActionError(e);
                }
              }}
            >
              <a>冲销</a>
            </Popconfirm>,
          );
        }
        const voucherId = r.voucherId;
        if (voucherId) {
          acts.push(
            <a key="voucher" onClick={() => openVoucher(voucherId)}>
              查看会计凭证
            </a>,
          );
        }
        return acts;
      },
    },
  ];

  return (
    <PageContainer
      {...financeHelpHeader('A3', '会计凭证')}
      extra={
        <Space wrap>
          {access.canViewFinanceVouchers && (
            <Link to="/finance/transactions">分录查询</Link>
          )}
          {access.canViewFinanceBizDocs && (
            <Link to="/finance/biz-docs">凭证来源查询</Link>
          )}
          {load.ok && canCreate && (
            <Button type="primary" onClick={() => setCreateOpen(true)}>
              新建账簿调账
            </Button>
          )}
        </Space>
      }
    >
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        <ProCard
          key={load.nonce}
          tabs={{
            activeKey:
              (tab === 'drafts' && !canCreate) ||
              (tab === 'pending' && !(canApprove || canReject))
                ? 'vouchers'
                : tab,
            onChange: (k) => setTab(k as TabKey),
            items: [
              {
                key: 'vouchers',
                label: '凭证列表',
                children: invalidRecordFilter ? (
                  <Alert
                    type="warning"
                    showIcon
                    title={
                      recordType === 'deposit'
                        ? '充值凭证链接无效'
                        : '提现凭证链接无效'
                    }
                    action={<Link to="/finance/vouchers">查看全部凭证</Link>}
                  />
                ) : (
                  <>
                    {focusedRecordId && (
                      <Alert
                        type="info"
                        showIcon
                        title={`${recordLabel} ${focusedRecordId} 的会计凭证`}
                        style={{ marginBottom: 16 }}
                        action={
                          <Space wrap>
                            {canViewRecord && (
                              <Link
                                to={
                                  recordType === 'deposit'
                                    ? depositRecordUrl(focusedRecordId)
                                    : withdrawalRecordUrl(focusedRecordId)
                                }
                              >
                                返回{recordLabel}
                              </Link>
                            )}
                            <Link to="/finance/vouchers">查看全部凭证</Link>
                          </Space>
                        }
                      />
                    )}
                    <AutoHeightProTable<FinVoucherDto>
                      key={
                        focusedRecordId
                          ? `${recordType}:${focusedRecordId}`
                          : 'all'
                      }
                      rowKey="id"
                      actionRef={voucherRef}
                      locale={{
                        emptyText: (
                          <FinanceEmpty
                            text={
                              focusedRecordId
                                ? `未找到该${recordLabel}的凭证`
                                : '暂无凭证'
                            }
                          />
                        ),
                      }}
                      search={focusedRecordId ? false : { labelWidth: 'auto' }}
                      request={async (params) => {
                        const maxResultCount = params.pageSize ?? 20;
                        const skipCount =
                          ((params.current ?? 1) - 1) * maxResultCount;
                        const result = await load.guard(() =>
                          getVouchers({
                            skipCount,
                            maxResultCount,
                            filter: focusedRecordId
                              ? undefined
                              : params.voucherNo || params.summary,
                            bizType: focusedRecordId
                              ? recordType
                              : params.bizType,
                            bizRefId: focusedRecordId ?? params.bizRefId,
                          }),
                        );
                        if (!result)
                          return { data: [], total: 0, success: false };
                        return {
                          data: result.items,
                          total: result.totalCount,
                          success: true,
                        };
                      }}
                      columns={[
                        // 凭证号由 ID 派生，检索使用业务单号或摘要。
                        {
                          title: '凭证号',
                          dataIndex: 'voucherNo',
                          width: 150,
                          search: false,
                        },
                        {
                          title: '业务类型',
                          dataIndex: 'bizType',
                          width: 130,
                          valueEnum: voucherBusinessTypes,
                        },
                        {
                          title: '业务单号',
                          dataIndex: 'bizRefId',
                          width: 120,
                        },
                        {
                          title: '业务阶段',
                          dataIndex: 'phase',
                          width: 100,
                          search: false,
                        },
                        {
                          title: '期间',
                          dataIndex: 'periodLabel',
                          width: 90,
                          search: false,
                        },
                        {
                          title: '状态',
                          dataIndex: 'status',
                          width: 90,
                          search: false,
                          render: (_, r) => (
                            <Tag
                              color={
                                r.status === 'Posted' ? 'success' : 'error'
                              }
                            >
                              {r.status === 'Posted' ? '已过账' : '已冲销'}
                            </Tag>
                          ),
                        },
                        {
                          title: '借方合计',
                          dataIndex: 'debitTotal',
                          width: 100,
                          search: false,
                        },
                        {
                          title: '贷方合计',
                          dataIndex: 'creditTotal',
                          width: 100,
                          search: false,
                        },
                        {
                          title: '时间',
                          dataIndex: 'creationTime',
                          search: false,
                          render: (_: unknown, r: any) =>
                            formatForDisplay(r.creationTime),
                          width: 170,
                        },
                        {
                          title: '操作',
                          valueType: 'option',
                          width: 160,
                          render: (_, r) => {
                            const acts = [
                              <a key="d" onClick={() => openVoucher(r.id)}>
                                详情
                              </a>,
                            ];
                            if (
                              access.canReverseFinanceAdjust &&
                              r.status === 'Posted' &&
                              r.bizType === 'manual-adjust'
                            ) {
                              acts.push(
                                <Popconfirm
                                  key="rev"
                                  title="红冲将生成反向凭证，确认？"
                                  onConfirm={async () => {
                                    try {
                                      await reverseVoucher(r.id);
                                      message.success('已红冲');
                                      voucherRef.current?.reload();
                                    } catch (e) {
                                      message.error(financeErrorText(e));
                                    }
                                  }}
                                >
                                  <a>红冲</a>
                                </Popconfirm>,
                              );
                            }
                            return acts;
                          },
                        },
                      ]}
                    />
                  </>
                ),
              },
              {
                key: 'drafts',
                label: '账簿调账草稿',
                children: (
                  <AutoHeightProTable<FinManualAdjustDto>
                    rowKey="id"
                    actionRef={draftRef}
                    search={false}
                    locale={{
                      emptyText: (
                        <FinanceEmpty text="没有草稿或被驳回的账簿调账" />
                      ),
                    }}
                    params={{ tab: 'drafts' }}
                    request={async (params) => {
                      const maxResultCount = params.pageSize ?? 20;
                      const skipCount =
                        ((params.current ?? 1) - 1) * maxResultCount;
                      const result = await load.guard(() =>
                        getAdjusts({
                          skipCount,
                          maxResultCount,
                          status: 'Draft,Rejected',
                        }),
                      );
                      if (!result)
                        return { data: [], total: 0, success: false };
                      return {
                        data: result.items,
                        total: result.totalCount,
                        success: true,
                      };
                    }}
                    columns={adjustColumns('drafts')}
                  />
                ),
              },
              {
                key: 'pending',
                label: '账簿调账待审批',
                children: (
                  <AutoHeightProTable<FinManualAdjustDto>
                    rowKey="id"
                    actionRef={pendingRef}
                    search={false}
                    locale={{
                      emptyText: <FinanceEmpty text="暂无待审批的账簿调账" />,
                    }}
                    params={{ tab: 'pending' }}
                    request={async (params) => {
                      const maxResultCount = params.pageSize ?? 20;
                      const skipCount =
                        ((params.current ?? 1) - 1) * maxResultCount;
                      const result = await load.guard(() =>
                        getAdjusts({
                          skipCount,
                          maxResultCount,
                          status: 'Pending',
                        }),
                      );
                      if (!result)
                        return { data: [], total: 0, success: false };
                      return {
                        data: result.items,
                        total: result.totalCount,
                        success: true,
                      };
                    }}
                    columns={adjustColumns('pending')}
                  />
                ),
              },
            ].filter((it) =>
              it.key === 'drafts'
                ? canCreate
                : it.key === 'pending'
                  ? canApprove || canReject
                  : true,
            ),
          }}
        />
      </FinanceStateView>

      <ModalForm
        title={
          editingAdjust
            ? `编辑账簿调账 ${editingAdjust.adjustNo}`
            : '新建账簿调账（草稿）'
        }
        open={createOpen || !!editingAdjust}
        onOpenChange={(open) => {
          if (!open) {
            setCreateOpen(false);
            setEditingAdjust(undefined);
          }
        }}
        formRef={adjustFormRef}
        modalProps={{ destroyOnHidden: true, width: 560 }}
        initialValues={
          editingAdjust
            ? {
                amount: editingAdjust.amount,
                currency: editingAdjust.currency || 'CNY',
                debitAccountCode:
                  editingAdjust.lines?.[0]?.accountCode || '1002',
                // 保留原借方归属，归属类型转为下拉选项使用的名称。
                debitOwnerType: finOwnerTypeName(editingAdjust.debitOwnerType),
                debitOwnerId: editingAdjust.debitOwnerId || 'default',
                reason: editingAdjust.summary,
              }
            : {
                amount: 15,
                currency: 'CNY',
                debitAccountCode: '1002',
                debitOwnerType: 'Channel',
                debitOwnerId: 'default',
                reason: '',
              }
        }
        onFinish={async (values) => {
          try {
            const payload = {
              amount: Number(values.amount),
              currency: values.currency || 'CNY',
              debitAccountCode: String(values.debitAccountCode || '').trim(),
              debitOwnerType: values.debitOwnerType || 'Channel',
              debitOwnerId: values.debitOwnerId || 'default',
              creditAccountCode: values.creditAccountCode
                ? String(values.creditAccountCode).trim()
                : undefined,
              reason: values.reason || '',
            };
            if (!payload.debitAccountCode || !(payload.amount > 0)) {
              message.error('请填写金额与借方科目');
              return false;
            }
            if (editingAdjust) {
              await updateAdjust(editingAdjust.id, payload);
              setEditingAdjust(undefined);
              message.success('已保存');
            } else {
              await createAdjust(payload);
              message.success('已创建');
              setCreateOpen(false);
              setTab('drafts');
            }
            reloadAdjusts();
            return true;
          } catch (e) {
            onActionError(e);
            highlightAdjustFields(financeErrorFields(e));
            return false;
          }
        }}
      >
        <ProFormDigit
          name="amount"
          label="金额"
          min={0.01}
          rules={[{ required: true }]}
        />
        <ProFormText
          name="currency"
          label="币种"
          initialValue="CNY"
          rules={[{ required: true }]}
        />
        <ProFormText
          name="debitAccountCode"
          label="借方科目编码"
          rules={[{ required: true }]}
          placeholder="如 1002"
        />
        <ProFormSelect
          name="debitOwnerType"
          label="借方 OwnerType"
          options={[
            { value: 'Platform', label: 'Platform (0)' },
            { value: 'User', label: 'User (1)' },
            { value: 'Agent', label: 'Agent (2)' },
            { value: 'Channel', label: 'Channel (3)' },
          ]}
          rules={[{ required: true }]}
        />
        <ProFormText
          name="debitOwnerId"
          label="借方 OwnerId"
          rules={[{ required: true }]}
        />
        <ProFormText
          name="creditAccountCode"
          label="贷方科目编码（可空，默认 3101）"
          placeholder="留空=3101"
        />
        {/* reason：存草稿可空；submit 时后端校验，空则 400 + data.fields=reason，由 ADJUST_FORM_FIELDS 高亮 */}
        <ProFormText
          name="reason"
          label="原因"
          placeholder="草稿可不填，提交时必填"
        />
      </ModalForm>

      <ModalForm<{ rejectReason: string }>
        title={
          rejecting ? `驳回账簿调账 ${rejecting.adjustNo}` : '驳回账簿调账'
        }
        open={!!rejecting}
        onOpenChange={(v) => {
          if (!v) setRejecting(undefined);
        }}
        modalProps={{ destroyOnHidden: true, width: 480 }}
        onFinish={async (values) => {
          if (!rejecting) return false;
          try {
            await rejectAdjust(
              rejecting.id,
              auditor,
              values.rejectReason.trim(),
            );
            message.success('已驳回');
            setRejecting(undefined);
            reloadAdjusts();
            return true;
          } catch (e) {
            onActionError(e);
            if (financeErrorCode(e) === FINANCE_ERROR.InvalidStatus)
              setRejecting(undefined);
            return false;
          }
        }}
      >
        <ProFormTextArea
          name="rejectReason"
          label="驳回意见"
          placeholder="写清驳回原因，制单人会在单据上看到"
          fieldProps={{ rows: 4, maxLength: 500, showCount: true }}
          rules={[
            { required: true, whitespace: true, message: '请填写驳回意见' },
          ]}
        />
      </ModalForm>

      <Drawer
        title={
          adjustDetail ? `账簿调账 ${adjustDetail.adjustNo}` : '账簿调账详情'
        }
        open={!!adjustDetail}
        onClose={() => setAdjustDetail(undefined)}
        size={560}
      >
        {adjustDetail && (
          <>
            <Descriptions column={1} size="small">
              <Descriptions.Item label="状态">
                {statusTag(adjustDetail.status)}
              </Descriptions.Item>
              <Descriptions.Item label="摘要">
                {adjustDetail.summary}
              </Descriptions.Item>
              <Descriptions.Item label="金额">
                {adjustDetail.amount.toFixed(2)} {adjustDetail.currency}
              </Descriptions.Item>
              <Descriptions.Item label="申请人">
                {adjustDetail.applicantName}
              </Descriptions.Item>
              <Descriptions.Item label="审批人">
                {adjustDetail.auditorName || '-'}
              </Descriptions.Item>
              {/* rejectReason 一直保留：重新编辑、再提交后仍展示 */}
              {adjustDetail.rejectReason ? (
                <Descriptions.Item label="驳回意见">
                  <span style={{ color: '#d46b08' }}>
                    {adjustDetail.rejectReason}
                  </span>
                </Descriptions.Item>
              ) : null}
            </Descriptions>
            <AutoHeightProTable
              rowKey={(l: { accountCode: string }, i?: number) =>
                `${l.accountCode}-${i}`
              }
              search={false}
              pagination={false}
              toolBarRender={false}
              dataSource={adjustDetail.lines}
              columns={[
                { title: '科目', dataIndex: 'accountCode', width: 90 },
                { title: '借', dataIndex: 'debit', width: 90 },
                { title: '贷', dataIndex: 'credit', width: 90 },
                { title: '摘要', dataIndex: 'summary', ellipsis: true },
              ]}
            />
            {adjustDetail.status === 'Rejected' &&
              adjustDetail.applicantId === meId &&
              canCreate && (
                <Space style={{ marginTop: 16 }}>
                  <Button
                    type="primary"
                    onClick={async () => {
                      try {
                        await reopenAdjust(adjustDetail.id, {
                          id: meId,
                          canCreate,
                        });
                        message.success('已回到草稿');
                        setAdjustDetail(undefined);
                        reloadAdjusts();
                      } catch (e) {
                        onActionError(e);
                      }
                    }}
                  >
                    重新编辑
                  </Button>
                </Space>
              )}
          </>
        )}
      </Drawer>

      <Drawer
        title={detail ? `会计凭证 ${detail.voucherNo}` : '会计凭证详情'}
        open={!!detail}
        onClose={() => {
          voucherRequest.current++;
          setDetail(undefined);
        }}
        size={920}
        extra={
          detail && <Link to={voucherEntriesUrl(detail.id)}>查看分录</Link>
        }
      >
        {detail && (
          <>
            <Descriptions column={1} size="small">
              <Descriptions.Item label="业务类型">
                {voucherBusinessTypes[detail.bizType] ?? detail.bizType}
              </Descriptions.Item>
              <Descriptions.Item label="业务阶段">
                {detail.phase}
              </Descriptions.Item>
              <Descriptions.Item label="业务单号">
                <Space wrap>
                  {detail.bizRefId}
                  {detail.bizType === 'deposit' &&
                    detail.bizRefId &&
                    canViewDeposit &&
                    isValidRecordId(detail.bizRefId) && (
                      <Link to={depositRecordUrl(detail.bizRefId)}>
                        返回充值单
                      </Link>
                    )}
                  {detail.bizType === 'withdraw' &&
                    detail.bizRefId &&
                    canViewWithdrawal &&
                    isValidRecordId(detail.bizRefId) && (
                      <Link to={withdrawalRecordUrl(detail.bizRefId)}>
                        返回提现单
                      </Link>
                    )}
                  {detail.bizRefId &&
                  detail.bizType === 'wallet-adjust' &&
                  access.canViewBalanceAdjustments ? (
                    <Link to={balanceAdjustmentUrl(detail.bizRefId)}>
                      查看用户余额调账
                    </Link>
                  ) : detail.bizRefId && access.canViewFinanceBizDocs ? (
                    <Link to={voucherSourceUrl(detail.bizRefId)}>
                      查询凭证来源
                    </Link>
                  ) : null}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="期间">
                {detail.periodLabel}
              </Descriptions.Item>
              <Descriptions.Item label="状态">
                {detail.status === 'Posted' ? '已过账' : '已冲销'}
              </Descriptions.Item>
              <Descriptions.Item label="摘要">
                {detail.summary || '-'}
              </Descriptions.Item>
              <Descriptions.Item label="借贷合计">
                {detail.debitTotal} / {detail.creditTotal}
              </Descriptions.Item>
            </Descriptions>
            <AutoHeightProTable
              fillViewport={false}
              rowKey="seq"
              search={false}
              pagination={false}
              toolBarRender={false}
              dataSource={detail.entries || []}
              columns={[
                { title: '#', dataIndex: 'seq', width: 50 },
                { title: '科目', dataIndex: 'accountCode', width: 90 },
                { title: '名称', dataIndex: 'accountName', width: 120 },
                { title: '归属', dataIndex: 'ownerId', width: 150 },
                { title: '余额桶', dataIndex: 'bucket', width: 100 },
                { title: '借', dataIndex: 'debit', width: 90 },
                { title: '贷', dataIndex: 'credit', width: 90 },
                {
                  title: '变动后余额',
                  dataIndex: 'balanceAfterText',
                  width: 140,
                },
              ]}
            />
          </>
        )}
      </Drawer>
    </PageContainer>
  );
};

export default VouchersPage;
