import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { financeErrorText } from '@/abp/finance';
import { queryClient } from '@/queryClient';
import { currencyLabel } from '@/abp/ordering';
import {
  approveWithdraw,
  completeWithdraw,
  getHotWallet,
  getWithdrawals,
  getWithdrawStatus,
  refundWithdraw,
  rejectWithdraw,
  retryWithdraw,
  WD_STATUS_LABELS,
  type WithdrawHotWalletStatusDto,
  type WithdrawOrderDto,
  type WithdrawStatusDto,
} from '@/abp/withdrawal';
import { withdrawalVouchersUrl } from '@/pages/finance/vouchers/navigation';
import { isValidRecordId, RecordFocus } from './record-focus';

const { Text } = Typography;

/** 列表默认显示待审提现单，单据详情跟踪打款与回执结果。 */
export default function WithdrawalsPage({
  embedded = false,
  initialStatus,
  withdrawalId,
}: {
  embedded?: boolean;
  initialStatus?: string;
  withdrawalId?: string;
} = {}) {
  const access = useAccess();
  const { message, modal } = App.useApp();
  const [data, setData] = useState<WithdrawOrderDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const focused = withdrawalId !== undefined;
  const invalidId = focused && !isValidRecordId(withdrawalId);
  const [loading, setLoading] = useState(focused && !invalidId);
  const [loadFailed, setLoadFailed] = useState(false);
  const [statusFilter, setStatusFilter] = useState(
    initialStatus === 'all'
      ? ''
      : initialStatus && Object.hasOwn(WD_STATUS_LABELS, initialStatus)
        ? initialStatus
        : 'Pending',
  );
  const [status, setStatus] = useState<WithdrawStatusDto>();
  const [statusError, setStatusError] = useState(false);
  const [hotWallet, setHotWallet] = useState<WithdrawHotWalletStatusDto>();
  const [walletLoading, setWalletLoading] = useState(true);
  const [walletError, setWalletError] = useState(false);
  const latestLoad = useRef(0);
  const pageSize = 20;
  const record = focused ? data[0] : undefined;
  const followingPayout =
    record?.status === 'Processing' || record?.status === 'Broadcasting';

  const reload = useCallback(
    async (background = false) => {
      if (invalidId) return;
      const version = ++latestLoad.current;
      if (!background) setLoading(true);
      setLoadFailed(false);
      try {
        const result = await getWithdrawals(
          focused
            ? { skipCount: 0, maxResultCount: 1, withdrawalId }
            : {
                skipCount: (page - 1) * pageSize,
                maxResultCount: pageSize,
                status: statusFilter,
              },
        );
        if (version !== latestLoad.current) return;
        const rows = focused
          ? result.items.filter(
              (item) => item.id.toLowerCase() === withdrawalId.toLowerCase(),
            )
          : result.items;

        if (background) {
          // 先读单据再查汇总，确保停止轮询时的汇总已纳入最新单据状态。
          const nextStatus = await getWithdrawStatus().catch(() => null);
          if (version !== latestLoad.current) return;
          if (nextStatus) setStatus(nextStatus);
          setStatusError(!nextStatus);
        }

        setData(rows);
        setTotal(focused ? rows.length : result.totalCount);
      } catch {
        if (version === latestLoad.current && focused && !background) {
          setData([]);
          setLoadFailed(true);
        }
      } finally {
        if (version === latestLoad.current) setLoading(false);
      }
    },
    [focused, invalidId, page, statusFilter, withdrawalId],
  );

  const reloadCards = useCallback(async () => {
    setStatusError(false);
    setWalletError(false);
    setWalletLoading(true);
    await Promise.all([
      getWithdrawStatus()
        .then(setStatus)
        .catch(() => setStatusError(true)),
      getHotWallet()
        .then(setHotWallet)
        .catch(() => setWalletError(true))
        .finally(() => setWalletLoading(false)),
    ]);
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);
  useEffect(() => {
    reloadCards();
  }, [reloadCards]);

  useEffect(() => {
    if (!followingPayout) return;
    let active = true;
    let timer: number;
    const refresh = async () => {
      if (document.visibilityState === 'visible') {
        await reload(true);
        void queryClient.invalidateQueries({
          predicate: ({ queryKey }) =>
            queryKey[0] === 'workbench' && queryKey[4] === 'withdrawals',
          refetchType: 'none',
        });
      }
      if (active) timer = window.setTimeout(refresh, 5_000);
    };
    timer = window.setTimeout(refresh, 5_000);
    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [followingPayout, reload]);

  const [rejectTarget, setRejectTarget] = useState<WithdrawOrderDto>();
  const [refundTarget, setRefundTarget] = useState<WithdrawOrderDto>();
  const [completeTarget, setCompleteTarget] = useState<WithdrawOrderDto>();
  const [remark, setRemark] = useState('');
  const [txHashInput, setTxHashInput] = useState('');
  const [acting, setActing] = useState(false);
  const actionInProgress = useRef(false);
  const [selectedIds, setSelectedIds] = useState<React.Key[]>([]);
  const [batchRejectOpen, setBatchRejectOpen] = useState(false);
  const [batchRunning, setBatchRunning] = useState(false);

  /** 串行提交，让服务端按每笔审批后的余额校验下一笔。 */
  const runBatch = async (
    action: (id: string) => Promise<void>,
    okText: string,
  ) => {
    setBatchRunning(true);
    let ok = 0;
    let fail = 0;
    let firstErr = '';
    try {
      for (const key of selectedIds) {
        try {
          await action(String(key));
          ok += 1;
        } catch (e) {
          fail += 1;
          if (!firstErr) {
            firstErr = financeErrorText(e);
          }
        }
      }
    } finally {
      setBatchRunning(false);
    }
    if (ok > 0) void queryClient.invalidateQueries({ queryKey: ['workbench'] });
    if (fail === 0) {
      message.success(`${okText}：${ok} 笔全部成功`);
    } else if (ok === 0) {
      message.error(`${okText}：${fail} 笔全部失败。${firstErr}`);
    } else {
      message.warning(
        `${okText}：成功 ${ok} 笔，失败 ${fail} 笔（保持待审）。首个错误：${firstErr}`,
      );
    }
    setSelectedIds([]);
    await reload();
    await reloadCards();
  };

  const guard = async (fn: () => Promise<void>, ok: string) => {
    if (actionInProgress.current) return;
    actionInProgress.current = true;
    setActing(true);
    try {
      await fn();
      void queryClient.invalidateQueries({ queryKey: ['workbench'] });
      message.success(ok);
      setRejectTarget(undefined);
      setRefundTarget(undefined);
      setCompleteTarget(undefined);
      setRemark('');
      setTxHashInput('');
      await reload();
      await reloadCards();
    } catch (e) {
      message.error(financeErrorText(e));
    } finally {
      actionInProgress.current = false;
      setActing(false);
    }
  };

  const fmtTime = (v?: string) => {
    if (!v) {
      return '-';
    }
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };
  const renderStatus = (value: string) => {
    const config = WD_STATUS_LABELS[value] ?? { color: 'default', text: value };
    return <Tag color={config.color}>{config.text}</Tag>;
  };
  const columns = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    {
      title: '用户',
      dataIndex: 'userId',
      width: 130,
      ellipsis: true,
      render: (v: string) => (
        <Tooltip title={v}>
          <Text style={{ fontFamily: 'monospace', fontSize: 11 }}>
            {`${v.slice(0, 8)}...`}
          </Text>
        </Tooltip>
      ),
    },
    {
      title: '币种',
      dataIndex: 'currency',
      width: 70,
      render: (v: string) => <Tag color="cyan">{v}</Tag>,
    },
    {
      title: '收款地址',
      dataIndex: 'toAddress',
      width: 150,
      ellipsis: true,
      render: (v: string) => (
        <Text
          copyable={{ text: v }}
          style={{ fontFamily: 'monospace', fontSize: 11 }}
        >{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text>
      ),
    },
    { title: '总额', dataIndex: 'amount', width: 110, align: 'right' as const },
    { title: '手续费', dataIndex: 'fee', width: 90, align: 'right' as const },
    {
      title: '净出账',
      dataIndex: 'actualAmount',
      width: 110,
      align: 'right' as const,
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 110,
      render: renderStatus,
    },
    {
      title: '链上哈希',
      dataIndex: 'txHash',
      width: 160,
      ellipsis: true,
      render: (v?: string) =>
        v ? (
          <Text
            copyable={{ text: v }}
            style={{ fontFamily: 'monospace', fontSize: 11 }}
          >{`${v.slice(0, 12)}...${v.slice(-6)}`}</Text>
        ) : (
          '-'
        ),
    },
    {
      title: '审核/失败原因',
      dataIndex: 'auditRemark',
      width: 180,
      ellipsis: true,
      render: (v: string | undefined, row: WithdrawOrderDto) =>
        row.failReason ?? v ?? '-',
    },
    {
      title: '操作',
      key: 'actions',
      width: 220,
      fixed: 'right' as const,
      render: (_: unknown, row: WithdrawOrderDto) => (
        <Space size={4}>
          {row.status === 'Pending' && (
            <>
              <Popconfirm
                title={`确认审批通过？净出账 ${row.actualAmount} ${row.currency}`}
                onConfirm={() =>
                  guard(() => approveWithdraw(row.id), '已审批通过')
                }
              >
                <Button size="small" type="primary" loading={acting}>
                  审批
                </Button>
              </Popconfirm>
              <Button
                size="small"
                danger
                onClick={() => {
                  setRejectTarget(row);
                  setRemark('');
                }}
              >
                拒绝
              </Button>
            </>
          )}
          {row.status === 'BroadcastFailed' && (
            <>
              <Button
                size="small"
                loading={acting}
                onClick={() => guard(() => retryWithdraw(row.id), '已重试')}
              >
                重试
              </Button>
              <Button
                size="small"
                disabled={acting}
                onClick={() => {
                  setCompleteTarget(row);
                  setTxHashInput(row.txHash ?? '');
                }}
              >
                人工完结
              </Button>
              <Button
                size="small"
                danger
                disabled={acting}
                onClick={() => {
                  setRefundTarget(row);
                  setRemark('');
                }}
              >
                退回余额
              </Button>
            </>
          )}
          {(row.status === 'Completed' ||
            row.status === 'Rejected' ||
            row.status === 'Cancelled') && <Text type="secondary">—</Text>}
        </Space>
      ),
    },
  ];

  const content = (
    <>
      <Space orientation="vertical" style={{ width: '100%' }} size={16}>
        <Card
          title="提现状态"
          size="small"
          extra={
            access.canAdminWithdrawals && (
              <Link to="/web3/config?view=withdraw">提现规则与启停</Link>
            )
          }
        >
          {statusError ? (
            <Alert
              type="error"
              title="加载提现状态失败"
              action={<Button onClick={reloadCards}>重试</Button>}
            />
          ) : status ? (
            <Space size={32} wrap>
              <Tag
                color={!status.halt && status.enabled ? 'success' : 'warning'}
              >
                {status.halt
                  ? '紧急停机中'
                  : status.enabled
                    ? '提现已开启'
                    : '提现已关闭'}
              </Tag>
              <Text>待审 {status.pendingCount}</Text>
              <Text>待广播 {status.processingCount}</Text>
              <Text>广播中 {status.broadcastingCount}</Text>
              <Text type="danger">广播失败 {status.broadcastFailedCount}</Text>
            </Space>
          ) : (
            <Spin size="small" />
          )}
          <div style={{ marginTop: 8 }}>
            {walletLoading ? (
              <Spin size="small" />
            ) : walletError ? (
              <Alert
                type="error"
                title="加载提现钱包余额失败"
                action={<Button onClick={reloadCards}>重试</Button>}
              />
            ) : hotWallet?.configured ? (
              <Space size={24} wrap>
                <Text>
                  热钱包{' '}
                  <Text
                    copyable
                    style={{ fontFamily: 'monospace', fontSize: 11 }}
                  >
                    {hotWallet.address}
                  </Text>
                </Text>
                {Object.entries(hotWallet.tokenBalances).map(([sym, bal]) => (
                  <Text key={sym}>
                    {sym} {bal}
                  </Text>
                ))}
                <Text>BNB {hotWallet.bnb}</Text>
                {hotWallet.breakerOpen ? (
                  <Tag color="error">
                    熔断中（余额低于地板，审批已全部阻断）
                  </Tag>
                ) : (
                  <Tag color="success">偿付正常</Tag>
                )}
              </Space>
            ) : (
              <Tag color="warning">热钱包未配置（审批与广播不可用）</Tag>
            )}
          </div>
        </Card>

        <Card
          title="提现单"
          extra={
            !focused && (
              <Space>
                <Select
                  style={{ width: 140 }}
                  value={statusFilter}
                  onChange={(v) => {
                    setStatusFilter(v);
                    setPage(1);
                  }}
                  options={[
                    { value: '', label: '全部状态' },
                    ...Object.entries(WD_STATUS_LABELS).map(([value, cfg]) => ({
                      value,
                      label: cfg.text,
                    })),
                  ]}
                />
                <Button
                  size="small"
                  type="primary"
                  disabled={!selectedIds.length}
                  loading={batchRunning}
                  onClick={() =>
                    modal.confirm({
                      title: `批量通过 ${selectedIds.length} 笔提现？`,
                      content:
                        '逐单执行（偿付护栏逐单检查），中途失败的单保持待审不影响后续。',
                      okText: '批量通过',
                      cancelText: '取消',
                      onOk: () => runBatch(approveWithdraw, '批量通过'),
                    })
                  }
                >
                  批量通过（{selectedIds.length}）
                </Button>
                <Button
                  size="small"
                  danger
                  disabled={!selectedIds.length}
                  onClick={() => {
                    setRemark('');
                    setBatchRejectOpen(true);
                  }}
                >
                  批量拒绝（{selectedIds.length}）
                </Button>
              </Space>
            )
          }
        >
          {focused && (
            <RecordFocus
              kind="提现"
              listPath="/web3/operations?view=withdrawals&status=all"
              invalid={invalidId}
              failed={loadFailed}
              loading={loading}
              reload={reload}
              summary={
                record && (
                  <Space wrap>
                    <Text>{record.networkName}</Text>
                    <Text strong>
                      {record.amount} {currencyLabel(record.currency)}
                    </Text>
                    {renderStatus(record.status)}
                    <Text copyable>提现单号 {record.id}</Text>
                    {record.broadcastSenderAddress && (
                      <Text copyable={{ text: record.broadcastSenderAddress }}>
                        打款钱包 {record.broadcastSenderAddress}
                      </Text>
                    )}
                    {access.canViewFinanceVouchers && (
                      <Link to={withdrawalVouchersUrl(record.id)}>
                        查看会计凭证
                      </Link>
                    )}
                  </Space>
                )
              }
            />
          )}
          {(!focused || record) && (
            <Table<WithdrawOrderDto>
              rowKey="id"
              loading={loading}
              dataSource={data}
              columns={columns}
              size="small"
              scroll={{ x: 1500 }}
              rowSelection={
                focused
                  ? undefined
                  : {
                      selectedRowKeys: selectedIds,
                      preserveSelectedRowKeys: true,
                      onChange: (keys) => setSelectedIds(keys),
                      getCheckboxProps: (row) => ({
                        disabled: row.status !== 'Pending',
                      }),
                    }
              }
              pagination={
                !focused && {
                  current: page,
                  pageSize,
                  total,
                  showSizeChanger: false,
                  onChange: (p) => setPage(p),
                }
              }
            />
          )}
        </Card>
      </Space>

      <Modal
        title={`拒绝提现 ${rejectTarget?.id.slice(0, 8)}...`}
        open={!!rejectTarget}
        confirmLoading={acting}
        okText="拒绝"
        okButtonProps={{ danger: true }}
        cancelText="取消"
        onCancel={() => setRejectTarget(undefined)}
        onOk={() => {
          if (!rejectTarget) return; // 模态打开必然已选行；收窄供闭包内直接用
          remark.trim()
            ? guard(
                () => rejectWithdraw(rejectTarget.id, remark.trim()),
                '已拒绝并解冻',
              )
            : message.warning('拒绝原因必填');
        }}
      >
        <Input.TextArea
          value={remark}
          onChange={(e) => setRemark(e.target.value)}
          maxLength={500}
          showCount
          placeholder="拒绝原因（必填，用户可见）"
        />
      </Modal>

      <Modal
        title={`批量拒绝 ${selectedIds.length} 笔提现`}
        open={batchRejectOpen}
        confirmLoading={batchRunning}
        okText="批量拒绝"
        okButtonProps={{ danger: true }}
        cancelText="取消"
        onCancel={() => setBatchRejectOpen(false)}
        onOk={async () => {
          if (!remark.trim()) {
            message.warning('拒绝原因必填');
            return;
          }
          await runBatch((id) => rejectWithdraw(id, remark.trim()), '批量拒绝');
          setBatchRejectOpen(false);
        }}
      >
        <Input.TextArea
          value={remark}
          onChange={(e) => setRemark(e.target.value)}
          maxLength={500}
          showCount
          placeholder="拒绝原因（必填，应用于所选全部提现单）"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          同一原因将应用于所选 {selectedIds.length}{' '}
          笔；逐单执行并解冻，失败的单保持待审。
        </Text>
      </Modal>

      <Modal
        title={`退回提现余额 ${refundTarget?.id.slice(0, 8)}...`}
        open={!!refundTarget}
        confirmLoading={acting}
        okText="确认退回"
        okButtonProps={{ danger: true }}
        cancelText="取消"
        onCancel={() => setRefundTarget(undefined)}
        onOk={() => {
          if (!refundTarget) return; // 同上：收窄代除断言
          remark.trim()
            ? guard(
                () => refundWithdraw(refundTarget.id, remark.trim()),
                '已退回用户余额',
              )
            : message.warning('退款原因必填');
        }}
      >
        <Input.TextArea
          value={remark}
          onChange={(e) => setRemark(e.target.value)}
          maxLength={500}
          showCount
          placeholder="退款原因（必填；前提：已核实链上未付出）"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          核验原打款钱包的交易结果后，净额和手续费将退回用户可用余额。
        </Text>
      </Modal>

      <Modal
        title={`人工完结 ${completeTarget?.id.slice(0, 8)}...`}
        open={!!completeTarget}
        confirmLoading={acting}
        okText="完结"
        cancelText="取消"
        onCancel={() => setCompleteTarget(undefined)}
        onOk={() => {
          if (!completeTarget) return; // 同上：收窄代除断言
          /^0x[0-9a-fA-F]{64}$/.test(txHashInput.trim())
            ? guard(
                () =>
                  completeWithdraw(
                    completeTarget.id,
                    txHashInput.trim().toLowerCase(),
                  ),
                '已完结',
              )
            : message.warning('请输入合法的交易哈希（0x + 64 位十六进制）');
        }}
      >
        <Input
          value={txHashInput}
          onChange={(e) => setTxHashInput(e.target.value)}
          placeholder="链上交易哈希（0x + 64 位十六进制）"
          style={{ fontFamily: 'monospace' }}
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          补录时会核对原打款钱包、交易 nonce、收款地址和实际到账金额。
        </Text>
      </Modal>
    </>
  );
  return embedded ? (
    content
  ) : (
    <PageContainer title="提现审核与打款">{content}</PageContainer>
  );
}
