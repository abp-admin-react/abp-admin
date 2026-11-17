import {
  Alert,
  App,
  Button,
  Card,
  Form,
  Input,
  Popconfirm,
  Space,
  Spin,
  Switch,
  Table,
  Typography,
} from 'antd';
import { useEffect, useState } from 'react';
import {
  type ChainApiKeyDto,
  type ChainApiKeysDto,
  configureTransactionRpc,
  createNetworkApiKey,
  getApiKeys,
  getTransactionRpc,
  removeApiKey,
  setApiKeyEnabled,
  type TestNodeCredentialDto,
  testNodeCredential,
} from '@/abp/chainAdmin';
import { isNotFound } from '@/requestErrorConfig';
import { useCredentialProbe } from './useCredentialProbe';

const unavailable: TestNodeCredentialDto = {
  passed: false,
  message: '暂时无法验证，请稍后重试。',
  credits: null,
  balanceUsd: null,
};

function TransactionKeyPool({
  networkId,
  provider,
  keys,
  saving,
  save,
}: {
  networkId: string;
  provider: 'RpcBsc' | 'EtherscanV2';
  keys: ChainApiKeyDto[];
  saving: boolean;
  save: (action: () => Promise<unknown>, success: string) => Promise<boolean>;
}) {
  const [keyForm] = Form.useForm<{ key: string; label?: string }>();
  const [testedKey, setTestedKey] = useState<string>();
  const probe = useCredentialProbe<TestNodeCredentialDto>();
  const isAnkr = provider === 'RpcBsc';
  const title = isAnkr ? '交易用 Ankr 项目密钥' : 'Etherscan 补查密钥';
  return (
    <Card title={title} size="small">
      <Form
        name={provider}
        aria-label={title}
        form={keyForm}
        layout="vertical"
        disabled={saving}
        method="post"
        onValuesChange={() => probe.clear()}
        onFinish={async ({ key, label }) => {
          if (
            await save(
              () =>
                createNetworkApiKey({
                  networkId,
                  provider,
                  key: key.trim(),
                  label,
                }),
              '密钥已添加',
            )
          ) {
            keyForm.resetFields();
            probe.clear();
          }
        }}
      >
        <Form.Item
          name="key"
          label={isAnkr ? 'Ankr 项目密钥' : 'Etherscan API 密钥'}
          rules={[
            { required: true, whitespace: true, message: '请输入 API 密钥' },
          ]}
        >
          <Input.Password
            name="key"
            autoComplete="new-password"
            spellCheck={false}
          />
        </Form.Item>
        <Form.Item name="label" label="备注（选填）">
          <Input name="label" maxLength={128} />
        </Form.Item>
        <Space wrap>
          <Button
            htmlType="button"
            loading={probe.testing && testedKey === undefined}
            disabled={probe.testing}
            onClick={async () => {
              const values = await keyForm
                .validateFields(['key'])
                .catch(() => undefined);
              if (!values) return;
              setTestedKey(undefined);
              await probe.test(
                () =>
                  testNodeCredential({
                    networkId,
                    provider,
                    key: values.key.trim(),
                  }),
                unavailable,
              );
            }}
          >
            测试连接
          </Button>
          <Button type="primary" htmlType="submit" loading={saving}>
            添加密钥
          </Button>
        </Space>
      </Form>
      <Table<ChainApiKeyDto>
        rowKey="id"
        dataSource={keys}
        pagination={false}
        size="small"
        scroll={{ x: 440 }}
        style={{ marginBlockStart: 16 }}
        locale={{ emptyText: '暂无密钥' }}
        columns={[
          { title: '备注', dataIndex: 'label', ellipsis: true },
          { title: '密钥', dataIndex: 'maskedKey' },
          {
            title: '启用',
            render: (_, row) => (
              <Switch
                aria-label={`启用密钥 ${row.maskedKey}`}
                checked={row.isEnabled}
                disabled={saving}
                onChange={(enabled) =>
                  save(
                    () => setApiKeyEnabled(row.id, enabled, networkId),
                    '密钥状态已更新',
                  )
                }
              />
            ),
          },
          {
            title: '操作',
            render: (_, row) => (
              <Space wrap size="small">
                <Button
                  type="link"
                  size="small"
                  aria-label={`测试已保存密钥 ${row.maskedKey}`}
                  loading={probe.testing && testedKey === row.id}
                  disabled={saving || probe.testing}
                  onClick={() => {
                    setTestedKey(row.id);
                    return probe.test(
                      () =>
                        testNodeCredential({
                          networkId,
                          provider,
                          keyId: row.id,
                        }),
                      unavailable,
                    );
                  }}
                >
                  测试
                </Button>
                <Popconfirm
                  title="删除这把密钥？"
                  onConfirm={() => {
                    probe.clear();
                    return save(
                      () => removeApiKey(row.id, networkId),
                      '密钥已删除',
                    );
                  }}
                >
                  <Button danger type="link" size="small" disabled={saving}>
                    删除
                  </Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />
      <div role="status" style={{ marginBlockStart: 8 }}>
        {probe.result && (
          <Typography.Text type={probe.result.passed ? 'success' : 'danger'}>
            {keys.find(({ id }) => id === testedKey)?.maskedKey}{' '}
            {probe.result.passed ? '测试通过' : '测试失败'} ·{' '}
            {probe.result.message}
          </Typography.Text>
        )}
      </div>
    </Card>
  );
}

export default function TransactionRpc({
  networkId,
  isBsc,
  onSaved,
}: {
  networkId: string;
  isBsc: boolean;
  onSaved: () => void;
}) {
  const { message } = App.useApp();
  const [urlForm] = Form.useForm<{ rpcUrl?: string }>();
  const [pools, setPools] = useState<ChainApiKeysDto>();
  const [maskedUrl, setMaskedUrl] = useState<string | null>();
  const [revision, setRevision] = useState(0);
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let active = true;
    setFailed(false);
    Promise.all([
      getTransactionRpc(networkId),
      isBsc ? getApiKeys(networkId) : Promise.resolve(undefined),
    ]).then(
      ([rpc, pool]) => {
        if (!active) return;
        setPools(pool);
        setMaskedUrl(rpc.rpcUrlMasked);
      },
      () => {
        if (active) setFailed(true);
      },
    );
    return () => {
      active = false;
    };
  }, [isBsc, networkId, revision]);

  async function save(action: () => Promise<unknown>, success: string) {
    setSaving(true);
    try {
      await action();
      message.success(success);
      setRevision((value) => value + 1);
      onSaved();
      return true;
    } catch (error) {
      if (isNotFound(error)) message.error('无法保存交易 RPC，请刷新后重试。');
      return false;
    } finally {
      setSaving(false);
    }
  }

  if (failed) {
    return (
      <Alert
        type="error"
        title="加载交易 RPC 失败"
        action={
          <Button onClick={() => setRevision((value) => value + 1)}>
            重试
          </Button>
        }
      />
    );
  }
  if (maskedUrl === undefined) return <Spin aria-label="加载交易 RPC" />;

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      <Typography.Text type="secondary">
        用于钱包余额查询、归集和提现。这里的配置与充值扫链的服务商顺序独立。
      </Typography.Text>
      {pools && (
        <TransactionKeyPool
          networkId={networkId}
          provider="RpcBsc"
          keys={pools.rpcKeys}
          saving={saving}
          save={save}
        />
      )}
      <Card title="自定义交易 RPC" size="small">
        <Typography.Paragraph type="secondary">
          {isBsc
            ? '没有启用的交易用 Ankr 密钥时使用此地址。'
            : '用于读取及提交该网络的链上交易。'}
        </Typography.Paragraph>
        <Form
          name="transaction-rpc-url"
          aria-label="自定义交易 RPC"
          method="post"
          form={urlForm}
          layout="vertical"
          disabled={saving}
          onFinish={async ({ rpcUrl }) => {
            if (
              await save(
                () =>
                  configureTransactionRpc({
                    networkId,
                    rpcUrl: rpcUrl?.trim() || null,
                  }),
                '交易 RPC 已保存',
              )
            )
              urlForm.resetFields();
          }}
        >
          <Form.Item
            name="rpcUrl"
            label="RPC 地址"
            extra={
              maskedUrl ? `已保存 ${maskedUrl}。留空沿用已保存地址。` : '未配置'
            }
          >
            <Input.Password
              name="rpcUrl"
              placeholder="https://"
              autoComplete="new-password"
              spellCheck={false}
            />
          </Form.Item>
          <Space wrap>
            <Button htmlType="submit" type="primary" loading={saving}>
              保存交易 RPC
            </Button>
            {maskedUrl && (
              <Popconfirm
                title="清除自定义交易 RPC 地址？"
                onConfirm={async () => {
                  if (
                    await save(
                      () =>
                        configureTransactionRpc({
                          networkId,
                          clearRpcUrl: true,
                        }),
                      '交易 RPC 地址已清除',
                    )
                  )
                    urlForm.resetFields();
                }}
              >
                <Button danger htmlType="button">
                  清除地址
                </Button>
              </Popconfirm>
            )}
          </Space>
        </Form>
      </Card>
      {pools && (
        <details>
          <summary>高级设置 · Etherscan 交易补查</summary>
          <Typography.Paragraph type="secondary">
            用于钱包监控和交易恢复时按地址补查转账。这组密钥独立于充值扫链。
          </Typography.Paragraph>
          <TransactionKeyPool
            networkId={networkId}
            provider="EtherscanV2"
            keys={pools.etherscanKeys}
            saving={saving}
            save={save}
          />
        </details>
      )}
    </Space>
  );
}
