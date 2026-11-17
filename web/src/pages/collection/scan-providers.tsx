import {
  Alert,
  Button,
  Card,
  Checkbox,
  Drawer,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
} from 'react';
import {
  type AnkrBalanceDto,
  type CollectionNetworkSummaryDto,
  getNetworks,
} from '@/abp/chainAdmin';
import {
  type ConfigureScanProviderInput,
  claimLegacyScanProviderBalance,
  createScanProvider,
  deleteScanProvider,
  getScanProviderBalance,
  getScanProviders,
  reorderScanProviders,
  type ScanProvider,
  type ScanProviderDto,
  type ScanProviderListDto,
  type ScanProviderTestDto,
  testScanProvider,
  testScanProviderBalance,
  updateScanProvider,
} from '@/abp/scanProviders';
import { AnkrBalanceAmounts } from './AnkrBalanceCard';
import { useCredentialProbe } from './useCredentialProbe';

const providerNames: Record<ScanProvider, string> = {
  ankr: 'Ankr',
  etherscan: 'Etherscan',
};
const providerOptions = Object.entries(providerNames).map(([value, label]) => ({
  value,
  label,
}));
const scanUnavailable: ScanProviderTestDto = {
  passed: false,
  message: '暂时无法测试扫链，请稍后重试。',
};
const balanceUnavailable: AnkrBalanceDto = {
  status: 'unavailable',
  credits: null,
  balanceUsd: null,
  checkedAt: null,
};

type ProviderForm = {
  provider: ScanProvider;
  label: string;
  apiKey?: string;
  platformAuthKey?: string;
  clearPlatformAuthKey: boolean;
  group?: string;
  isEnabled: boolean;
  networkIds: string[];
  logResultCaps: Record<string, number | null>;
};

function entryName(entry: ScanProviderDto) {
  return entry.label || `${providerNames[entry.provider]} ${entry.keyMasked}`;
}

