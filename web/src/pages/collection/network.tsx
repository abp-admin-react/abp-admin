import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Divider,
  Drawer,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import {
  type ReactNode,
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';
import {
  type AnkrSettingsDto,
  type ChainApiKeyDto,
  type ChainApiKeysDto,
  type CollectionNetworkSummaryDto,
  type ConfigureNetworkInput,
  configureAnkr,
  configureNetwork,
  configureNetworkToken,
  createNetworkApiKey,
  getAnkrSettings,
  getApiKeys,
  getNetworkStatus,
  getNetworks,
  type NetworkStatusDto,
  removeApiKey,
  setApiKeyEnabled,
  type TestNodeCredentialDto,
  testNodeCredential,
  type TokenStatusDto,
} from '@/abp/chainAdmin';
import { isNotFound } from '@/requestErrorConfig';
import AnkrBalanceCard, { AnkrBalanceAmounts } from './AnkrBalanceCard';

type SaveNetworkChange = (
  action: () => Promise<unknown>,
  success: string,
) => Promise<boolean>;

function useCredentialTest() {
  const latest = useRef(0);
  const pending = useRef(false);
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<TestNodeCredentialDto>();

  function clear() {
    // 输入有变更时，忽略旧密钥或分组仍在进行的查询结果。
    latest.current += 1;
    setResult(undefined);
  }

  async function test(input: Parameters<typeof testNodeCredential>[0]) {
    if (pending.current) return;
    pending.current = true;
    const request = ++latest.current;
    setTesting(true);
    setResult(undefined);
    try {
      const response = await testNodeCredential(input);
      if (latest.current === request) setResult(response);
    } catch {
      if (latest.current === request) {
        setResult({
          passed: false,
          message: '暂时无法验证，请稍后重试',
          credits: null,
          balanceUsd: null,
        });
      }
    } finally {
      pending.current = false;
      setTesting(false);
    }
  }

  return { clear, result, test, testing };
}

function CredentialTestResult({
  result,
  label,
  children,
}: {
  result?: TestNodeCredentialDto;
  label?: string;
  children?: ReactNode;
}) {
  return (
    <div role="status" style={result ? { marginBlockStart: 8 } : undefined}>
      {result && (
        <Typography.Text type={result.passed ? 'success' : 'danger'}>
          {label && `${label} · `}
          {result.passed ? '测试通过：' : '测试失败：'}
          {result.message}
        </Typography.Text>
      )}
      {children}
    </div>
  );
}

function NetworkApiKeyPool({
  networkId,
  provider,
  keys,
  busy,
  save,
  canTest,
}: {
  networkId: string;
  provider: 'RpcBsc' | 'EtherscanV2';
  keys: ChainApiKeyDto[];
  busy: boolean;
  save: SaveNetworkChange;
  canTest: boolean;
}) {
  const [form] = Form.useForm<{ key: string; label?: string }>();
  const probe = useCredentialTest();
  const savedProbe = useCredentialTest();
  const [testedKey, setTestedKey] = useState<string>();
  const isAnkr = provider === 'RpcBsc';
  const titleId = useId();

  return (
    <Card
      title={
        <span id={titleId}>
          {isAnkr ? 'Ankr Project API Key' : 'Etherscan V2 API Key'}
        </span>
      }
    >
      <Typography.Paragraph type="secondary">
        {isAnkr
          ? '用于 BSC 充值检测、归集和提现。多把密钥可自动轮换。'
          : canTest
            ? '用于按地址补查代币转账。BSC 补查需要 Etherscan 的付费套餐。'
            : '用于按地址补查代币转账。'}
      </Typography.Paragraph>
      <Space orientation="vertical" size={16} style={{ width: '100%' }}>
        <Form
          name={`keys-${provider}`}
          aria-labelledby={titleId}
          form={form}
          disabled={busy}
          layout="vertical"
          onValuesChange={(changed) => {
            if ('key' in changed) probe.clear();
          }}
          onFinish={async (values) => {
            if (
              await save(
                () => createNetworkApiKey({ ...values, provider, networkId }),
                '节点密钥已添加',
              )
            ) {
              form.resetFields();
              probe.clear();
            }
          }}
        >
          <Row gutter={16}>
            <Col xs={24} sm={15}>
              <Form.Item
                name="key"
                label={isAnkr ? '新项目密钥' : '新 Etherscan 密钥'}
                rules={[
                  { required: true, whitespace: true, message: '请输入密钥' },
                ]}
              >
                <Input.Password
                  autoComplete="new-password"
                  spellCheck={false}
                  placeholder="输入 API Key"
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={9}>
              <Form.Item name="label" label="备注（选填）">
                <Input maxLength={128} placeholder="例如：主用密钥" />
              </Form.Item>
            </Col>
          </Row>
          <Space wrap>
            {canTest && (
              <Button
                htmlType="button"
                loading={probe.testing}
                disabled={busy}
                onClick={async () => {
                  const values = await form
                    .validateFields(['key'])
                    .catch(() => undefined);
                  if (values)
                    await probe.test({
                      networkId,
                      provider,
                      key: values.key.trim(),
                    });
                }}
              >
                测试连接
              </Button>
            )}
            <Button type="primary" htmlType="submit" loading={busy}>
              添加密钥
            </Button>
          </Space>
          <CredentialTestResult result={probe.result} />
        </Form>
        <div>
          <Typography.Text strong>已保存的密钥</Typography.Text>
          {keys.length === 0 && (
            <Typography.Paragraph
              type="secondary"
              style={{ marginBlock: '8px 0' }}
            >
              还没有添加密钥。
            </Typography.Paragraph>
          )}
          {keys.length > 0 && (
            <Table<ChainApiKeyDto>
              rowKey="id"
              dataSource={keys}
              pagination={false}
              size="small"
              scroll={{ x: canTest ? 440 : 360 }}
              style={{ marginBlockStart: 8 }}
              columns={[
                { title: '备注', dataIndex: 'label', ellipsis: true },
                { title: '密钥', dataIndex: 'maskedKey' },
                {
                  title: '启用',
                  render: (_, row) => (
                    <Switch
                      aria-label={`启用密钥 ${row.maskedKey}`}
                      checked={row.isEnabled}
                      disabled={busy}
                      onChange={(value) => {
                        savedProbe.clear();
                        return save(
                          () => setApiKeyEnabled(row.id, value, networkId),
                          '密钥状态已更新',
                        );
                      }}
                    />
                  ),
                },
                {
                  title: '操作',
                  render: (_, row) => (
                    <Space wrap size="small">
                      {canTest && (
                        <Button
                          type="link"
                          size="small"
                          aria-label={`测试已保存密钥 ${row.maskedKey}`}
                          loading={savedProbe.testing && testedKey === row.id}
                          disabled={
                            busy || (savedProbe.testing && testedKey !== row.id)
                          }
                          onClick={() => {
                            setTestedKey(row.id);
                            return savedProbe.test({
                              networkId,
                              provider,
                              keyId: row.id,
                            });
                          }}
                        >
                          测试
                        </Button>
                      )}
                      <Popconfirm
                        title="删除这把节点密钥？"
                        onConfirm={() => {
                          savedProbe.clear();
                          return save(
                            () => removeApiKey(row.id, networkId),
                            '密钥已删除',
                          );
                        }}
                      >
                        <Button danger type="link" size="small" disabled={busy}>
                          删除
                        </Button>
                      </Popconfirm>
                    </Space>
                  ),
                },
              ]}
            />
          )}
          <CredentialTestResult
            result={savedProbe.result}
            label={keys.find((key) => key.id === testedKey)?.maskedKey}
          />
        </div>
      </Space>
      {isAnkr && (
        <>
          <Divider />
          <DepositScanIntervalForm networkId={networkId} />
        </>
      )}
    </Card>
  );
}

function DepositScanIntervalForm({ networkId }: { networkId: string }) {
  const { message } = App.useApp();
  const labelId = useId();
  const [interval, setInterval] = useState<number>();
  const [saving, setSaving] = useState(false);
  const [faulted, setFaulted] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    setFaulted(false);
    getAnkrSettings(networkId)
      .then((settings) => {
        if (active) setInterval(settings.scanIntervalSeconds);
      })
      .catch(() => {
        if (active) setFaulted(true);
      });
    return () => {
      active = false;
    };
  }, [networkId, revision]);

  if (faulted) {
    return (
      <Alert
        type="error"
        title="加载全局充值检测间隔失败"
        action={
          <Button onClick={() => setRevision((value) => value + 1)}>
            重试
          </Button>
        }
      />
    );
  }
  if (interval === undefined) return <Spin />;

  return (
    <Form<{ scanIntervalSeconds: number }>
      name="deposit-scan-interval"
      aria-labelledby={labelId}
      layout="vertical"
      initialValues={{ scanIntervalSeconds: interval }}
      disabled={saving}
      onFinish={async ({ scanIntervalSeconds }) => {
        setSaving(true);
        try {
          await configureAnkr({ networkId, scanIntervalSeconds });
          message.success('全局充值检测间隔已保存');
        } catch (error) {
          if (isNotFound(error)) message.error('无法保存全局充值检测间隔');
        } finally {
          setSaving(false);
        }
      }}
    >
      <Form.Item
        name="scanIntervalSeconds"
        label={<span id={labelId}>全局充值检测间隔（秒）</span>}
        extra="控制系统每轮自动充值检测之间的等待时间，由所有项目密钥共用。"
        rules={[{ required: true, type: 'integer', min: 5, max: 4294967 }]}
      >
        <InputNumber min={5} max={4294967} precision={0} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        保存检测间隔
      </Button>
    </Form>
  );
}

