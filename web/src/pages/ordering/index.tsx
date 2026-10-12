import { PlusOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import type { ActionType } from '@ant-design/pro-components';
import { App, Button, Card, Form, Input, Modal, Select, Space, Tag } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  OrderDto,
  PAY_METHODS,
  cancelOrder,
  createOrder,
  getMyOrders,
  startPay,
} from '@/abp/ordering';
import { MyBalanceDto, getMyBalances } from '@/abp/collection';

/**
 * 订单页（/ordering，计划第 4.3 节）：列表、创建、发起支付、取消。
 * 金额是十进制字符串，不在前端解析成 number。管理端动作（标履约、人工退款记录）
 * 由 Host 管理员在此页按权限操作。
 * 列表走全站统一的 AutoHeightProTable：服务端分页（request 回调里
 * current/pageSize → SkipCount/MaxResultCount）、变更后 actionRef.reload()。
 */

const STATUS_TAGS: Record<string, { color: string; text: string }> = {
  PendingPayment: { color: 'processing', text: '待支付' },
  Paid: { color: 'success', text: '已支付' },
  Closed: { color: 'default', text: '已关闭' },
  Fulfilled: { color: 'purple', text: '已履约' },
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
    await createOrder(values);
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
          {b.currency === 'Usdt' ? 'USDT' : 'USDC'} 余额 {b.balance}
        </Tag>
      ))}
    </Space>
  );

  return (
    <PageContainer>
      <AutoHeightProTable<OrderDto>
        rowKey="id"
        actionRef={actionRef}
        headerTitle={headerTitle}
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
            width: 100,
            render: (_, record) => {
              const tag = STATUS_TAGS[record.status] ?? { color: 'default', text: record.status };
              return <Tag color={tag.color}>{tag.text}</Tag>;
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
            width: 120,
            render: (_, record) => record.payChannel ?? '-',
          },
          {
            title: '异常',
            dataIndex: 'paymentAnomaly',
            width: 70,
            render: (_, record) => (record.paymentAnomaly ? <Tag color="error">异常</Tag> : '-'),
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
          expandedRowRender: (order) =>
            order.anomalyLines?.length ? (
              <ul style={{ margin: 0, paddingLeft: 16 }}>
                {order.anomalyLines.map((line) => (
                  <li key={line.id}>
                    {line.creationTime} · {line.amount ?? '-'} {line.currency ?? ''} ·{' '}
                    {line.channelTradeNo ?? ''} · {line.note}
                  </li>
                ))}
              </ul>
            ) : (
              '无异常明细'
            ),
        }}
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
        <Space direction="vertical" style={{ width: '100%' }}>
          <Select
            style={{ width: '100%' }}
            value={payMethod}
            onChange={setPayMethod}
            options={PAY_METHODS.map((m) => ({ value: m.value, label: m.label }))}
          />
          {payPayload && (
            <Card size="small" title="支付凭据（扫码 / 跳转）">
              <div style={{ wordBreak: 'break-all' }}>{payPayload}</div>
            </Card>
          )}
        </Space>
      </Modal>
    </PageContainer>
  );
}
