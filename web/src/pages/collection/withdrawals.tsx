import { PageContainer } from '@ant-design/pro-components';
import {
  App,
  Button,
  Card,
  Descriptions,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { financeErrorText } from '@/abp/finance';
import {
  approveWithdraw,
  backfillOpening,
  completeWithdraw,
  getHotWallet,
  getWithdrawals,
  getWithdrawStatus,
  refundWithdraw,
  rejectWithdraw,
  retryWithdraw,
  setWithdrawEnabled,
  setWithdrawVault,
  WD_STATUS_LABELS,
  type WithdrawHotWalletStatusDto,
  type WithdrawOrderDto,
  type WithdrawStatusDto,
} from '@/abp/withdrawal';

const { Text } = Typography;

/**
 * 提现管理页（/collection/withdrawals，提现 M3/M4）：
 * 状态卡（开关/总闸/限额 + 热钱包余额/熔断）+ 列表（默认待审）；
 * 审批/拒绝/重试/人工完结/退款拒绝 + 批量通过/批量拒绝（串行逐单、偿付护栏逐单检查）；
 * 写动作错误就地提示（skipErrorHandler 语义）。
 */
export default function WithdrawalsPage() {
  const { message, modal } = App.useApp();
  const [data, setData] = useState<WithdrawOrderDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string>('Pending');
  const [status, setStatus] = useState<WithdrawStatusDto>();
  const [hotWallet, setHotWallet] = useState<WithdrawHotWalletStatusDto>();
  const [walletLoading, setWalletLoading] = useState(false);
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getWithdrawals({
        skipCount: (page - 1) * pageSize,
        maxResultCount: pageSize,
        status: statusFilter,
      });
      setData(result.items);
      setTotal(result.totalCount);
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, [page, statusFilter]);

  const reloadCards = useCallback(async () => {
    try {
      setStatus(await getWithdrawStatus());
    } catch {
      /* interceptor */
    }
    setWalletLoading(true);
    try {
      setHotWallet(await getHotWallet());
    } catch {
      setHotWallet(undefined); // 热钱包未配置或 RPC 不可用：状态卡显示未配置即可
    } finally {
      setWalletLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);
  useEffect(() => {
    reloadCards();
  }, [reloadCards]);

  const [walletModal, setWalletModal] = useState(false);
  const [walletForm, setWalletForm] = useState({ address: '', privateKey: '' });
  const [rejectTarget, setRejectTarget] = useState<WithdrawOrderDto>();
  const [refundTarget, setRefundTarget] = useState<WithdrawOrderDto>();
  const [completeTarget, setCompleteTarget] = useState<WithdrawOrderDto>();
  const [remark, setRemark] = useState('');
  const [txHashInput, setTxHashInput] = useState('');
  const [acting, setActing] = useState(false);
  const [selectedIds, setSelectedIds] = useState<React.Key[]>([]);
  const [batchRejectOpen, setBatchRejectOpen] = useState(false);
  const [batchRunning, setBatchRunning] = useState(false);

  /** 批量动作：串行逐单（对齐线上做法），逐单成败计数汇总；失败的单保持原状态不影响后续。 */
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
    setActing(true);
    try {
      await fn();
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
  const shortId = (v: string) => `${v.slice(0, 8)}...`;

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
      render: (v: string) => {
        const cfg = WD_STATUS_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
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
                onClick={() => guard(() => retryWithdraw(row.id), '已重试')}
              >
                重试
              </Button>
              <Button
                size="small"
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
                onClick={() => {
                  setRefundTarget(row);
                  setRemark('');
                }}
              >
                退款拒绝
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

  return (
    <PageContainer>
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        <Card
          title="提现状态"
          size="small"
          extra={
            <Space size={8}>
              <Button
                size="small"
                onClick={() => {
                  setWalletModal(true);
                  setWalletForm({ address: '', privateKey: '' });
                }}
              >
                配置热钱包
              </Button>
              <Popconfirm
                title="存量余额开账回填？"
                description="把 ColBalance 与账本 2201 的差额一次性过账（幂等可重跑），上线提现前执行一次。"
                onConfirm={() =>
                  guard(async () => {
                    const r = await backfillOpening();
                    message.success(
                      `开账完成：用户 ${r.users}，过账 ${r.posted}，跳过 ${r.skipped}`,
                    );
                  }, '开账完成')
                }
              >
                <Button size="small" loading={acting}>
                  存量开账
                </Button>
              </Popconfirm>
            </Space>
          }
        >
          {status ? (
            <Space size={32} wrap>
              <Space size={6}>
                功能开关：
                <Popconfirm
                  title={`确认${status.enabled ? '关闭' : '开启'}提现功能？`}
                  description="关闭后拒绝新提交、广播 worker 睡眠；开启即恢复付币（下一轮热生效）。"
                  onConfirm={async () => {
                    try {
                      await setWithdrawEnabled(!status.enabled);
                      message.success(
                        `提现功能已${status.enabled ? '关闭' : '开启'}`,
                      );
                      await reloadCards();
                    } catch (e) {
                      message.error(financeErrorText(e));
                    }
                  }}
                  okText="确认"
                  cancelText="取消"
                >
                  <Switch checked={status.enabled} size="small" />
                </Popconfirm>
              </Space>
              <Space size={6}>
                总闸（紧急停机）：
                <Switch
                  checked={status.halt}
                  checkedChildren="已拉闸"
                  unCheckedChildren="正常"
                  disabled
                  size="small"
                />
              </Space>
              <Text>单笔最小 {status.minAmount}</Text>
              <Text>费率 {status.feeRate}</Text>
              <Text>
                单笔上限 {status.maxDailyAmount} · 每日 {status.maxDailyCount}{' '}
                笔
              </Text>
              <Text>小额自动审 ≤ {status.autoApproveMaxAmount}</Text>
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
                <Text>熔断地板 {hotWallet.usdtFloor}</Text>
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
          }
        >
          <Table<WithdrawOrderDto>
            rowKey="id"
            loading={loading}
            dataSource={data}
            columns={columns}
            size="small"
            scroll={{ x: 1500 }}
            rowSelection={{
              selectedRowKeys: selectedIds,
              preserveSelectedRowKeys: true,
              onChange: (keys) => setSelectedIds(keys),
              getCheckboxProps: (row) => ({
                disabled: row.status !== 'Pending',
              }),
            }}
            pagination={{
              current: page,
              pageSize,
              total,
              showSizeChanger: false,
              onChange: (p) => setPage(p),
            }}
          />
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
        onOk={() =>
          remark.trim()
            ? guard(
                () => rejectWithdraw(rejectTarget!.id, remark.trim()),
                '已拒绝并解冻',
              )
            : message.warning('拒绝原因必填')
        }
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
          await runBatch(
            (id) => rejectWithdraw(id, remark.trim()),
            '批量拒绝',
          );
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
        title={`退款拒绝 ${refundTarget?.id.slice(0, 8)}...`}
        open={!!refundTarget}
        confirmLoading={acting}
        okText="退款拒绝"
        okButtonProps={{ danger: true }}
        cancelText="取消"
        onCancel={() => setRefundTarget(undefined)}
        onOk={() =>
          remark.trim()
            ? guard(
                () => refundWithdraw(refundTarget!.id, remark.trim()),
                '已退款拒绝',
              )
            : message.warning('退款原因必填')
        }
      >
        <Input.TextArea
          value={remark}
          onChange={(e) => setRemark(e.target.value)}
          maxLength={500}
          showCount
          placeholder="退款原因（必填；前提：已核实链上未付出）"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          净额 + 手续费将退回用户可用余额；请先在浏览器核对链上确实未付出。
        </Text>
      </Modal>

      <Modal
        title={`人工完结 ${completeTarget?.id.slice(0, 8)}...`}
        open={!!completeTarget}
        confirmLoading={acting}
        okText="完结"
        cancelText="取消"
        onCancel={() => setCompleteTarget(undefined)}
        onOk={() =>
          /^0x[0-9a-fA-F]{64}$/.test(txHashInput.trim())
            ? guard(
                () =>
                  completeWithdraw(
                    completeTarget!.id,
                    txHashInput.trim().toLowerCase(),
                  ),
                '已完结',
              )
            : message.warning('请输入合法的交易哈希（0x + 64 位十六进制）')
        }
      >
        <Input
          value={txHashInput}
          onChange={(e) => setTxHashInput(e.target.value)}
          placeholder="链上交易哈希（0x + 64 位十六进制）"
          style={{ fontFamily: 'monospace' }}
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          服务端会强制核对该哈希的回执且状态必须成功。
        </Text>
      </Modal>
      <Modal
        title="配置提现热钱包"
        open={walletModal}
        confirmLoading={acting}
        okText="保存"
        cancelText="取消"
        onCancel={() => setWalletModal(false)}
        onOk={() => {
          if (!/^0x[0-9a-fA-F]{40}$/.test(walletForm.address.trim())) {
            message.warning('地址需为 0x + 40 位十六进制');
            return;
          }
          guard(async () => {
            await setWithdrawVault(
              walletForm.address.trim(),
              walletForm.privateKey.trim(),
            );
            message.success('热钱包已配置（密文入库）');
          }, '热钱包已配置');
        }}
      >
        <Descriptions column={1} size="small" style={{ marginBottom: 12 }}>
          <Descriptions.Item label="地址">
            热钱包地址（0x + 40 位十六进制）
          </Descriptions.Item>
          <Descriptions.Item label="私钥">
            服务端校验私钥与地址匹配后加密入库；明文不落日志不回显。必须与归集助记词不同源。
          </Descriptions.Item>
        </Descriptions>
        <Input
          placeholder="热钱包地址"
          value={walletForm.address}
          onChange={(e) =>
            setWalletForm((f) => ({ ...f, address: e.target.value }))
          }
          style={{ fontFamily: 'monospace', marginBottom: 8 }}
        />
        <Input.Password
          placeholder="热钱包私钥"
          value={walletForm.privateKey}
          onChange={(e) =>
            setWalletForm((f) => ({ ...f, privateKey: e.target.value }))
          }
          style={{ fontFamily: 'monospace' }}
        />
      </Modal>
    </PageContainer>
  );
}
