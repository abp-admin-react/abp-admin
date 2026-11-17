import {
  type ActionType,
  PageContainer,
  type ProColumns,
} from '@ant-design/pro-components';
import { history, Link, useAccess, useLocation, useModel } from '@umijs/max';
import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  Popconfirm,
  Select,
  Space,
  Spin,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  COLLECTION_CHANNELS,
  COLLECTION_CURRENCIES,
  COLLECTION_STATUSES,
} from '@/abp/collection';
import {
  type CollectionPaymentAdminDto,
  type CollectionRefundDto,
  getCollectionPayment,
  getCollectionPayments,
  getCollectionRefunds,
  queryCollectionPayment,
  queryCollectionRefund,
  type RequestCollectionRefundInput,
  requestCollectionRefund,
} from '@/abp/collectionAdmin';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  ConfigurationLink,
  ConfigurationPage,
  useConfigurationLocation,
} from '@/components/ContextConfiguration';
import { useModuleEnabled } from '@/hooks/useModuleAvailability';
import { isNotFound } from '@/requestErrorConfig';
import DepositsPage from '../collection/deposits';
import NetworkConfigPage from '../collection/network';
import { orderRecordUrl } from '../collection/record-links';
import { Web3RuleSection } from '../collection/rules';
import { PaymentChannelSettings } from '../settings/channel';
import { thirdPartyPaymentChannels } from '../settings/channelGroups';

type PaymentSearch = {
  filter?: string;
  status?: string;
  channel?: string;
};

const channelLabels = Object.fromEntries(
  COLLECTION_CHANNELS.map(({ value, label }) => [value, label]),
);

const currencyLabels = Object.fromEntries(
  COLLECTION_CURRENCIES.map(({ value, label }) => [value, label]),
);

const statusLabels = Object.fromEntries(
  COLLECTION_STATUSES.map(({ value, label }) => [value, label]),
);

const statusColors: Record<string, string> = {
  Pending: 'processing',
  Completed: 'success',
  Closed: 'default',
  ChannelFailed: 'error',
};

function PaymentStatus({ payment }: { payment: CollectionPaymentAdminDto }) {
  return (
    <Space size={4} wrap>
      <Tag color={statusColors[payment.status]}>
        {statusLabels[payment.status] ?? payment.status}
      </Tag>
      {payment.channelResultUnknown && <Tag color="warning">渠道结果未知</Tag>}
    </Space>
  );
}

function formatAmount(payment: CollectionPaymentAdminDto) {
  return `${payment.amount} ${currencyLabels[payment.currency] ?? payment.currency}`;
}

function formatTime(value: string | null) {
  if (!value) return '—';
  const time = new Date(value);
  return Number.isNaN(time.getTime()) ? value : time.toLocaleString();
}

function PaymentDetails({
  id,
  onQueried,
  onUnresolvedRefund,
  retryInput,
}: {
  id: string;
  onQueried: () => void;
  onUnresolvedRefund: (input: RequestCollectionRefundInput | undefined) => void;
  retryInput?: RequestCollectionRefundInput;
}) {
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const [payment, setPayment] = useState<CollectionPaymentAdminDto>();
  const [error, setError] = useState<string>();
  const [querying, setQuerying] = useState(false);

  useEffect(() => {
    let active = true;
    getCollectionPayment(id)
      .then((result) => {
        if (active) setPayment(result);
      })
      .catch((failure: unknown) => {
        if (active) {
          setError(isNotFound(failure) ? '收款记录不存在' : '加载收款记录失败');
        }
      });
    return () => {
      active = false;
    };
  }, [id]);

  if (error) return <Alert title={error} type="error" showIcon />;
  if (!payment) return <Spin />;

  async function refreshPayment() {
    setQuerying(true);
    try {
      setPayment(await queryCollectionPayment(id));
      onQueried();
    } catch (failure) {
      if (isNotFound(failure)) setError('收款记录不存在');
    } finally {
      setQuerying(false);
    }
  }

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      {access.canReconcileCollectionPayments && (
        <Button loading={querying} onClick={refreshPayment}>
          查询渠道支付状态
        </Button>
      )}
      <Descriptions
        column={1}
        size="small"
        items={[
          { key: 'no', label: '收款单号', children: payment.collectionNo },
          { key: 'id', label: '收款 ID', children: payment.id },
          { key: 'userId', label: '用户 ID', children: payment.userId },
          {
            key: 'businessType',
            label: '业务类型',
            children: payment.businessType,
          },
          {
            key: 'businessId',
            label: '业务单 ID',
            children:
              payment.businessType === 'Order' &&
              payment.businessId &&
              (access.canViewAllOrders ||
                (access.canViewOwnOrders &&
                  initialState?.currentUser?.userid === payment.userId)) ? (
                <Link to={orderRecordUrl(payment.businessId)}>
                  {payment.businessId}
                </Link>
              ) : (
                payment.businessId
              ),
          },
          {
            key: 'channel',
            label: '渠道',
            children: channelLabels[payment.channel] ?? payment.channel,
          },
          {
            key: 'trade',
            label: '渠道交易号',
            children: payment.channelTradeNo ?? '—',
          },
          { key: 'amount', label: '收款金额', children: formatAmount(payment) },
          {
            key: 'status',
            label: '状态',
            children: <PaymentStatus payment={payment} />,
          },
          {
            key: 'note',
            label: '状态说明',
            children: payment.statusNote ?? '—',
          },
          {
            key: 'created',
            label: '创建时间',
            children: formatTime(payment.creationTime),
          },
          {
            key: 'expires',
            label: '过期时间',
            children: formatTime(payment.expiresAt),
          },
          {
            key: 'completed',
            label: '完成时间',
            children: formatTime(payment.completedAt),
          },
        ]}
      />
      <PaymentRefunds
        payment={payment}
        canRefund={access.canRefundCollectionPayments}
        onUnresolvedRefund={onUnresolvedRefund}
        retryInput={retryInput}
      />
    </Space>
  );
}

