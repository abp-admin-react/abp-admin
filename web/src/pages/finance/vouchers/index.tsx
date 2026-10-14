import FinanceHelpTitle from '@/components/FinanceHelp';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProCard,
  ProFormDigit,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import {
  App,
  Button,
  Descriptions,
  Drawer,
  Popconfirm,
  Space,
  Tag,
} from 'antd';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import { formatForDisplay, rangeToUtcBounds } from '@/utils/time';
import React, { useEffect, useMemo, useRef, useState } from 'react';
import type { ProFormInstance } from '@ant-design/pro-components';
import { Access, useAccess, useModel } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  ADJUST_STATUS_LABEL,
  type AdjustStatus,
  type FinManualAdjustDto,
  type FinVoucherDto,
  approveAdjust,
  FINANCE_ERROR,
  createAdjust,
  deleteAdjust,
  financeErrorCode,
  financeErrorText,
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
  financeErrorFields,
} from '@/abp/finance';

type TabKey = 'vouchers' | 'drafts' | 'pending';

const VouchersPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const currentUser = initialState?.currentUser as
    | { id?: string; userid?: string; userName?: string; name?: string }
    | undefined;
  // live：ABP application-configuration → currentUser.id（app.tsx 映射 userid）；mock 无登录态回落种子 id
  const meId = resolveFinanceUserId(currentUser);
  const meName =
    currentUser?.name || currentUser?.userName || '当前用户';

  const voucherRef = useRef<ActionType>(undefined);
  const draftRef = useRef<ActionType>(undefined);
  const pendingRef = useRef<ActionType>(undefined);
  const adjustFormRef = useRef<ProFormInstance>(undefined);
  const [pendingFieldErrors, setPendingFieldErrors] = useState<string[]>([]);

  /** InvalidInput：按 error.data.fields 高亮表单字段（toast 仍由 onActionError 出） */
  const ADJUST_FORM_FIELDS = ['amount', 'currency', 'debitAccountCode', 'debitOwnerType', 'debitOwnerId', 'creditAccountCode', 'reason'];
  const highlightAdjustFields = (fields: string[]) => {
    const names = fields
      .map((f) => ADJUST_FORM_FIELDS.find((n) => n.toLowerCase() === f.toLowerCase()))
      .filter(Boolean) as string[];
    if (!names.length) return;
    adjustFormRef.current?.setFields(names.map((name) => ({ name, errors: ['请检查此项'] })));
    adjustFormRef.current?.scrollToField?.(names[0]);
  };
  const [tab, setTab] = useState<TabKey>('vouchers');
  const [detail, setDetail] = useState<FinVoucherDto>();
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

  const auditor = useMemo(() => ({ id: meId, name: meName }), [meId, meName]);

  const reloadAdjusts = () => {
    draftRef.current?.reload();
    pendingRef.current?.reload();
    voucherRef.current?.reload();
  };

  /** 业务错误统一提示；InvalidStatus 时自动刷新（v1 §1.2） */
  const onActionError = (e: unknown) => {
    message.error(financeErrorText(e));
    if (financeErrorCode(e) === FINANCE_ERROR.InvalidStatus) {
      reloadAdjusts();
      setAdjustDetail(undefined);
    }
  };

  useEffect(() => {
    if (!pendingFieldErrors.length || !editingAdjust) return;
    const t = setTimeout(() => {
      highlightAdjustFields(pendingFieldErrors);
      setPendingFieldErrors([]);
    }, 50);
    return () => clearTimeout(t);
  }, [pendingFieldErrors, editingAdjust]);

  const statusTag = (s: AdjustStatus) => {
    const cfg = ADJUST_STATUS_LABEL[s];
    return <Tag color={cfg.color}>{cfg.text}</Tag>;
  };

  const adjustColumns = (mode: 'drafts' | 'pending') => [
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
              r.rejectReason ? <span style={{ color: '#d46b08' }}>{r.rejectReason}</span> : '-',
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
                      title="提交后进入待批，不可再改，确认？"
                      onConfirm={async () => {
                        try {
                          await submitAdjust(r.id);
                          message.success('已提交待批');
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
          // Rejected 只读；仅制单人（且有 Adjust.Create）可「重新编辑」→ reopen → Draft
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
        // 老板待批：只审批/驳回；本人发起的单不出审批按钮（UI 先拦自批，
        // 后端兜底 403 Finance:Adjust:SelfApproveNotAllowed，除非有 Finance.Adjust.ApproveOwn）
        if (mode === 'pending' && (canApprove || canReject) && r.status === 'Pending') {
          if (r.applicantId === meId && !canApproveOwn) {
            acts.push(
              <Tag key="self" color="orange">
                本人发起
              </Tag>,
            );
          } else {
            if (canApprove) acts.push(
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
            if (canReject) acts.push(
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
              title="冲销该调账，确认？"
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
        if (r.voucherId) {
          acts.push(
            <a
              key="voucher"
              onClick={async () => {
                setDetail(await getVoucher(r.voucherId!));
              }}
            >
              凭证
            </a>,
          );
        }
        return acts;
      },
    },
  ];

  return (
    <PageContainer
      title={<FinanceHelpTitle page="A3" title="A3 凭证与手工调账" />}
      extra={
        load.ok ? (
          <Access accessible={canCreate}>
            <Button type="primary" onClick={() => setCreateOpen(true)}>
              新建调账
            </Button>
          </Access>
        ) : null
      }
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <ProCard
        key={load.nonce}
        tabs={{
          activeKey:
            (tab === 'drafts' && !canCreate) || (tab === 'pending' && !(canApprove || canReject))
              ? 'vouchers'
              : tab,
          onChange: (k) => setTab(k as TabKey),
          items: [
            {
              key: 'vouchers',
              label: '凭证列表',
              children: (
                <AutoHeightProTable<FinVoucherDto>
                  rowKey="id"
                  actionRef={voucherRef}
                  locale={{ emptyText: <FinanceEmpty text="暂无凭证" /> }}
                  search={{ labelWidth: 'auto' }}
                  request={async (params) => {
                    const maxResultCount = params.pageSize ?? 20;
                    const skipCount =
                      ((params.current ?? 1) - 1) * maxResultCount;
                    const result = await load.guard(() =>
                      getVouchers({
                      skipCount,
                      maxResultCount,
                      filter: params.voucherNo || params.summary,
                      bizType: params.bizType,
                    }),
                    );
                    if (!result) return { data: [], total: 0, success: false };
                    return {
                      data: result.items,
                      total: result.totalCount,
                      success: true,
                    };
                  }}
                  columns={[
                    { title: '凭证号', dataIndex: 'voucherNo', width: 150 },
                    { title: '业务类型', dataIndex: 'bizType', width: 120 },
                    { title: '业务单号', dataIndex: 'bizRefId', width: 120 },
                    { title: 'Phase', dataIndex: 'phase', width: 100, search: false },
                    { title: '期间', dataIndex: 'periodLabel', width: 90, search: false },
                    {
                      title: '状态',
                      dataIndex: 'status',
                      width: 90,
                      search: false,
                      render: (_, r) => (
                        <Tag color={r.status === 'Posted' ? 'success' : 'error'}>
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
                      render: (_: unknown, r: any) => formatForDisplay(r.creationTime),
                      width: 170,
                    },
                    {
                      title: '操作',
                      valueType: 'option',
                      width: 160,
                      render: (_, r) => {
                        const acts = [
                          <a
                            key="d"
                            onClick={async () => setDetail(await getVoucher(r.id))}
                          >
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
              ),
            },
            {
              key: 'drafts',
              label: '调账草稿',
              children: (
                <AutoHeightProTable<FinManualAdjustDto>
                  rowKey="id"
                  actionRef={draftRef}
                  search={false}
                  locale={{ emptyText: <FinanceEmpty text="没有草稿或被驳回的调账" /> }}
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
                    if (!result) return { data: [], total: 0, success: false };
                    return {
                      data: result.items,
                      total: result.totalCount,
                      success: true,
                    };
                  }}
                  columns={adjustColumns('drafts') as any}
                />
              ),
            },
            {
              key: 'pending',
              label: '待审批',
              children: (
                <AutoHeightProTable<FinManualAdjustDto>
                  rowKey="id"
                  actionRef={pendingRef}
                  search={false}
                  locale={{ emptyText: <FinanceEmpty text="没有待审批的调账" /> }}
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
                    if (!result) return { data: [], total: 0, success: false };
                    return {
                      data: result.items,
                      total: result.totalCount,
                      success: true,
                    };
                  }}
                  columns={adjustColumns('pending') as any}
                />
              ),
            },
          ].filter((it) =>
            it.key === 'drafts' ? canCreate : it.key === 'pending' ? (canApprove || canReject) : true,
          ),
        }}
      />
      </FinanceStateView>

            <ModalForm
        title={editingAdjust ? `编辑调账 ${editingAdjust.adjustNo}` : '新建调账（草稿）'}
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
                debitAccountCode: editingAdjust.lines?.[0]?.accountCode || '1002',
                debitOwnerType: 'Channel',
                debitOwnerId: 'default',
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
            reloadAdjusts();
            return true;
          } catch (e) {
            onActionError(e);
            highlightAdjustFields(financeErrorFields(e));
            return false;
          }
        }}
      >
        <ProFormDigit name="amount" label="金额" min={0.01} rules={[{ required: true }]} />
        <ProFormText name="currency" label="币种" initialValue="CNY" rules={[{ required: true }]} />
        <ProFormText name="debitAccountCode" label="借方科目编码" rules={[{ required: true }]} placeholder="如 1002" />
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
        <ProFormText name="debitOwnerId" label="借方 OwnerId" rules={[{ required: true }]} />
        <ProFormText name="creditAccountCode" label="贷方科目编码（可空，默认 3101）" placeholder="留空=3101" />
          {/* reason：存草稿可空；submit 时后端校验，空则 400 + data.fields=reason，由 ADJUST_FORM_FIELDS 高亮 */}
          <ProFormText name="reason" label="原因" placeholder="草稿可不填，提交时必填" />
      </ModalForm>

      <ModalForm<{ rejectReason: string }>
        title={rejecting ? `驳回调账 ${rejecting.adjustNo}` : '驳回调账'}
        open={!!rejecting}
        onOpenChange={(v) => {
          if (!v) setRejecting(undefined);
        }}
        modalProps={{ destroyOnHidden: true, width: 480 }}
        onFinish={async (values) => {
          if (!rejecting) return false;
          try {
            await rejectAdjust(rejecting.id, auditor, values.rejectReason.trim());
            message.success('已驳回');
            setRejecting(undefined);
            reloadAdjusts();
            return true;
          } catch (e) {
            onActionError(e);
            if (financeErrorCode(e) === FINANCE_ERROR.InvalidStatus) setRejecting(undefined);
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
        title={adjustDetail ? `调账 ${adjustDetail.adjustNo}` : '调账详情'}
        open={!!adjustDetail}
        onClose={() => setAdjustDetail(undefined)}
        size={560}
      >
        {adjustDetail && (
          <>
            <Descriptions column={1} size="small">
              <Descriptions.Item label="状态">{statusTag(adjustDetail.status)}</Descriptions.Item>
              <Descriptions.Item label="摘要">{adjustDetail.summary}</Descriptions.Item>
              <Descriptions.Item label="金额">
                {adjustDetail.amount.toFixed(2)} {adjustDetail.currency}
              </Descriptions.Item>
              <Descriptions.Item label="申请人">{adjustDetail.applicantName}</Descriptions.Item>
              <Descriptions.Item label="审批人">{adjustDetail.auditorName || '-'}</Descriptions.Item>
              {/* rejectReason 一直保留：重新编辑、再提交后仍展示 */}
              {adjustDetail.rejectReason ? (
                <Descriptions.Item label="驳回意见">
                  <span style={{ color: '#d46b08' }}>{adjustDetail.rejectReason}</span>
                </Descriptions.Item>
              ) : null}
            </Descriptions>
            <AutoHeightProTable
              rowKey={(l: { accountCode: string }, i?: number) => `${l.accountCode}-${i}`}
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
            {adjustDetail.status === 'Rejected' && adjustDetail.applicantId === meId && canCreate && (
              <Space style={{ marginTop: 16 }}>
                <Button
                  type="primary"
                  onClick={async () => {
                    try {
                      await reopenAdjust(adjustDetail.id, { id: meId, canCreate });
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
        title={detail ? `凭证 ${detail.voucherNo}` : '凭证详情'}
        open={!!detail}
        onClose={() => setDetail(undefined)}
        size={560}
      >
        {detail && (
          <>
            <Descriptions column={1} size="small">
              <Descriptions.Item label="业务">
                {detail.bizType} / {detail.phase}
              </Descriptions.Item>
              <Descriptions.Item label="业务单号">
                {detail.bizRefId}
              </Descriptions.Item>
              <Descriptions.Item label="期间">
                {detail.periodLabel}
              </Descriptions.Item>
              <Descriptions.Item label="状态">{detail.status}</Descriptions.Item>
              <Descriptions.Item label="摘要">
                {detail.summary || '-'}
              </Descriptions.Item>
              <Descriptions.Item label="借贷合计">
                {detail.debitTotal} / {detail.creditTotal}
              </Descriptions.Item>
            </Descriptions>
            <AutoHeightProTable
              rowKey="seq"
              search={false}
              pagination={false}
              toolBarRender={false}
              dataSource={detail.entries || []}
              columns={[
                { title: '#', dataIndex: 'seq', width: 50 },
                { title: '科目', dataIndex: 'accountCode', width: 90 },
                { title: '名称', dataIndex: 'accountName', width: 120 },
                { title: '借', dataIndex: 'debit', width: 90 },
                { title: '贷', dataIndex: 'credit', width: 90 },
              ]}
            />
          </>
        )}
      </Drawer>
    </PageContainer>
  );
};

export default VouchersPage;