function ProviderEditor({
  entry,
  networks,
  saving,
  onSave,
}: {
  entry?: ScanProviderDto;
  networks: CollectionNetworkSummaryDto[];
  saving: boolean;
  onSave: (values: ConfigureScanProviderInput) => Promise<void>;
}) {
  const [form] = Form.useForm<ProviderForm>();
  const provider = Form.useWatch('provider', form);
  const selectedNetworkIds = Form.useWatch('networkIds', form) ?? [];
  const clearPlatformKey = Form.useWatch('clearPlatformAuthKey', form);
  const [preferredTestNetwork, setPreferredTestNetwork] = useState<string>();
  const scan = useCredentialProbe<ScanProviderTestDto>();
  const balance = useCredentialProbe<AnkrBalanceDto>();
  const testNetworks = networks.filter(({ id }) =>
    selectedNetworkIds.includes(id),
  );
  const testNetwork =
    preferredTestNetwork && selectedNetworkIds.includes(preferredTestNetwork)
      ? preferredTestNetwork
      : (testNetworks.find(({ chainId }) => chainId === 56)?.id ??
        selectedNetworkIds[0]);
  const initialValues: Partial<ProviderForm> = {
    provider: entry?.provider,
    label: entry?.label ?? '',
    group: entry?.group ?? '',
    isEnabled: entry?.isEnabled ?? true,
    clearPlatformAuthKey: false,
    networkIds:
      entry?.networks.map(({ networkId }) => networkId) ??
      networks.filter(({ chainId }) => chainId === 56).map(({ id }) => id),
    logResultCaps: Object.fromEntries(
      entry?.networks.map(({ networkId, logResultCap }) => [
        networkId,
        logResultCap,
      ]) ?? [],
    ),
  };

  return (
    <Form<ProviderForm>
      name="scan-provider"
      aria-label="扫链服务商配置"
      method="post"
      form={form}
      layout="vertical"
      initialValues={initialValues}
      disabled={saving}
      validateTrigger="onBlur"
      scrollToFirstError={{ focus: true }}
      onValuesChange={(changed) => {
        if (
          'provider' in changed ||
          'apiKey' in changed ||
          'networkIds' in changed
        )
          scan.clear();
        if (
          'platformAuthKey' in changed ||
          'group' in changed ||
          'clearPlatformAuthKey' in changed ||
          'provider' in changed
        ) {
          balance.clear();
        }
        if (changed.clearPlatformAuthKey)
          form.setFieldValue('platformAuthKey', '');
      }}
      onFinish={(values) =>
        onSave({
          provider: values.provider,
          label: values.label?.trim() || null,
          isEnabled: values.isEnabled,
          apiKey: values.apiKey?.trim() || null,
          platformAuthKey:
            values.provider === 'ankr' && !values.clearPlatformAuthKey
              ? values.platformAuthKey?.trim() || null
              : null,
          clearPlatformAuthKey:
            values.provider === 'ankr' && !!values.clearPlatformAuthKey,
          group:
            values.provider === 'ankr' ? values.group?.trim() || null : null,
          networks: values.networkIds.map((networkId) => ({
            networkId,
            logResultCap:
              values.provider === 'ankr'
                ? (values.logResultCaps?.[networkId] ?? null)
                : null,
          })),
        })
      }
    >
      <Form.Item
        name="provider"
        label="服务商"
        rules={[{ required: true, message: '选择服务商' }]}
      >
        <Select
          options={providerOptions}
          disabled={!!entry || saving}
          placeholder="选择 Ankr 或 Etherscan"
        />
      </Form.Item>
      <Form.Item name="label" label="备注（选填）">
        <Input name="label" maxLength={64} />
      </Form.Item>
      <Form.Item
        name="apiKey"
        label={provider === 'ankr' ? 'Ankr RPC 项目密钥' : 'API 密钥'}
        extra={
          entry ? `已保存 ${entry.keyMasked}。留空沿用已保存密钥。` : undefined
        }
        rules={[
          { required: !entry, whitespace: true, message: '请输入 API 密钥' },
        ]}
      >
        <Input.Password
          name="apiKey"
          autoComplete="new-password"
          autoCapitalize="none"
          spellCheck={false}
        />
      </Form.Item>
      <Form.Item
        name="networkIds"
        label="适用网络"
        extra={
          provider === 'etherscan'
            ? 'Etherscan V2 查询 BSC 需要支持该网络的付费套餐。'
            : undefined
        }
        rules={[
          {
            required: true,
            type: 'array',
            min: 1,
            message: '至少选择一个网络',
          },
        ]}
      >
        <Select
          mode="multiple"
          virtual={networks.length > 100}
          showSearch={{ optionFilterProp: 'label' }}
          options={networks.map(({ id, name, chainId }) => ({
            value: id,
            label: `${name} (${chainId})`,
          }))}
        />
      </Form.Item>
      {provider === 'ankr' && testNetworks.length > 0 && (
        <details style={{ marginBlockEnd: 16 }}>
          <summary>高级设置 · 日志响应上限</summary>
          <Typography.Paragraph type="secondary">
            按 Ankr 节点的单次日志结果条数上限填写。留空使用默认值。
          </Typography.Paragraph>
          {testNetworks.map(({ id, name }) => (
            <Form.Item
              key={id}
              name={['logResultCaps', id]}
              label={`${name} 单次日志响应上限`}
              rules={[
                { type: 'integer', min: 100, message: '请输入至少 100 的整数' },
              ]}
            >
              <InputNumber min={100} precision={0} />
            </Form.Item>
          ))}
        </details>
      )}
      {testNetworks.length > 1 && (
        <Form.Item label="扫链测试网络" htmlFor="scan-provider-test-network">
          <Select
            id="scan-provider-test-network"
            value={testNetwork}
            options={testNetworks.map(({ id, name }) => ({
              value: id,
              label: name,
            }))}
            onChange={(id) => {
              setPreferredTestNetwork(id);
              scan.clear();
            }}
          />
        </Form.Item>
      )}
      <Button
        htmlType="button"
        loading={scan.testing}
        onClick={async () => {
          const values = await form
            .validateFields(['provider', 'apiKey', 'networkIds'])
            .catch(() => null);
          if (!values || !testNetwork) return;
          await scan.test(
            () =>
              testScanProvider({
                id: entry?.id ?? null,
                provider: values.provider,
                apiKey: values.apiKey?.trim() || null,
                networkId: testNetwork,
              }),
            scanUnavailable,
          );
        }}
      >
        测试扫链
      </Button>
      <div role="status" style={{ marginBlock: 8 }}>
        {scan.result && (
          <Typography.Text type={scan.result.passed ? 'success' : 'danger'}>
            {scan.result.passed ? '测试通过' : '测试失败'} ·{' '}
            {scan.result.message}
          </Typography.Text>
        )}
      </div>
      {provider === 'ankr' && (
        <Card
          title="Ankr 账户余额（选填）"
          size="small"
          style={{ marginBlock: 16 }}
        >
          <Form.Item
            name="platformAuthKey"
            label="Ankr Platform Auth Key"
            extra={
              entry?.platformKeyMasked
                ? `已保存 ${entry.platformKeyMasked}。留空沿用。`
                : '查询该账户的 Credits 和美元余额。'
            }
          >
            <Input.Password
              name="platformAuthKey"
              maxLength={2048}
              disabled={saving || clearPlatformKey}
              autoComplete="new-password"
              autoCapitalize="none"
              spellCheck={false}
            />
          </Form.Item>
          <Form.Item
            name="group"
            label="Ankr 账户分组（选填）"
            extra="留空使用个人账户。"
            rules={[
              {
                transform: (value?: string) => value?.trim(),
                pattern: /^\S*$/,
                message: '分组标识不能包含空格',
              },
            ]}
          >
            <Input
              name="group"
              maxLength={128}
              autoCapitalize="none"
              spellCheck={false}
            />
          </Form.Item>
          {entry?.platformKeyMasked && (
            <Form.Item name="clearPlatformAuthKey" valuePropName="checked">
              <Checkbox>保存时清除账户查询密钥</Checkbox>
            </Form.Item>
          )}
          <Button
            htmlType="button"
            loading={balance.testing}
            disabled={saving || clearPlatformKey}
            onClick={async () => {
              const values = await form
                .validateFields(['platformAuthKey', 'group'])
                .catch(() => null);
              if (!values) return;
              const platformAuthKey = values.platformAuthKey?.trim() || null;
              const group = values.group?.trim() || null;
              if (!platformAuthKey && !entry?.platformKeyMasked) {
                form.setFields([
                  {
                    name: 'platformAuthKey',
                    errors: ['请输入要测试的账户查询密钥'],
                  },
                ]);
                form.scrollToField('platformAuthKey', { focus: true });
                return;
              }
              await balance.test(
                () =>
                  entry && !platformAuthKey && entry.group === group
                    ? getScanProviderBalance(entry.id)
                    : testScanProviderBalance({
                        id: entry?.id ?? null,
                        platformAuthKey,
                        group,
                      }),
                balanceUnavailable,
              );
            }}
          >
            查询账户余额
          </Button>
          <div role="status" style={{ marginBlockStart: 8 }}>
            {balance.result?.status === 'available' && (
              <AnkrBalanceAmounts {...balance.result} />
            )}
            {balance.result?.status === 'notConfigured' &&
              '未配置账户查询密钥。'}
            {balance.result?.status === 'unavailable' &&
              '暂时无法查询 Ankr 账户余额。'}
          </div>
        </Card>
      )}
      <Form.Item name="isEnabled" label="启用" valuePropName="checked">
        <Switch />
      </Form.Item>
      <Button htmlType="submit" type="primary" loading={saving}>
        保存服务商
      </Button>
    </Form>
  );
}

