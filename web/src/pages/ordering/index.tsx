import { PlusOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { history, useAccess, useLocation } from '@umijs/max';
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
  adminGetOrder,
  adminGetOrders,
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
import { isNotFound } from '@/requestErrorConfig';

/** 订单及余额流水。个人与管理视图按各自权限请求数据。 */

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

const fmtTime = (v?: string | null) => (v ? new Date(v).toLocaleString() : '-');

/** 链上交易哈希形状（写入侧来自链上 log，渲染侧仍守形状——非 0x64hex 只显示文本不出链接） */
const TX_HASH_RE = /^0x[0-9a-fA-F]{64}$/;

type PaymentCredential = { method: string; value: string };

/** 仅为标准 Stripe Checkout 地址提供跳转。其他渠道凭据以文字展示。 */
function stripeCheckoutUrl(payload: string): string | undefined {
  try {
    const url = new URL(payload);
    if (
      url.origin === 'https://checkout.stripe.com' &&
      !url.username &&
      !url.password
    ) {
      return url.href;
    }
  } catch {
    return undefined;
  }
  return undefined;
}

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
    <Typography.Text style={{ fontFamily: 'monospace', fontSize: 12 }}>
      {txHash}
    </Typography.Text>
  ) : (
    '-'
  );

/** 订单事实区（渠道单号 / 退款累计 / 异常与退款明细）——行展开与详情弹窗共用的唯一渲染 */
const OrderDetailFacts: React.FC<{ order: OrderDto }> = ({ order }) => (
  <>
    {order.channelTradeNo && (
      <Typography.Paragraph style={{ marginBottom: 4 }} type="secondary">
        渠道单号：
        <Typography.Text copyable={{ text: order.channelTradeNo }}>
          {order.channelTradeNo}
        </Typography.Text>
      </Typography.Paragraph>
    )}
    {order.refundedAmount != null && (
      <Typography.Paragraph style={{ marginBottom: 4 }}>
        退款累计：
        <Typography.Text type="warning">{order.refundedAmount}</Typography.Text>
      </Typography.Paragraph>
    )}
    {order.anomalyLines?.length ? (
      <ul style={{ margin: 0, paddingLeft: 16 }}>
        {order.anomalyLines.map((line) => (
          <li key={line.id}>
            {fmtTime(line.creationTime)} · {line.amount ?? '-'}{' '}
            {line.currency ?? ''} · {line.channelTradeNo ?? ''} · {line.note}
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
  const access = useAccess();
  const { pathname, search } = useLocation();
  const query = new URLSearchParams(search);
  const view = query.get('view');
  const personalAllowed = access.canViewOwnOrders;
  const showAll =
    access.canViewAllOrders && (view === 'all' || !personalAllowed || !view);
  const activeTab = showAll ? 'all' : view === 'txns' ? 'txns' : 'orders';
  const hasAnomaly = showAll && query.get('hasAnomaly') === 'true';
  const requestedId = query.get('orderId');
  const { message, modal } = App.useApp();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const [createOpen, setCreateOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const createInFlight = useRef(false);
  const [payTarget, setPayTarget] = useState<OrderDto | null>(null);
  const [payMethod, setPayMethod] = useState<string>('WechatNative');
  const [payCredential, setPayCredential] = useState<PaymentCredential | null>(
    null,
  );
  const [paying, setPaying] = useState(false);
  const payInFlight = useRef(false);
  const [balances, setBalances] = useState<MyBalanceDto[]>([]);
  const [form] = Form.useForm<{ subject: string; amount: string }>();

  const reload = () => actionRef.current?.reload();

  useEffect(() => {
    if (!personalAllowed || activeTab === 'all') return;
    // 余额读取失败时仍可继续查看订单。
    getMyBalances()
      .then(setBalances)
      .catch(() => {});
  }, [personalAllowed, activeTab]);

  const submitCreate = async () => {
    // 同一拍内的重复点击仍读到旧的 React state，先拿同步提交锁。
    if (createInFlight.current) return;
    createInFlight.current = true;
    setCreating(true);
    try {
      const values = await form.validateFields().catch(() => undefined);
      if (!values) return;
      await createOrder(values);
      message.success('订单已创建');
      setCreateOpen(false);
      form.resetFields();
      reload();
    } catch (error) {
      if (isNotFound(error)) message.error('创建订单接口暂不可用');
    } finally {
      createInFlight.current = false;
      setCreating(false);
    }
  };

  const closePayment = () => {
    if (payInFlight.current) return;
    setPayTarget(null);
    setPayCredential(null);
  };

  const copyPayment = async () => {
    if (!payCredential) return;
    try {
      await navigator.clipboard.writeText(payCredential.value);
      message.success('支付凭据已复制');
    } catch {
      message.error('复制失败，请手动选中并复制支付凭据');
    }
  };

  const submitPay = async () => {
    if (!payTarget || payCredential || payInFlight.current) return;
    payInFlight.current = true;
    setPaying(true);
    try {
      const result = await startPay(payTarget.id, payMethod);
      switch (result.outcome) {
        case 'ChannelCreated':
          if (result.channelPayload) {
            setPayCredential({
              method: payMethod,
              value: result.channelPayload,
            });
          } else {
            message.info('暂未取得支付凭据，请确认订单状态后重试');
          }
          break;
        case 'BalancePaid':
          message.success('余额支付成功');
          setPayTarget(null);
          break;
        case 'AlreadyCompleted':
          message.info('该订单已支付');
          setPayTarget(null);
          break;
        case 'Closed':
          message.warning('订单已关闭');
          setPayTarget(null);
          break;
      }
    } catch (error) {
      if (isNotFound(error)) message.error('订单不存在，请刷新后重试');
    } finally {
      payInFlight.current = false;
      setPaying(false);
      reload();
    }
  };

  const checkoutUrl =
    payCredential?.method === 'StripeCheckout'
      ? stripeCheckoutUrl(payCredential.value)
      : undefined;

  const doCancel = (order: OrderDto) => {
    modal.confirm({
      title: `取消订单 ${order.orderNo}？`,
      content: '确认取消后，此订单将关闭。若已完成付款，请先查看订单状态。',
      okText: '确认取消',
      cancelText: '返回',
      onOk: async () => {
        const result = await cancelOrder(order.id);
        if (result.status === 'Closed') {
          message.success('订单已关闭');
        } else if (result.status === 'Paid' || result.status === 'Fulfilled') {
          message.info('订单已支付，请查看订单详情');
        } else {
          message.warning('订单未取消，请刷新后查看状态');
        }
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
  const [, setTxnRange] = useState<[string | null, string | null]>([
    null,
    null,
  ]);
  // 表格立即重查，使用 ref 读取本次选择的起止时间。
  const txnRangeRef = useRef<[string | null, string | null]>([null, null]);
  const [orderDetail, setOrderDetail] = useState<OrderDto | null>(null);
  useEffect(() => {
    setOrderDetail(null);
    if (!requestedId) return;
    let active = true;
    const getDetails = showAll ? adminGetOrder : getOrder;
    getDetails(requestedId)
      .then((order) => {
        if (active) setOrderDetail(order);
      })
      .catch((error: unknown) => {
        if (active && isNotFound(error)) message.error('订单不存在');
      });
    return () => {
      active = false;
    };
  }, [requestedId, showAll, message]);

  function closeOrderDetail() {
    setOrderDetail(null);
    if (requestedId) {
      const next = new URLSearchParams(search);
      next.delete('orderId');
      history.replace(next.size ? `${pathname}?${next}` : pathname);
    }
  }
  /** 扣款/回冲行 → 来源订单（request 回调里批量反查后写入；回冲按原扣款流水关联） */
  const [txnRefs, setTxnRefs] = useState<
    Record<string, { orderId: string; orderNo: string }>
  >({});

  /** 回冲行按原扣款流水（RefTxnId）关联到原订单；其余行用自身 Id——规则唯一出处 */
  const orderLookupTxnId = (t: BalanceTxnDto) =>
    t.action === 'DebitReverse' ? (t.refTxnId ?? t.id) : t.id;

  const txnOrderRef = (t: BalanceTxnDto) =>
    txnRefs[orderLookupTxnId(t)] ?? null;

  const txnColumns: ProColumns<BalanceTxnDto>[] = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 170,
      render: (_, t) => fmtTime(t.creationTime),
    },
    {
      title: '类型',
      dataIndex: 'action',
      width: 110,
      render: (_, t) => {
        const cfg = BALANCE_ACTION_LABELS[t.action];
        return cfg ? <Tag color={cfg.color}>{cfg.text}</Tag> : t.action;
      },
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
        <Typography.Text style={{ fontFamily: 'monospace' }}>
          {t.balanceAfter}
        </Typography.Text>
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
    <PageContainer title="订单管理">
      {activeTab !== 'all' && !!balances.length && (
        <Card size="small" style={{ marginBottom: 16 }}>
          {headerTitle}
        </Card>
      )}
      <Tabs
        activeKey={activeTab}
        onChange={(key) => history.push(`${pathname}?view=${key}`)}
        destroyOnHidden
        items={[
          ...(access.canViewAllOrders
            ? [
                {
                  key: 'all',
                  label: '全部订单',
                  children: showAll && (
                    <AutoHeightProTable<OrderDto>
                      key={hasAnomaly ? 'anomalies' : 'all'}
                      rowKey="id"
                      search={false}
                      pagination={{ defaultPageSize: 20 }}
                      headerTitle={hasAnomaly ? '有支付异常的订单' : undefined}
                      toolBarRender={() => [
                        <Button
                          key="anomaly"
                          onClick={() =>
                            history.push(
                              `${pathname}?view=all${hasAnomaly ? '' : '&hasAnomaly=true'}`,
                            )
                          }
                        >
                          {hasAnomaly ? '查看全部' : '仅看支付异常'}
                        </Button>,
                      ]}
                      request={async ({ current = 1, pageSize = 20 }) => {
                        const result = await adminGetOrders({
                          skipCount: (current - 1) * pageSize,
                          maxResultCount: pageSize,
                          hasAnomaly: hasAnomaly || undefined,
                        });
                        return {
                          data: result.items,
                          total: result.totalCount,
                          success: true,
                        };
                      }}
                      columns={[
                        { title: '订单号', dataIndex: 'orderNo', width: 210 },
                        {
                          title: '用户 ID',
                          dataIndex: 'userId',
                          ellipsis: true,
                          width: 170,
                        },
                        { title: '标题', dataIndex: 'subject', ellipsis: true },
                        {
                          title: '标价（USD）',
                          dataIndex: 'amount',
                          width: 115,
                        },
                        {
                          title: '支付方式',
                          width: 150,
                          render: (_, order) => payChannelLabel(order) ?? '—',
                        },
                        {
                          title: '状态',
                          width: 160,
                          render: (_, order) => (
                            <Space size={4} wrap>
                              <Tag color={STATUS_TAGS[order.status]?.color}>
                                {STATUS_TAGS[order.status]?.text ??
                                  order.status}
                              </Tag>
                              {order.paymentAnomaly && (
                                <Tag color="warning">支付异常</Tag>
                              )}
                            </Space>
                          ),
                        },
                        {
                          title: '创建时间',
                          dataIndex: 'creationTime',
                          width: 160,
                          render: (_, order) => fmtTime(order.creationTime),
                        },
                        {
                          title: '操作',
                          width: 72,
                          render: (_, order) => (
                            <Button
                              size="small"
                              type="link"
                              onClick={() => {
                                const next = new URLSearchParams(search);
                                next.set('view', 'all');
                                next.set('orderId', order.id);
                                history.push(`${pathname}?${next}`);
                              }}
                            >
                              详情
                            </Button>
                          ),
                        },
                      ]}
                    />
                  ),
                },
              ]
            : []),
          ...(personalAllowed
            ? [
                {
                  key: 'orders',
                  label: '我的订单',
                  children: (
                    <AutoHeightProTable<OrderDto>
                      rowKey="id"
                      actionRef={actionRef}
                      search={false}
                      pagination={{ pageSize: 10 }}
                      request={async (params) => {
                        const result = await getMyOrders({
                          skipCount:
                            ((params.current || 1) - 1) *
                            (params.pageSize || 10),
                          maxResultCount: params.pageSize || 10,
                        });
                        return {
                          data: result.items,
                          total: result.totalCount,
                          success: true,
                        };
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
                            const tag = STATUS_TAGS[record.status] ?? {
                              color: 'default',
                              text: record.status,
                            };
                            return (
                              <Space size={4} wrap>
                                <Tag color={tag.color}>{tag.text}</Tag>
                                {record.refundedAmount != null && (
                                  <Tooltip
                                    title={`人工退款累计 ${record.refundedAmount}`}
                                  >
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
                            return (
                              <Tag color={meta?.color ?? 'default'}>
                                {label}
                              </Tag>
                            );
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
                                  if (payInFlight.current) return;
                                  setPayTarget(order);
                                  setPayCredential(null);
                                  setPayMethod(
                                    order.payChannel ?? 'WechatNative',
                                  );
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
                  label: '我的余额流水',
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
                          skipCount:
                            ((params.current || 1) - 1) *
                            (params.pageSize || 10),
                          maxResultCount: params.pageSize || 10,
                          fromTime: txnRangeRef.current[0] ?? undefined,
                          toTime: txnRangeRef.current[1] ?? undefined,
                        });
                        // 扣款/回冲行批量反查来源订单号（后端只按当前用户过滤，不泄露他人单号）
                        const ids = result.items.map(orderLookupTxnId);
                        if (ids.length > 0) {
                          const orderRefs = await getOrderRefsByBalanceTxn(
                            ids,
                          ).catch(() => []);
                          setTxnRefs(
                            Object.fromEntries(
                              orderRefs.map((r) => [
                                r.balanceTxnId,
                                { orderId: r.orderId, orderNo: r.orderNo },
                              ]),
                            ),
                          );
                        }
                        return {
                          data: result.items,
                          total: result.totalCount,
                          success: true,
                        };
                      }}
                      columns={txnColumns}
                    />
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        title="创建订单"
        open={createOpen}
        okText="创建订单"
        onOk={submitCreate}
        onCancel={() => {
          if (!createInFlight.current) setCreateOpen(false);
        }}
        confirmLoading={creating}
        closable={!creating}
        keyboard={!creating}
        mask={{ closable: !creating }}
        cancelButtonProps={{ disabled: creating }}
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          disabled={creating}
          initialValues={{ amount: '10.00' }}
        >
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
        title={`${payCredential ? '支付凭据' : '发起支付'} · ${payTarget?.orderNo ?? ''}`}
        open={payTarget != null}
        okText="发起支付"
        onOk={submitPay}
        onCancel={closePayment}
        confirmLoading={paying}
        closable={!paying}
        keyboard={!paying}
        mask={{ closable: !paying }}
        cancelButtonProps={{ disabled: paying }}
        footer={
          payCredential ? (
            <Space wrap>
              <Button onClick={() => setPayCredential(null)}>
                更换支付方式
              </Button>
              <Button onClick={copyPayment}>复制支付凭据</Button>
              {checkoutUrl && (
                <Button
                  type="primary"
                  href={checkoutUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  打开 Stripe 支付页
                </Button>
              )}
              <Button onClick={closePayment}>关闭</Button>
            </Space>
          ) : undefined
        }
        destroyOnHidden
      >
        <Space orientation="vertical" style={{ width: '100%' }}>
          {payCredential ? (
            <Card
              size="small"
              title={
                PAY_CHANNEL_META[payCredential.method]?.label ??
                payCredential.method
              }
            >
              <div style={{ wordBreak: 'break-all' }}>
                {payCredential.value}
              </div>
            </Card>
          ) : (
            <>
              <Select
                aria-label="支付方式"
                style={{ width: '100%' }}
                value={payMethod}
                disabled={paying}
                onChange={(method) => {
                  if (!payInFlight.current) setPayMethod(method);
                }}
                options={PAY_METHOD_GROUPS.map(({ groupLabel, methods }) => ({
                  label: groupLabel,
                  options: methods,
                }))}
              />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                第三方支付需要启用对应渠道。
              </Typography.Text>
            </>
          )}
        </Space>
      </Modal>

      <Modal
        title={`订单详情 · ${orderDetail?.orderNo ?? ''}`}
        open={orderDetail != null}
        footer={null}
        onCancel={closeOrderDetail}
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
