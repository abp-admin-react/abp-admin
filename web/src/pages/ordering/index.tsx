import { PlusOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import {
  App,
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Tabs,
  Tag,
  Timeline,
  Tooltip,
  Typography,
} from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  type OrderDto,
  PAY_CHANNEL_META,
  PAY_METHOD_GROUPS,
  currencyLabel,
  payChannelLabel,
  cancelOrder,
  createOrder,
  getOrder,
  getMyOrders,
  getOrderRefsByBalanceTxn,
  startPay,
} from '@/abp/ordering';
import {
  type BalanceTxnDto,
  type MyBalanceDto,
  BALANCE_ACTION_LABELS,
  getMyBalances,
  getMyTxns,
} from '@/abp/collection';

/**
 * 订单页（/ordering，计划第 4.3 节）：列表、创建、发起支付、取消 + 资金流水。
 * 金额是十进制字符串，不在前端解析成 number。管理端动作（标履约、人工退款记录）
 * 由 Host 管理员在此页按权限操作。
 *
 * 两个页签：
 * - 订单：AutoHeightProTable 服务端分页；行展开是订单时间线（创建→支付→履约→关闭）
 *   + 渠道单号 + 退款/异常明细——对齐正常商城的订单详情口径。
 * - 流水：余额资金流水（充值/支付扣款/回冲），充值行带链上凭证（BscScan 跳转），
 *   扣款/回冲行可查看来源订单；按时间范围过滤。
 *
 * 列表走全站统一的 AutoHeightProTable：服务端分页（request 回调里
 * current/pageSize → SkipCount/MaxResultCount）、变更后 actionRef.reload()。
 */

const STATUS_TAGS: Record<string, { color: string; text: string }> = {
  PendingPayment: { color: 'processing', text: '待支付' },
  Paid: { color: 'success', text: '已支付' },
  Closed: { color: 'default', text: '已关闭' },
  Fulfilled: { color: 'purple', text: '已履约' },
};

const CLOSE_REASON_LABELS: Record<string, string> = {
  UserCancel: '用户取消',
  Expired: '超时未付',
};

const fmtTime = (v?: string | null) =>
  v ? new Date(v).toLocaleString() : '-';

/** 链上交易哈希形状（写入侧来自链上 log，渲染侧仍守形状——非 0x64hex 只显示文本不出链接） */
const TX_HASH_RE = /^0x[0-9a-fA-F]{64}$/;

/** 链上凭证：短哈希 + BscScan 跳转（主网 chainid=56）；target=_blank 必带 noopener */
const TxHashLink: React.FC<{ txHash?: string | null }> = ({ txHash }) =>
  txHash && TX_HASH_RE.test(txHash) ? (
    <Typography.Link
      copyable={{ text: txHash }}
      href={`https://bscscan.com/tx/${txHash}`}
      target="_blank"
      rel="noopener noreferrer"
      style={{ fontFamily: 'monospace', fontSize: 12 }}
    >
      {`${txHash.slice(0, 10)}...${txHash.slice(-8)}`}
    </Typography.Link>
  ) : txHash ? (
    <Typography.Text style={{ fontFamily: 'monospace', fontSize: 12 }}>{txHash}</Typography.Text>
  ) : (
    '-'
  );

/** 订单事实区（渠道单号 / 退款累计 / 异常与退款明细）——行展开与详情弹窗共用的唯一渲染 */
const OrderDetailFacts: React.FC<{ order: OrderDto }> = ({ order }) => (
  <>
    {order.channelTradeNo && (
      <Typography.Paragraph style={{ marginBottom: 4 }} type="secondary">
        渠道单号：
        <Typography.Text copyable={{ text: order.channelTradeNo }}>{order.channelTradeNo}</Typography.Text>
      </Typography.Paragraph>
    )}
    {order.refundedAmount != null && (
      <Typography.Paragraph style={{ marginBottom: 4 }}>
        退款累计：<Typography.Text type="warning">{order.refundedAmount}</Typography.Text>
      </Typography.Paragraph>
    )}
    {order.anomalyLines?.length ? (
      <ul style={{ margin: 0, paddingLeft: 16 }}>
        {order.anomalyLines.map((line) => (
          <li key={line.id}>
            {fmtTime(line.creationTime)} · {line.amount ?? '-'} {line.currency ?? ''} ·{' '}
            {line.channelTradeNo ?? ''} · {line.note}
          </li>
        ))}
      </ul>
    ) : (
      <Typography.Text type="secondary">无异常/退款明细</Typography.Text>
    )}
  </>
);