export default function ScanProvidersPage() {
  const [list, setList] = useState<ScanProviderListDto>();
  const [networks, setNetworks] = useState<CollectionNetworkSummaryDto[]>([]);
  const [editing, setEditing] = useState<ScanProviderDto | null>();
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const pending = useRef(false);
  const reorderFocus = useRef<HTMLElement>(null);
  const titleId = useId();

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [savedList, supportedNetworks] = await Promise.all([
        getScanProviders(),
        getNetworks(),
      ]);
      setList(savedList);
      setNetworks(supportedNetworks);
    } catch {
      setError('加载扫链服务商失败。');
    } finally {
      setLoading(false);
    }
  }, []);
  useEffect(() => {
    void load();
  }, [load]);

  useLayoutEffect(() => {
    if (reorderFocus.current && document.activeElement === document.body) {
      reorderFocus.current.focus({ preventScroll: true });
    }
    reorderFocus.current = null;
  }, [list]);

  async function save(
    action: () => Promise<unknown>,
    success: string,
    trigger?: HTMLElement,
  ) {
    if (pending.current) return false;
    pending.current = true;
    setSaving(true);
    setError('');
    setNotice('');
    try {
      await action();
      try {
        const updated = await getScanProviders();
        if (document.activeElement === trigger) reorderFocus.current = trigger;
        setList(updated);
        setNotice(success);
      } catch {
        setError('操作已保存，刷新列表失败。请重试。');
      }
      return true;
    } catch {
      setError('操作失败，请刷新后重试。');
      return false;
    } finally {
      pending.current = false;
      setSaving(false);
    }
  }

  const providers =
    list?.providers.toSorted(
      (left, right) => left.sortOrder - right.sortOrder,
    ) ?? [];
  const bscNetwork = networks.find(({ chainId }) => chainId === 56);
  const busy = loading || saving || !!error;

  function move(index: number, target: number, trigger: HTMLElement) {
    if (busy || target < 0 || target >= providers.length) return;
    const ids = providers.map(({ id }) => id);
    [ids[index], ids[target]] = [ids[target], ids[index]];
    return save(
      () => reorderScanProviders(ids),
      `${entryName(providers[index])} 已移到第 ${target + 1} 位`,
      trigger,
    );
  }

  return (
    <Card
      title={<span id={titleId}>扫链服务商</span>}
      extra={
        <Button
          type="primary"
          disabled={busy || !networks.length}
          onClick={() => setEditing(null)}
        >
          添加服务商
        </Button>
      }
    >
      <Typography.Paragraph type="secondary">
        充值扫链从适用网络的第一条已启用配置开始，查询失败时按顺序使用下一条。当前自动充值扫链运行于
        BSC。
      </Typography.Paragraph>
      {list?.legacyRpcActive && (
        <Alert
          showIcon
          type="info"
          title="BSC 仍保留原有 RPC"
          description="启用新的扫链配置后会优先尝试下方顺序。新的 Ankr RPC 成功读取日志后，原有 RPC 才会退出充值扫链。"
          style={{ marginBlockEnd: 12 }}
        />
      )}
      {list?.legacyBalancePending && (
        <Alert
          showIcon
          type="warning"
          title="已有 Ankr 余额凭据等待关联"
          description="请在对应的 Ankr 配置行选择「关联旧余额凭据」。"
          style={{ marginBlockEnd: 12 }}
        />
      )}
      {error && (
        <Alert
          showIcon
          type="error"
          title={error}
          action={<Button onClick={load}>刷新</Button>}
          style={{ marginBlockEnd: 12 }}
        />
      )}
      <div role="status" className="sr-only">
        {notice}
      </div>
      <Table<ScanProviderDto>
        aria-labelledby={titleId}
        rowKey="id"
        dataSource={providers}
        loading={loading}
        pagination={false}
        scroll={{ x: 820 }}
        locale={{ emptyText: '暂无第三方扫链服务商' }}
        columns={[
          {
            title: '顺序',
            key: 'order',
            render: (_, _entry, index) => index + 1,
          },
          {
            title: '服务商',
            dataIndex: 'provider',
            render: (type: ScanProvider) => providerNames[type],
          },
          {
            title: '备注',
            dataIndex: 'label',
            render: (label: string | null) => label || '—',
          },
          { title: 'API 密钥', dataIndex: 'keyMasked' },
          {
            title: '适用网络',
            key: 'networks',
            render: (_, entry) => (
              <Space wrap>
                {entry.networks.map(({ networkId }) => (
                  <Tag key={networkId}>
                    {networks.find(({ id }) => id === networkId)?.name ??
                      networkId}
                  </Tag>
                ))}
              </Space>
            ),
          },
          {
            title: '状态',
            key: 'enabled',
            render: (_, entry) => (
              <Tag color={entry.isEnabled ? 'success' : 'default'}>
                {entry.isEnabled ? '已启用' : '已停用'}
              </Tag>
            ),
          },
          {
            title: '操作',
            key: 'actions',
            render: (_, entry, index) => (
              <Space wrap size="small">
                <Button
                  type="link"
                  size="small"
                  aria-label={`编辑 ${entryName(entry)}`}
                  disabled={busy}
                  onClick={() => setEditing(entry)}
                >
                  编辑
                </Button>
                <Button
                  type="link"
                  size="small"
                  aria-label={`上移 ${entryName(entry)}`}
                  aria-disabled={busy || index === 0}
                  className="aria-disabled:opacity-50"
                  onClick={(event) =>
                    move(index, index - 1, event.currentTarget)
                  }
                >
                  上移
                </Button>
                <Button
                  type="link"
                  size="small"
                  aria-label={`下移 ${entryName(entry)}`}
                  aria-disabled={busy || index === providers.length - 1}
                  className="aria-disabled:opacity-50"
                  onClick={(event) =>
                    move(index, index + 1, event.currentTarget)
                  }
                >
                  下移
                </Button>
                {list?.legacyBalancePending &&
                  entry.provider === 'ankr' &&
                  !entry.platformKeyMasked &&
                  entry.networks.some(
                    ({ networkId }) => networkId === bscNetwork?.id,
                  ) && (
                    <Popconfirm
                      title={`将原有余额查询凭据关联到 ${entryName(entry)}？`}
                      onConfirm={() =>
                        save(
                          () => claimLegacyScanProviderBalance(entry.id),
                          '已关联 Ankr 余额凭据',
                        )
                      }
                    >
                      <Button type="link" size="small" disabled={busy}>
                        关联旧余额凭据
                      </Button>
                    </Popconfirm>
                  )}
                <Popconfirm
                  title={`删除 ${entryName(entry)}？`}
                  onConfirm={() =>
                    save(() => deleteScanProvider(entry.id), '扫链服务商已删除')
                  }
                >
                  <Button
                    danger
                    type="link"
                    size="small"
                    aria-label={`删除 ${entryName(entry)}`}
                    disabled={busy}
                  >
                    删除
                  </Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />
      <Drawer
        title={
          editing ? `编辑 ${providerNames[editing.provider]}` : '添加扫链服务商'
        }
        open={editing !== undefined}
        size="min(620px, 100%)"
        focusable={{ trap: true, focusTriggerAfterClose: true }}
        mask={{ closable: false }}
        styles={{ body: { overscrollBehavior: 'contain' } }}
        onClose={() => {
          if (!saving) setEditing(undefined);
        }}
      >
        {editing !== undefined && (
          <>
            {error && (
              <Alert
                type="error"
                showIcon
                title={error}
                style={{ marginBlockEnd: 12 }}
              />
            )}
            <ProviderEditor
              key={editing?.id ?? 'new'}
              entry={editing ?? undefined}
              networks={networks}
              saving={saving}
              onSave={async (values) => {
                if (
                  await save(
                    () =>
                      editing
                        ? updateScanProvider(editing.id, values)
                        : createScanProvider(values),
                    '扫链服务商已保存',
                  )
                ) {
                  setEditing(undefined);
                }
              }}
            />
          </>
        )}
      </Drawer>
    </Card>
  );
}
