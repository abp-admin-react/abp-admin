import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Drawer,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Row,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  type CollectionNetworkSummaryDto,
  type ConfigureNetworkInput,
  configureNetwork,
  configureNetworkToken,
  getNetworkStatus,
  getNetworks,
  type NetworkStatusDto,
  type TokenStatusDto,
} from '@/abp/chainAdmin';
import TransactionRpc from './transaction-rpc';

function NetworkEditor({
  network,
  onSaved,
  runtimeNetwork,
}: {
  network: CollectionNetworkSummaryDto;
  onSaved: () => void;
  runtimeNetwork?: CollectionNetworkSummaryDto;
}) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ConfigureNetworkInput>();
  const [tokenForm] = Form.useForm();
  const [status, setStatus] = useState<NetworkStatusDto>();
  const [token, setToken] = useState<TokenStatusDto>();
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const resetRules = useRef(true);
  const savedArrival = useRef<number | null>(null);
  const isBsc = network.name === 'BSC' && network.chainId === 56;
  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    getNetworkStatus(network.id)
      .then((value) => {
        if (!active) return;
        setStatus(value);
        // 修改代币时保留充值规则表单中尚未保存的输入。
        if (resetRules.current) {
          savedArrival.current = value.estimatedArrivalMinutes ?? null;
          form.setFieldsValue({
            networkId: network.id,
            estimatedArrivalMinutes: savedArrival.current,
            depositFeeRate: String(value.depositFeeRate),
            autoCreditMaxAmount: value.autoCreditMaxAmount,
          });
          resetRules.current = false;
        }
      })
      .catch(() => {
        if (active) setFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [network.id, form, revision]);

  async function save(
    action: () => Promise<unknown>,
    success: string,
    refreshRules = false,
  ) {
    setBusy(true);
    try {
      await action();
      message.success(success);
      resetRules.current = refreshRules;
      setRevision((n) => n + 1);
      onSaved();
      return true;
    } catch {
      return false;
    } finally {
      setBusy(false);
    }
  }
  if (loading && !status) return <Spin />;
  if (!status)
    return (
      <Alert
        type="error"
        title="加载网络配置失败"
        action={<Button onClick={() => setRevision((n) => n + 1)}>重试</Button>}
      />
    );
  return (
    <Spin spinning={loading}>
      {runtimeNetwork && network.id !== runtimeNetwork.id && (
        <Alert
          type="info"
          showIcon
          title={`当前自动充值与归集运行于 ${runtimeNetwork.name}。${network.name} 的自动处理尚待接入。`}
          style={{ marginBottom: 16 }}
        />
      )}
      {failed && (
        <Alert
          type="error"
          title="刷新网络配置失败"
          action={
            <Button onClick={() => setRevision((n) => n + 1)}>重试</Button>
          }
        />
      )}
      <Form
        form={form}
        disabled={busy}
        layout="vertical"
        onFinish={({ estimatedArrivalMinutes, ...values }) => {
          const arrival = estimatedArrivalMinutes ?? null;
          return save(
            () =>
              configureNetwork({
                ...values,
                networkId: network.id,
                ...(isBsc &&
                  arrival !== savedArrival.current && {
                    estimatedArrivalMinutes: arrival,
                  }),
              }),
            '充值规则已保存',
            true,
          );
        }}
      >
        <Space orientation="vertical" size={16} style={{ width: '100%' }}>
          <Card title={`${network.name} 充值规则`} size="small">
            <Table<TokenStatusDto>
              rowKey="id"
              dataSource={status.tokens}
              pagination={false}
              size="small"
              columns={[
                { title: '代币', dataIndex: 'symbol' },
                { title: '最低充值', dataIndex: 'minDeposit' },
                {
                  title: '状态',
                  dataIndex: 'isEnabled',
                  render: (enabled: boolean) => (
                    <Tag color={enabled ? 'success' : 'default'}>
                      {enabled ? '启用' : '停用'}
                    </Tag>
                  ),
                },
                {
                  title: '操作',
                  render: (_, row) => (
                    <Button
                      type="link"
                      aria-label={`配置 ${row.symbol} 充值规则`}
                      onClick={() => {
                        tokenForm.setFieldsValue(row);
                        setToken(row);
                      }}
                    >
                      配置
                    </Button>
                  ),
                },
              ]}
              style={{ marginBottom: 16 }}
            />
            <Row gutter={16}>
              {isBsc && (
                <Col xs={24} sm={12}>
                  <Form.Item
                    name="estimatedArrivalMinutes"
                    label="预计到账（分钟）"
                    extra="仅用于展示，实际到账以区块确认为准。"
                    rules={[
                      {
                        required: true,
                        type: 'integer',
                        min: 1,
                        max: 1440,
                        message: '请输入 1–1440 之间的整数',
                      },
                    ]}
                  >
                    <InputNumber min={1} max={1440} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              )}
              <Col xs={24} sm={12}>
                <Form.Item
                  name="depositFeeRate"
                  label="充值费率"
                  extra="0.05 表示 5%，保存后用于新充值单。"
                  rules={[{ required: true }]}
                >
                  <Input inputMode="decimal" />
                </Form.Item>
              </Col>
            </Row>
            <Form.Item
              name="autoCreditMaxAmount"
              label="自动放行金额上限"
              extra="超过此金额需人工放行。0 表示全部需人工放行。"
              rules={[{ required: true }]}
            >
              <Input inputMode="decimal" />
            </Form.Item>
          </Card>
          <Button type="primary" htmlType="submit" loading={busy}>
            保存充值规则
          </Button>
        </Space>
      </Form>
      <Modal
        title={`${token?.symbol ?? ''} 代币规则`}
        open={!!token}
        onCancel={() => setToken(undefined)}
        confirmLoading={busy}
        onOk={() => tokenForm.submit()}
      >
        <Form
          form={tokenForm}
          disabled={busy}
          layout="vertical"
          onFinish={async (values) => {
            if (
              token &&
              (await save(
                () =>
                  configureNetworkToken({
                    networkId: network.id,
                    tokenId: token.id,
                    minDeposit: values.minDeposit,
                    isEnabled: values.isEnabled,
                  }),
                '代币规则已保存',
              ))
            )
              setToken(undefined);
          }}
        >
          <Form.Item
            name="minDeposit"
            label="最低充值"
            rules={[{ required: true }]}
          >
            <Input inputMode="decimal" />
          </Form.Item>
          <Form.Item name="isEnabled" label="启用代币" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Spin>
  );
}