/** 订单时间线（商城口径：创建 → 支付 → 履约 → 关闭） */
const OrderTimeline: React.FC<{ order: OrderDto }> = ({ order }) => {
  const items = [
    order.creationTime && {
      color: 'blue',
      children: `创建 · ${fmtTime(order.creationTime)}`,
    },
    order.paidAt && {
      color: 'green',
      children: `支付 · ${fmtTime(order.paidAt)}${
        order.channelTradeNo
          ? ` · 渠道单号 ${order.channelTradeNo}`
          : PAY_CHANNEL_META[order.payChannel ?? '']?.category === 'Balance'
            ? ' · 余额支付'
            : ''
      }`,
    },
    order.fulfilledAt && {
      color: 'purple',
      children: `履约 · ${fmtTime(order.fulfilledAt)}`,
    },
    order.status === 'Closed' && {
      color: 'gray',
      children: `关闭 · ${fmtTime(order.closedAt ?? order.expiresAt)} · ${
        CLOSE_REASON_LABELS[order.closeReason ?? ''] ?? order.closeReason ?? ''
      }${order.closedAt ? '' : '（截止时间，实际关闭时刻未记录）'}`,
    },
  ].filter(Boolean) as { color: string; children: React.ReactNode }[];
  return <Timeline items={items} style={{ margin: '8px 0 0' }} />;
};