// 当前外部收款渠道的原路退款币种，与 CollectionCurrency 合同一致。
const refundCurrencies: Record<string, number> = { Cny: 1, Usd: 2 };
const refundCurrencyCodes: Record<number, string> = {
  1: 'CNY',
  2: 'USD',
  3: 'USDT',
  4: 'USDC',
};

const rejectedRefundCodes = new Set([
  'AbpAdmin.Collection:InvalidAmount',
  'AbpAdmin.Collection:RefundNotAllowed',
  'AbpAdmin.Collection:RefundAmountExceeded',
  'AbpAdmin.Collection:ChannelNotConfigured',
]);

function refundRequestError(error: unknown) {
  const response = (
    error as {
      response?: {
        status?: number;
        data?: { error?: { code?: string; message?: string } };
      };
    }
  )?.response;
  const payload = response?.data?.error;
  const status = response?.status;
  return {
    rejected:
      status === 401 ||
      status === 403 ||
      status === 404 ||
      (payload?.code != null && rejectedRefundCodes.has(payload.code)),
    message: payload?.message,
  };
}

function refundStatus(status: CollectionRefundDto['status']) {
  if (status === 1 || status === 'Succeeded') return '退款成功';
  if (status === 2 || status === 'Failed') return '退款失败';
  return '退款处理中';
}

function isRefundPending(status: CollectionRefundDto['status']) {
  return status === 0 || status === 'Pending';
}