export default function NetworkConfigPage({
  mode = 'network',
}: {
  mode?: 'network' | 'deposit';
} = {}) {
  const access = useAccess();
  const [networks, setNetworks] = useState<CollectionNetworkSummaryDto[]>([]);
  const [selected, setSelected] = useState<CollectionNetworkSummaryDto>();
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const load = useCallback(async () => {
    if (!access.canAdminNetwork) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      setNetworks(await getNetworks());
      setFailed(false);
    } catch {
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, [access.canAdminNetwork]);
  useEffect(() => {
    void load();
  }, [load]);
  if (!access.canAdminNetwork) {
    return <Empty description="暂无链网络管理权限" />;
  }

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      {failed && (
        <Alert
          type="error"
          title="加载网络列表失败"
          action={<Button onClick={load}>重试</Button>}
        />
      )}
      <Card
        extra={
          <Button onClick={load} loading={loading}>
            刷新
          </Button>
        }
      >
        <Table<CollectionNetworkSummaryDto>
          rowKey="id"
          dataSource={networks}
          loading={loading}
          scroll={{ x: 720 }}
          pagination={false}
          locale={{ emptyText: <Empty description="暂无链网络" /> }}
          columns={[
            { title: '网络', dataIndex: 'name' },
            { title: 'Chain ID', dataIndex: 'chainId' },
            {
              title: '自动处理',
              key: 'processing',
              render: (_, row) => (
                <Tag color={row.id === networks[0]?.id ? 'success' : 'default'}>
                  {row.id === networks[0]?.id ? '当前运行网络' : '待接入'}
                </Tag>
              ),
            },
            ...(mode === 'deposit'
              ? [
                  {
                    title: '充值费率',
                    dataIndex: 'depositFeeRate',
                    render: (rate: number) => `${(rate * 100).toFixed(2)}%`,
                  },
                  {
                    title: '自动放行金额上限',
                    dataIndex: 'autoCreditMaxAmount',
                  },
                ]
              : [
                  {
                    title: '交易 RPC',
                    dataIndex: 'rpcConfigured',
                    render: (configured: boolean) => (
                      <Tag color={configured ? 'success' : 'warning'}>
                        {configured ? '已配置' : '未配置'}
                      </Tag>
                    ),
                  },
                ]),
            {
              title: '操作',
              render: (_, row) => (
                <Button type="link" onClick={() => setSelected(row)}>
                  {mode === 'deposit' ? '配置充值' : '配置交易 RPC'}
                </Button>
              ),
            },
          ]}
        />
      </Card>
      <Drawer
        title={
          selected
            ? `${selected.name} ${mode === 'deposit' ? '充值配置' : '交易 RPC'}`
            : undefined
        }
        open={!!selected}
        onClose={() => setSelected(undefined)}
        size="min(1280px, 100%)"
        focusable={{ trap: true, focusTriggerAfterClose: true }}
        styles={{ body: { overscrollBehavior: 'contain' } }}
      >
        {selected &&
          (mode === 'deposit' ? (
            <NetworkEditor
              key={selected.id}
              network={selected}
              onSaved={load}
              runtimeNetwork={networks[0]}
            />
          ) : (
            <TransactionRpc
              key={selected.id}
              networkId={selected.id}
              isBsc={selected.name === 'BSC' && selected.chainId === 56}
              onSaved={load}
            />
          ))}
      </Drawer>
    </Space>
  );
}