function AnkrSettingsForm({ networkId }: { networkId: string }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<{
    platformAuthKey?: string;
    group?: string;
  }>();
  const probe = useCredentialTest();
  const titleId = useId();
  const [settings, setSettings] = useState<AnkrSettingsDto>();
  const [busy, setBusy] = useState(true);
  const [faulted, setFaulted] = useState(false);
  const [revision, setRevision] = useState(0);
  const reload = () => setRevision((n) => n + 1);
  const testedBalance = probe.result;

  useEffect(() => {
    let active = true;
    setBusy(true);
    setFaulted(false);
    getAnkrSettings(networkId)
      .then((value) => {
        if (!active) return;
        setSettings(value);
        form.setFieldsValue({
          platformAuthKey: '',
          group: value.group ?? '',
        });
      })
      .catch(() => {
        if (active) setFaulted(true);
      })
      .finally(() => {
        if (active) setBusy(false);
      });
    return () => {
      active = false;
    };
  }, [form, networkId, revision]);

  async function save(
    input: Parameters<typeof configureAnkr>[0],
    success: string,
  ) {
    setBusy(true);
    try {
      await configureAnkr(input);
      form.setFieldValue('platformAuthKey', '');
      probe.clear();
      message.success(success);
      setSettings(undefined);
      reload();
    } catch (error) {
      if (isNotFound(error)) message.error('无法保存 Ankr 配置');
      setBusy(false);
    }
  }

  if (faulted) {
    return (
      <Alert
        type="error"
        title="加载 Ankr 配置失败"
        action={<Button onClick={reload}>重试</Button>}
      />
    );
  }

  const settingsCard = (
    <Card title={<span id={titleId}>Ankr Platform Auth Key</span>}>
      <Typography.Paragraph type="secondary">
        用于查看 Ankr 账户的 Credits 和美元余额。在 Ankr 的 Settings → Platform
        Swagger 创建密钥。
      </Typography.Paragraph>
      <Spin spinning={busy && !settings}>
        <Form
          name="ankr-settings"
          aria-labelledby={titleId}
          form={form}
          disabled={busy || !settings}
          layout="vertical"
          onValuesChange={(changed) => {
            if ('platformAuthKey' in changed || 'group' in changed)
              probe.clear();
          }}
          onFinish={({ platformAuthKey, group }) => {
            const key = platformAuthKey?.trim();
            const accountGroup = group?.trim() || null;
            return save(
              {
                networkId,
                ...(key && { platformAuthKey: key }),
                ...(settings?.group !== accountGroup && {
                  group: accountGroup,
                }),
              },
              'Ankr 配置已保存',
            );
          }}
        >
          <Form.Item
            name="platformAuthKey"
            label="账户查询密钥"
            rules={[{ max: 2048 }]}
            extra={
              settings?.platformAuthKeyMasked
                ? `已保存 ${settings.platformAuthKeyMasked}。留空保留当前密钥。`
                : '未配置'
            }
          >
            <Input.Password
              autoComplete="new-password"
              maxLength={2048}
              spellCheck={false}
              placeholder="输入 Platform Auth Key"
            />
          </Form.Item>
          <Row gutter={16}>
            <Col xs={24} sm={15}>
              <Form.Item
                name="group"
                label="Ankr 账户分组"
                extra="选填，留空使用个人账户。"
                rules={[
                  {
                    transform: (value?: string) => value?.trim(),
                    pattern: /^\S*$/,
                    message: '分组标识不能包含空格',
                  },
                  { max: 128 },
                ]}
              >
                <Input allowClear maxLength={128} />
              </Form.Item>
            </Col>
          </Row>
          <Space wrap>
            <Button
              htmlType="button"
              loading={probe.testing}
              disabled={busy || !settings}
              onClick={async () => {
                const values = await form
                  .validateFields(['platformAuthKey', 'group'])
                  .catch(() => undefined);
                if (!values) return;
                const key = values.platformAuthKey?.trim();
                if (!key && !settings?.platformAuthKeyMasked) {
                  form.setFields([
                    {
                      name: 'platformAuthKey',
                      errors: ['请输入要测试的平台密钥'],
                    },
                  ]);
                  return;
                }
                await probe.test({
                  networkId,
                  provider: 'AnkrPlatform',
                  ...(key && { key }),
                  group: values.group?.trim() || null,
                });
              }}
            >
              测试连接
            </Button>
            <Button
              type="primary"
              htmlType="submit"
              loading={busy && !!settings}
            >
              保存 Ankr 配置
            </Button>
            {settings?.platformAuthKeyMasked && (
              <Popconfirm
                title="清除已保存的 Ankr 账户查询密钥？"
                okText="确认清除"
                onConfirm={() =>
                  save(
                    { networkId, clearPlatformAuthKey: true },
                    'Ankr 账户查询密钥已清除',
                  )
                }
              >
                <Button danger type="link" disabled={busy}>
                  清除账户查询密钥
                </Button>
              </Popconfirm>
            )}
          </Space>
          <CredentialTestResult result={testedBalance}>
            {testedBalance?.passed &&
              testedBalance.credits != null &&
              testedBalance.balanceUsd != null && (
                <Card
                  size="small"
                  title="本次测试余额"
                  role="region"
                  aria-label="本次测试余额"
                  style={{ marginBlockStart: 8 }}
                >
                  <AnkrBalanceAmounts
                    credits={testedBalance.credits}
                    balanceUsd={testedBalance.balanceUsd}
                  />
                </Card>
              )}
          </CredentialTestResult>
        </Form>
      </Spin>
    </Card>
  );

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      {settings && (
        <AnkrBalanceCard networkId={networkId} savedGroup={settings.group} />
      )}
      {settingsCard}
    </Space>
  );
}

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
  const [keys, setKeys] = useState<ChainApiKeysDto>();
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
    Promise.all([getNetworkStatus(network.id), getApiKeys(network.id)])
      .then(([value, pool]) => {
        if (!active) return;
        setStatus(value);
        setKeys(pool);
        // 修改节点密钥或代币时，保留充值规则表单中尚未保存的输入。
        if (resetRules.current) {
          savedArrival.current = value.estimatedArrivalMinutes ?? null;
          form.setFieldsValue({
            networkId: network.id,
            rpcUrl: '',
            nodeLogResultCap: value.nodeLogResultCap ?? null,
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
      <Tabs
        items={[
          {
            key: 'rules',
            label: '节点与充值规则',
            children: (
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
                    '网络配置已保存',
                    true,
                  );
                }}
              >
                <Space
                  orientation="vertical"
                  size={16}
                  style={{ width: '100%' }}
                >
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
                            <InputNumber
                              min={1}
                              max={1440}
                              style={{ width: '100%' }}
                            />
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
                      label="自动归集金额上限"
                      extra="超过此金额需人工放行。0 表示全部需人工放行。"
                      rules={[{ required: true }]}
                    >
                      <Input inputMode="decimal" />
                    </Form.Item>
                  </Card>
                  <Card title="节点配置" size="small">
                    <Form.Item
                      name="rpcUrl"
                      label="RPC 地址"
                      extra={`已保存 ${status.rpcUrlMasked || '未配置'}。留空沿用已保存地址。`}
                    >
                      <Input.Password
                        autoComplete="new-password"
                        placeholder="https://"
                      />
                    </Form.Item>
                    <Form.Item
                      name="nodeLogResultCap"
                      label="单次日志响应上限"
                      extra="根据节点实测或服务商文档填写。"
                      rules={[{ type: 'number', min: 100 }]}
                    >
                      <InputNumber
                        min={100}
                        precision={0}
                        style={{ width: '100%' }}
                      />
                    </Form.Item>
                  </Card>
                  <Button type="primary" htmlType="submit" loading={busy}>
                    保存网络配置
                  </Button>
                </Space>
              </Form>
            ),
          },
          {
            key: 'keys',
            label: '节点密钥',
            children: isBsc ? (
              <>
                <Typography.Paragraph type="secondary">
                  选一个服务来配置。测试连接只发起查询；确认后可另行保存。
                </Typography.Paragraph>
                <Tabs
                  type="card"
                  size="small"
                  items={[
                    {
                      key: 'ankr-rpc',
                      label: 'Ankr RPC',
                      children: (
                        <NetworkApiKeyPool
                          networkId={network.id}
                          provider="RpcBsc"
                          keys={keys?.rpcKeys ?? []}
                          busy={busy}
                          save={save}
                          canTest
                        />
                      ),
                    },
                    {
                      key: 'ankr-platform',
                      label: 'Ankr 账户余额',
                      children: <AnkrSettingsForm networkId={network.id} />,
                    },
                    {
                      key: 'etherscan',
                      label: 'Etherscan 补查',
                      children: (
                        <NetworkApiKeyPool
                          networkId={network.id}
                          provider="EtherscanV2"
                          keys={keys?.etherscanKeys ?? []}
                          busy={busy}
                          save={save}
                          canTest
                        />
                      ),
                    },
                  ]}
                />
              </>
            ) : (
              <NetworkApiKeyPool
                networkId={network.id}
                provider="EtherscanV2"
                keys={keys?.etherscanKeys ?? []}
                busy={busy}
                save={save}
                canTest={false}
              />
            ),
          },
        ]}
      />
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

export default function NetworkConfigPage() {
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
            {
              title: '节点',
              dataIndex: 'rpcConfigured',
              render: (configured: boolean) => (
                <Tag color={configured ? 'success' : 'warning'}>
                  {configured ? '已配置' : '未配置'}
                </Tag>
              ),
            },
            {
              title: '充值费率',
              dataIndex: 'depositFeeRate',
              render: (rate: number) => `${(rate * 100).toFixed(2)}%`,
            },
            { title: '自动归集上限', dataIndex: 'autoCreditMaxAmount' },
            {
              title: '操作',
              render: (_, row) => (
                <Button type="link" onClick={() => setSelected(row)}>
                  配置
                </Button>
              ),
            },
          ]}
        />
      </Card>
      <Drawer
        title={selected ? `${selected.name} 网络配置` : '网络配置'}
        open={!!selected}
        onClose={() => setSelected(undefined)}
        size="min(1280px, 100%)"
        focusable={{ trap: true, focusTriggerAfterClose: true }}
        styles={{ body: { overscrollBehavior: 'contain' } }}
      >
        {selected && (
          <NetworkEditor
            key={selected.id}
            network={selected}
            onSaved={load}
            runtimeNetwork={networks[0]}
          />
        )}
      </Drawer>
    </Space>
  );
}