function PaymentRefunds({
  payment,
  canRefund,
  onUnresolvedRefund,
  retryInput,
}: {
  payment: CollectionPaymentAdminDto;
  canRefund: boolean;
  onUnresolvedRefund: (input: RequestCollectionRefundInput | undefined) => void;
  retryInput?: RequestCollectionRefundInput;
}) {
  const [form] = Form.useForm<{ amount: string }>();
  const [refunds, setRefunds] = useState<CollectionRefundDto[]>();
  const [loadError, setLoadError] = useState<string>();
  const [submitError, setSubmitError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [queriedRefund, setQueriedRefund] = useState<string>();
  const submitting = useRef(false);
  const currency = refundCurrencies[payment.currency];

  useEffect(() => {
    let active = true;
    getCollectionRefunds(payment.id)
      .then((result) => {
        if (active) setRefunds(result);
      })
      .catch((failure) => {
        if (active)
          setLoadError(
            isNotFound(failure) ? '收款记录不存在' : '加载退款记录失败',
          );
      });
    return () => {
      active = false;
    };
  }, [payment.id]);

  function recordRefund(refund: CollectionRefundDto) {
    setRefunds((current) => [
      refund,
      ...(current ?? []).filter((item) => item.id !== refund.id),
    ]);
  }

  async function submitRefund(input: RequestCollectionRefundInput) {
    if (submitting.current) return;
    submitting.current = true;
    onUnresolvedRefund(input);
    setSubmitError(undefined);
    setNotice(undefined);
    setBusy(true);
    try {
      const refund = await requestCollectionRefund(input);
      recordRefund(refund);
      onUnresolvedRefund(undefined);
      setNotice(refundStatus(refund.status));
      form.resetFields();
    } catch (failure) {
      const error = refundRequestError(failure);
      if (error.rejected) onUnresolvedRefund(undefined);
      setSubmitError(
        error.rejected
          ? (error.message ?? '退款请求被拒绝，请核对权限和收款状态')
          : '未收到退款请求结果，请重试原请求',
      );
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  async function refreshRefund(id: string) {
    setQueriedRefund(id);
    try {
      recordRefund(await queryCollectionRefund(id));
    } catch (failure) {
      if (isNotFound(failure)) setLoadError('退款记录不存在');
    } finally {
      setQueriedRefund(undefined);
    }
  }

  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        退款记录
      </Typography.Title>
      {loadError && <Alert type="error" title={loadError} showIcon />}
      {!refunds && !loadError && <Spin />}
      {refunds?.length === 0 && (
        <Typography.Text type="secondary">暂无退款记录</Typography.Text>
      )}
      {refunds?.map((refund) => (
        <Descriptions
          key={refund.id}
          column={1}
          size="small"
          items={[
            {
              key: 'refund-no',
              label: '退款单号',
              children: refund.merchantRefundNo,
            },
            {
              key: 'channel-no',
              label: '渠道退款号',
              children: refund.channelRefundNo ?? '—',
            },
            {
              key: 'amount',
              label: '金额',
              children: `${refund.amount} ${refundCurrencyCodes[refund.currency] ?? refund.currency}`,
            },
            {
              key: 'status',
              label: '状态',
              children: (
                <Space wrap>
                  <Tag>{refundStatus(refund.status)}</Tag>
                  {refund.channelResultUnknown && (
                    <Tag color="warning">渠道结果未知</Tag>
                  )}
                  {canRefund && isRefundPending(refund.status) && (
                    <Button
                      size="small"
                      loading={queriedRefund === refund.id}
                      disabled={queriedRefund != null}
                      onClick={() => refreshRefund(refund.id)}
                    >
                      查退款
                    </Button>
                  )}
                </Space>
              ),
            },
            {
              key: 'note',
              label: '状态说明',
              children: refund.statusNote ?? '—',
            },
            {
              key: 'created',
              label: '创建时间',
              children: formatTime(refund.creationTime),
            },
            {
              key: 'finished',
              label: '成功时间',
              children: formatTime(refund.succeededAt),
            },
          ]}
        />
      ))}
      {canRefund &&
        payment.status === 'Completed' &&
        currency != null &&
        refunds && (
          <Form
            form={form}
            layout="vertical"
            onFinish={({ amount }) =>
              submitRefund({
                collectionId: payment.id,
                requestId: crypto.randomUUID(),
                amount: amount.trim(),
                currency,
              })
            }
          >
            <Form.Item
              name="amount"
              label={`原路退款金额（${currencyLabels[payment.currency] ?? payment.currency}）`}
              rules={[
                { required: true, message: '请输入退款金额' },
                {
                  validator: async (_, value?: string) => {
                    const amount = value?.trim() ?? '';
                    if (
                      amount &&
                      (!/^\d{1,10}(\.\d{1,2})?$/.test(amount) ||
                        /^0+(\.0+)?$/.test(amount))
                    ) {
                      throw new Error('请输入大于零且最多两位小数的金额');
                    }
                  },
                },
              ]}
            >
              <Input
                inputMode="decimal"
                placeholder="0.00"
                disabled={retryInput != null}
              />
            </Form.Item>
            {retryInput && !submitError && (
              <Typography.Paragraph type="secondary">
                待重试金额 {retryInput.amount}{' '}
                {refundCurrencyCodes[retryInput.currency]}
              </Typography.Paragraph>
            )}
            {submitError && (
              <Alert
                type="warning"
                title={submitError}
                showIcon
                style={{ marginBottom: 12 }}
              />
            )}
            {notice && (
              <Typography.Paragraph type="secondary">
                {notice}
              </Typography.Paragraph>
            )}
            {retryInput ? (
              <Button
                danger
                loading={busy}
                onClick={() => submitRefund(retryInput)}
              >
                重试原退款请求
              </Button>
            ) : (
              <Popconfirm
                title="按填写金额发起原路退款？"
                onConfirm={() => form.submit()}
              >
                <Button danger loading={busy}>
                  发起退款
                </Button>
              </Popconfirm>
            )}
          </Form>
        )}
    </Space>
  );
}

export default function PaymentsPage() {
  const access = useAccess();
  const { setting, link } = useConfigurationLocation();
  const { pathname, search, hash } = useLocation();
  const query = new URLSearchParams(search);
  const requestedView = query.get('view');
  const depositId = query.get('depositId') || undefined;
  const depositStatus = query.get('status') || undefined;
  const depositSweepTaskId = query.get('sweepTaskId') || undefined;
  const canViewPayments = access.canViewCollectionPayments;
  const canViewDeposits = access.canViewDepositRecords;
  const canConfigurePayments = access.canManagePaymentSettings;
  const canConfigureDeposits = access.canConfigureCollectionNetwork;
  const activeView =
    canViewDeposits && (requestedView === 'deposits' || !canViewPayments)
      ? 'deposits'
      : canViewPayments
        ? 'payments'
        : undefined;
  const activeSetting =
    canConfigureDeposits && setting === 'deposit'
      ? 'deposit'
      : canConfigurePayments && setting === 'channels'
        ? 'channels'
        : !activeView && canConfigurePayments
          ? 'channels'
          : !activeView && canConfigureDeposits
            ? 'deposit'
            : undefined;
  const selectedChannel =
    thirdPartyPaymentChannels.find(
      ({ channel }) => channel === query.get('channel'),
    )?.channel ??
    thirdPartyPaymentChannels.find(({ key }) => key === requestedView)?.channel;
  const [unresolvedRefunds, setUnresolvedRefunds] = useState<
    Record<string, RequestCollectionRefundInput>
  >({});
  const acceptsDeposits = useModuleEnabled('Chain');

  function updateUnresolvedRefund(
    id: string,
    input?: RequestCollectionRefundInput,
  ) {
    setUnresolvedRefunds((current) => {
      if (input) return { ...current, [id]: input };
      const remaining = { ...current };
      delete remaining[id];
      return remaining;
    });
  }

  if (activeSetting) {
    const depositSettings = activeSetting === 'deposit';
    return (
      <ConfigurationPage
        parent="收款管理"
        title={depositSettings ? '账户充值配置' : '第三方渠道配置'}
        returnTo={activeView ? link() : undefined}
      >
        {canConfigurePayments && canConfigureDeposits && (
          <nav aria-label="收款配置" style={{ marginBottom: 12 }}>
            <Space wrap size="middle">
              {depositSettings ? (
                <ConfigurationLink to={link('channels')}>
                  第三方渠道配置
                </ConfigurationLink>
              ) : (
                <ConfigurationLink to={link('deposit')}>
                  账户充值配置
                </ConfigurationLink>
              )}
            </Space>
          </nav>
        )}
        {depositSettings ? (
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            {access.canAdminNetwork && <NetworkConfigPage mode="deposit" />}
            {access.canManageSettings && (
              <Web3RuleSection section="Chain" scope="deposit" />
            )}
          </Space>
        ) : (
          <PaymentChannelSettings />
        )}
      </ConfigurationPage>
    );
  }

  const configuration =
    activeView === 'deposits'
      ? canConfigureDeposits
        ? 'deposit'
        : canConfigurePayments
          ? 'channels'
          : undefined
      : canConfigurePayments
        ? 'channels'
        : canConfigureDeposits
          ? 'deposit'
          : undefined;
  return (
    <PageContainer
      title="收款管理"
      extra={
        configuration && (
          <ConfigurationLink to={link(configuration)}>
            收款配置
          </ConfigurationLink>
        )
      }
    >
      {activeView ? (
        <Tabs
          activeKey={activeView}
          destroyOnHidden
          onChange={(view) => {
            const next = new URLSearchParams(search);
            next.set('view', view);
            for (const name of [
              'channel',
              'paymentId',
              'depositId',
              'sweepTaskId',
              'status',
              'settings',
              'settingsChannel',
            ]) {
              next.delete(name);
            }
            history.push({ pathname, search: `?${next}`, hash });
          }}
          items={[
            ...(canViewPayments
              ? [
                  {
                    key: 'payments',
                    label: '第三方收款',
                    children:
                      activeView === 'payments' ? (
                        <PaymentRecords
                          selectedChannel={selectedChannel}
                          unresolvedRefunds={unresolvedRefunds}
                          onUnresolvedRefund={updateUnresolvedRefund}
                        />
                      ) : null,
                  },
                ]
              : []),
            ...(canViewDeposits
              ? [
                  {
                    key: 'deposits',
                    label: '账户充值',
                    children:
                      activeView === 'deposits' ? (
                        <>
                          {!acceptsDeposits && (
                            <Alert
                              type="warning"
                              showIcon
                              title="链上充值已停止受理"
                              description="可以继续查看已有记录和处理充值尾单。"
                              style={{ marginBottom: 12 }}
                            />
                          )}
                          <DepositsPage
                            key={`${depositId ?? ''}:${depositStatus ?? ''}:${depositSweepTaskId ?? ''}`}
                            embedded
                            depositId={depositId}
                            initialStatus={depositStatus}
                            sweepTaskId={depositSweepTaskId}
                            rulesHref={link('deposit')}
                          />
                        </>
                      ) : null,
                  },
                ]
              : []),
          ]}
        />
      ) : (
        <Empty description="暂无可访问的收款视图" />
      )}
    </PageContainer>
  );
}

function PaymentRecords({
  selectedChannel,
  unresolvedRefunds,
  onUnresolvedRefund,
}: {
  selectedChannel?: string;
  unresolvedRefunds: Record<string, RequestCollectionRefundInput>;
  onUnresolvedRefund: (
    id: string,
    input?: RequestCollectionRefundInput,
  ) => void;
}) {
  const acceptsPayments = useModuleEnabled('Collection');
  const { pathname, search, hash } = useLocation();
  const query = new URLSearchParams(search);
  const detailId = query.get('paymentId') || undefined;
  function setDetailId(id?: string) {
    const next = new URLSearchParams(search);
    if (id) next.set('paymentId', id);
    else next.delete('paymentId');
    const target = { pathname, search: next.size ? `?${next}` : '', hash };
    if (id) history.push(target);
    else history.replace(target);
  }
  const actionRef = useRef<ActionType | undefined>(undefined);

  const columns: ProColumns<CollectionPaymentAdminDto>[] = [
    {
      title: '搜索',
      dataIndex: 'filter',
      hideInTable: true,
      fieldProps: { placeholder: '收款单号、渠道交易号或业务单 ID' },
    },
    {
      title: '收款单号',
      dataIndex: 'collectionNo',
      search: false,
      ellipsis: true,
      width: 190,
    },
    {
      title: '渠道',
      dataIndex: 'channel',
      search: false,
      valueType: 'select',
      valueEnum: channelLabels,
      width: 135,
    },
    {
      title: '状态',
      dataIndex: 'status',
      valueType: 'select',
      valueEnum: statusLabels,
      render: (_, payment) => <PaymentStatus payment={payment} />,
      width: 155,
    },
    {
      title: '金额',
      dataIndex: 'amount',
      search: false,
      align: 'right',
      width: 145,
      render: (_, payment) => formatAmount(payment),
    },
    {
      title: '渠道交易号',
      dataIndex: 'channelTradeNo',
      search: false,
      ellipsis: true,
      width: 180,
    },
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      search: false,
      valueType: 'dateTime',
      width: 170,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 80,
      render: (_, payment) => (
        <Button
          type="link"
          size="small"
          onClick={() => setDetailId(payment.id)}
        >
          详情
        </Button>
      ),
    },
  ];

  return (
    <>
      {!acceptsPayments && (
        <Alert
          type="warning"
          showIcon
          title="收款已停止受理"
          description="可以继续查询已有收款及处理符合条件的退款。"
          style={{ marginBottom: 12 }}
        />
      )}
      <AutoHeightProTable<CollectionPaymentAdminDto, PaymentSearch>
        actionRef={actionRef}
        rowKey="id"
        columns={columns}
        params={{ channel: selectedChannel }}
        toolBarRender={() => [
          <Space key="channel">
            <label htmlFor="third-party-channel">渠道</label>
            <Select
              id="third-party-channel"
              allowClear
              placeholder="全部外部渠道"
              value={selectedChannel}
              style={{ minWidth: 145 }}
              options={thirdPartyPaymentChannels.map(({ channel, label }) => ({
                value: channel,
                label,
              }))}
              onChange={(channel) => {
                const next = new URLSearchParams(search);
                next.set('view', 'payments');
                if (channel) next.set('channel', channel);
                else next.delete('channel');
                history.push({ pathname, search: `?${next}`, hash });
              }}
            />
          </Space>,
        ]}
        request={async ({ current = 1, pageSize = 20, filter, status }) => {
          const { items, totalCount } = await getCollectionPayments({
            skipCount: (current - 1) * pageSize,
            maxResultCount: pageSize,
            filter,
            status,
            channel: selectedChannel,
          });
          return { data: items, total: totalCount, success: true };
        }}
        pagination={{ defaultPageSize: 20 }}
      />
      <Drawer
        title="收款记录详情"
        open={!!detailId}
        onClose={() => setDetailId(undefined)}
        size={520}
      >
        {detailId && (
          <PaymentDetails
            key={detailId}
            id={detailId}
            onQueried={() => actionRef.current?.reload()}
            retryInput={unresolvedRefunds[detailId]}
            onUnresolvedRefund={(input) => onUnresolvedRefund(detailId, input)}
          />
        )}
      </Drawer>
    </>
  );
}