export default function OrderingPage() {
  const { message, modal } = App.useApp();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const [createOpen, setCreateOpen] = useState(false);
  const [payTarget, setPayTarget] = useState<OrderDto | null>(null);
  const [payMethod, setPayMethod] = useState<string>('WechatNative');
  const [payPayload, setPayPayload] = useState<string | null>(null);
  const [balances, setBalances] = useState<MyBalanceDto[]>([]);
  const [form] = Form.useForm<{ subject: string; amount: string }>();

  const reload = () => actionRef.current?.reload();

  useEffect(() => {
    // 余额展示（计划第 4.3 节）：来自收款模块的用户接口，失败不阻塞订单列表
    getMyBalances().then(setBalances).catch(() => {});
  }, []);

  const submitCreate = async () => {
    const values = await form.validateFields();
    try {
      await createOrder(values);
    } catch {
      // 后端拒绝（如金额格式非法）时保持弹窗打开；全局拦截器已有错误提示，
      // 这里兜底提示，避免静默无反馈让用户以为没点上
      message.error('订单创建失败，请检查金额格式（两位小数）后重试');
      return;
    }
    message.success('订单已创建');
    setCreateOpen(false);
    form.resetFields();
    reload();
  };

  const submitPay = async () => {
    if (!payTarget) return;
    const result = await startPay(payTarget.id, payMethod);
    if (result.outcome === 'ChannelCreated' && result.channelPayload) {
      setPayPayload(result.channelPayload);
      message.success('收款已创建，请扫码或跳转支付');
    } else if (result.outcome === 'BalancePaid') {
      message.success('余额支付成功');
      setPayTarget(null);
    } else if (result.outcome === 'AlreadyCompleted') {
      message.info('该订单已按到账标记为已支付');
      setPayTarget(null);
    } else if (result.outcome === 'Closed') {
      message.warning('订单已关闭');
      setPayTarget(null);
    }
    reload();
  };

  const doCancel = (order: OrderDto) => {
    modal.confirm({
      title: `取消订单 ${order.orderNo}？`,
      content: '关闭后不可支付；用户取消后的到账只告警、不复活。',
      onOk: async () => {
        await cancelOrder(order.id);
        message.success('订单已取消');
        reload();
      },
    });
  };

  // 余额标签放 headerTitle：不占表格行高，AutoHeightProTable 的高度测量不受干扰
  const headerTitle = (
    <Space>
      {balances.map((b) => (
        <Tag key={b.currency} color={b.currency === 'Usdt' ? 'green' : 'blue'}>
          {currencyLabel(b.currency)} 余额 {b.balance}
        </Tag>
      ))}
    </Space>
  );

  // ── 流水页签 ────────────────────────────────────────────────────────────────────────
  const txnActionRef = useRef<ActionType | undefined>(undefined);
  const [, setTxnRange] = useState<[string | null, string | null]>([null, null]);
  // request 闭包读 ref 而不是 state：onChange 里 setState+reload 同拍发起的请求
  // 拿到的还是旧闭包（实测首次过滤失效），ref 立即可见
  const txnRangeRef = useRef<[string | null, string | null]>([null, null]);
  const [orderDetail, setOrderDetail] = useState<OrderDto | null>(null);
  /** 扣款/回冲行 → 来源订单（request 回调里批量反查后写入；回冲按原扣款流水关联） */
  const [txnRefs, setTxnRefs] = useState<Record<string, { orderId: string; orderNo: string }>>({});

  /** 回冲行按原扣款流水（RefTxnId）关联到原订单；其余行用自身 Id——规则唯一出处 */
  const orderLookupTxnId = (t: BalanceTxnDto) =>
    t.action === 'DebitReverse' ? t.refTxnId ?? t.id : t.id;

  const txnOrderRef = (t: BalanceTxnDto) => txnRefs[orderLookupTxnId(t)] ?? null;

  const txnColumns: ProColumns<BalanceTxnDto>[] = [
    { title: '时间', dataIndex: 'creationTime', width: 170, render: (_, t) => fmtTime(t.creationTime) },
    {
      title: '类型',
      dataIndex: 'action',
      width: 110,
      render: (_, t) => BALANCE_ACTION_LABELS[t.action] ?? t.action,
    },
    {
      title: '金额',
      dataIndex: 'amount',
      width: 150,
      render: (_, t) => (
        <Typography.Text
          style={{ fontFamily: 'monospace' }}
          type={t.amount.startsWith('-') ? 'danger' : 'success'}
        >
          {t.amount}
        </Typography.Text>
      ),
    },
    {
      title: '币种',
      dataIndex: 'currency',
      width: 80,
      render: (_, t) => currencyLabel(t.currency),
    },
    {
      title: '变动后余额',
      dataIndex: 'balanceAfter',
      width: 140,
      render: (_, t) => (
        <Typography.Text style={{ fontFamily: 'monospace' }}>{t.balanceAfter}</Typography.Text>
      ),
    },
    {
      title: '关联订单',
      key: 'orderRef',
      width: 200,
      render: (_, t) => {
        const ref = txnOrderRef(t);
        return ref ? (
          <Typography.Link
            onClick={async () =>
              setOrderDetail(await getOrder(ref.orderId).catch(() => null))
            }
          >
            {ref.orderNo}
          </Typography.Link>
        ) : (
          '-'
        );
      },
    },
    {
      title: '链上凭证',
      dataIndex: 'txHash',
      width: 170,
      render: (_, t) => <TxHashLink txHash={t.txHash} />,
    },
  ];

  return (
    <PageContainer>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Space>{headerTitle}</Space>
      </Card>
      <Tabs
        defaultActiveKey="orders"
        items={[
          {
            key: 'orders',
            label: '订单',
            children: (
              <AutoHeightProTable<OrderDto>
                rowKey="id"
                actionRef={actionRef}
                search={false}
                pagination={{ pageSize: 10 }}
                request={async (params) => {
                  // 服务端分页（全站惯用法，与 files/audit-logs 等页一致）；
                  // 后端钳制页大小上限 100
                  const result = await getMyOrders({
                    skipCount: ((params.current || 1) - 1) * (params.pageSize || 10),
                    maxResultCount: params.pageSize || 10,
                  });
                  return { data: result.items, total: result.totalCount, success: true };
                }}
                toolBarRender={() => [
                  <Button
                    key="create"
                    type="primary"
                    icon={<PlusOutlined />}
                    onClick={() => setCreateOpen(true)}
                  >
                    创建订单
                  </Button>,
                ]}
                columns={[
                  { title: '订单号', dataIndex: 'orderNo', width: 220 },
                  { title: '标题', dataIndex: 'subject', ellipsis: true },
                  {
                    title: '标价 (USD)',
                    dataIndex: 'amount',
                    width: 110,
                    render: (_, record) => record.amount ?? '-',
                  },
                  {
                    title: '状态',
                    dataIndex: 'status',
                    width: 160,
                    render: (_, record) => {
                      const tag = STATUS_TAGS[record.status] ?? { color: 'default', text: record.status };
                      return (
                        <Space size={4} wrap>
                          <Tag color={tag.color}>{tag.text}</Tag>
                          {record.refundedAmount != null && (
                            <Tooltip title={`人工退款累计 ${record.refundedAmount}`}>
                              <Tag color="orange">已退款</Tag>
                            </Tooltip>
                          )}
                        </Space>
                      );
                    },
                  },
                  {
                    title: '应付',
                    width: 140,
                    render: (_, order) =>
                      order.payAmount
                        ? `${order.payAmount} ${order.payCurrency ?? ''}`
                        : '-',
                  },
                  {
                    title: '支付方式',
                    dataIndex: 'payChannel',
                    width: 150,
                    render: (_, record) => {
                      const label = payChannelLabel(record);
                      if (!label) return '-';
                      const meta = record.payChannel
                        ? PAY_CHANNEL_META[record.payChannel]
                        : undefined;
                      return <Tag color={meta?.color ?? 'default'}>{label}</Tag>;
                    },
                  },
                  {
                    title: '操作',
                    width: 160,
                    render: (_, order) => (
                      <Space>
                        <Button
                          size="small"
                          type="primary"
                          disabled={order.status !== 'PendingPayment'}
                          onClick={() => {
                            setPayTarget(order);
                            setPayPayload(null);
                            setPayMethod(order.payChannel ?? 'WechatNative');
                          }}
                        >
                          支付
                        </Button>
                        <Button
                          size="small"
                          danger
                          disabled={order.status !== 'PendingPayment'}
                          onClick={() => doCancel(order)}
                        >
                          取消
                        </Button>
                      </Space>
                    ),
                  },
                ]}
                expandable={{
                  expandedRowRender: (order) => (
                    <div style={{ padding: '4px 0' }}>
                      <OrderTimeline order={order} />
                      <OrderDetailFacts order={order} />
                    </div>
                  ),
                }}
              />
            ),
          },
          {
            key: 'txns',
            label: '流水',
            children: (
              <AutoHeightProTable<BalanceTxnDto>
                rowKey="id"
                actionRef={txnActionRef}
                search={false}
                pagination={{ pageSize: 10 }}
                headerTitle={
                  <DatePicker.RangePicker
                    showTime
                    onChange={(range) => {
                      const next: [string | null, string | null] = [
                        range?.[0]?.toISOString() ?? null,
                        range?.[1]?.toISOString() ?? null,
                      ];
                      txnRangeRef.current = next;
                      setTxnRange(next);
                      txnActionRef.current?.reload();
                    }}
                  />
                }
                request={async (params) => {
                  const result = await getMyTxns({
                    skipCount: ((params.current || 1) - 1) * (params.pageSize || 10),
                    maxResultCount: params.pageSize || 10,
                    fromTime: txnRangeRef.current[0] ?? undefined,
                    toTime: txnRangeRef.current[1] ?? undefined,
                  });
                  // 扣款/回冲行批量反查来源订单号（后端只按当前用户过滤，不泄露他人单号）
                  const ids = result.items.map(orderLookupTxnId);
                  if (ids.length > 0) {
                    const orderRefs = await getOrderRefsByBalanceTxn(ids).catch(() => []);
                    setTxnRefs(
                      Object.fromEntries(
                        orderRefs.map((r) => [
                          r.balanceTxnId,
                          { orderId: r.orderId, orderNo: r.orderNo },
                        ]),
                      ),
                    );
                  }
                  return { data: result.items, total: result.totalCount, success: true };
                }}
                columns={txnColumns}
              />
            ),
          },
        ]}
      />

      <Modal
        title="创建订单"
        open={createOpen}
        onOk={submitCreate}
        onCancel={() => setCreateOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical" initialValues={{ amount: '10.00' }}>
          <Form.Item
            name="subject"
            label="标题"
            rules={[{ required: true, message: '请输入标题' }]}
          >
            <Input placeholder="业务单标题" />
          </Form.Item>
          <Form.Item
            name="amount"
            label="标价（USD，两位小数）"
            rules={[{ required: true, message: '请输入金额' }]}
          >
            <Input placeholder="10.00" />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={`发起支付 · ${payTarget?.orderNo ?? ''}`}
        open={payTarget != null}
        onOk={submitPay}
        onCancel={() => {
          setPayTarget(null);
          setPayPayload(null);
        }}
        destroyOnClose
      >
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Select
            style={{ width: '100%' }}
            value={payMethod}
            onChange={setPayMethod}
            options={PAY_METHOD_GROUPS.map((g) => ({
              label: g.groupLabel,
              options: g.methods.map((m) => ({
                value: m.value,
                label: m.note ? `${m.label}（${m.note} 实装前不可真实支付）` : m.label,
                disabled: false,
              })),
            }))}
          />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            法币渠道（微信/支付宝/Stripe）在 M3/M4 实装前为开发态假网关，仅联调路径可用。
          </Typography.Text>
          {payPayload && (
            <Card size="small" title="支付凭据（扫码 / 跳转）">
              <div style={{ wordBreak: 'break-all' }}>{payPayload}</div>
            </Card>
          )}
        </Space>
      </Modal>

      <Modal
        title={`订单详情 · ${orderDetail?.orderNo ?? ''}`}
        open={orderDetail != null}
        footer={null}
        onCancel={() => setOrderDetail(null)}
        width={560}
      >
        {orderDetail && (
          <Space orientation="vertical" style={{ width: '100%' }} size={8}>
            <Space wrap>
              <Tag color={STATUS_TAGS[orderDetail.status]?.color ?? 'default'}>
                {STATUS_TAGS[orderDetail.status]?.text ?? orderDetail.status}
              </Tag>
              {orderDetail.refundedAmount != null && (
                <Tag color="orange">退款累计 {orderDetail.refundedAmount}</Tag>
              )}
            </Space>
            <Typography.Paragraph style={{ marginBottom: 0 }}>
              {orderDetail.subject} · 标价 {orderDetail.amount ?? '-'} USD
            </Typography.Paragraph>
            <OrderTimeline order={orderDetail} />
            <OrderDetailFacts order={orderDetail} />
          </Space>
        )}
      </Modal>
    </PageContainer>
  );
}
