import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Collapse,
  Empty,
  Form,
  Input,
  Modal,
  Row,
  Select,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  type CollectionNetworkSummaryDto,
  type CollectionWalletStatusDto,
  generateMnemonic,
  getWalletNetworks,
  getWalletStatus,
  setVaultAddress,
} from '@/abp/chainAdmin';
import { setWithdrawVault } from '@/abp/withdrawal';

const { Text, Paragraph } = Typography;

function WalletConfiguration({ networkId }: { networkId: string }) {
  const access = useAccess();
  const { message, modal } = App.useApp();
  const mounted = useRef(false);
  const mnemonicConfirmation = useRef<{ destroy: () => void }>(undefined);
  const [mnemonic, setMnemonic] = useState<string>();
  const [status, setStatus] = useState<CollectionWalletStatusDto>();
  const currentStatus = useRef<CollectionWalletStatusDto>(undefined);
  const [balances, setBalances] = useState<CollectionWalletStatusDto>();
  const configurationVersion = useRef(0);
  const [balanceLoading, setBalanceLoading] = useState(false);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [balanceError, setBalanceError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [withdrawForm] = Form.useForm<{
    address: string;
    privateKey: string;
  }>();
  const [vaultForm] = Form.useForm<{ symbol: string; address: string }>();

  const load = useCallback(
    async (
      includeBalances = false,
      refreshForms: 'all' | 'vault' | 'withdraw' | 'none' = 'all',
    ) => {
      if (!mounted.current) return;
      if (includeBalances) {
        const version = configurationVersion.current;
        setBalanceLoading(true);
        try {
          const value = await getWalletStatus(networkId, true);
          if (version === configurationVersion.current) {
            const current = currentStatus.current;
            const changed =
              current &&
              (current.withdrawAddress !== value.withdrawAddress ||
                current.fuelAddress !== value.fuelAddress ||
                current.tokens.length !== value.tokens.length ||
                value.tokens.some(
                  (token) =>
                    current.tokens.find((item) => item.symbol === token.symbol)
                      ?.vaultAddress !== token.vaultAddress,
                ));
            if (changed) {
              // 余额对应本次 RPC 查询的地址，保留表单中未保存的地址。
              currentStatus.current = value;
              setStatus(value);
            }
            setBalances(value);
            setBalanceError(!value.balancesAvailable);
          }
        } catch {
          if (version === configurationVersion.current) setBalanceError(true);
        } finally {
          setBalanceLoading(false);
        }
        return;
      }

      // 配置重读后，忽略仍在查询旧地址的响应。
      const version = ++configurationVersion.current;
      setBalances(undefined);
      setBalanceError(false);
      setLoading(true);
      try {
        const value = await getWalletStatus(networkId, false);
        if (version !== configurationVersion.current) return;
        currentStatus.current = value;
        setStatus(value);
        setFailed(false);
        if (refreshForms === 'all' || refreshForms === 'withdraw') {
          withdrawForm.setFieldsValue({
            address: value.withdrawAddress ?? '',
            privateKey: '',
          });
        }
        if (refreshForms === 'all' || refreshForms === 'vault') {
          const symbol = vaultForm.getFieldValue('symbol');
          const token =
            value.tokens.find((item) => item.symbol === symbol) ??
            value.tokens[0];
          if (token) {
            vaultForm.setFieldsValue({
              symbol: token.symbol,
              address: token.vaultAddress ?? '',
            });
          }
        }
      } catch {
        if (version === configurationVersion.current) setFailed(true);
      } finally {
        if (version === configurationVersion.current) setLoading(false);
      }
    },
    [networkId, withdrawForm, vaultForm],
  );

  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      configurationVersion.current++;
      mnemonicConfirmation.current?.destroy();
    };
  }, [load]);

  async function save(
    action: () => Promise<unknown>,
    text: string,
    area: 'vault' | 'withdraw',
  ) {
    setSaving(true);
    try {
      await action();
      message.success(text);
      await load(false, area);
    } catch {
      /* API 请求层显示错误，保留当前输入供重试。 */
    } finally {
      setSaving(false);
    }
  }

  function createMnemonic() {
    mnemonicConfirmation.current = modal.confirm({
      title: '生成充值总密钥',
      content:
        '系统将生成新的 24 词助记词。已有充值地址或未完成归集时会拒绝操作。生成后请立即离线备份。',
      okText: '生成',
      cancelText: '取消',
      onOk: async () => {
        const words = await generateMnemonic(networkId);
        if (!mounted.current) return;
        setMnemonic(words);
        void load(false, 'none');
      },
    });
  }

  if (failed && !status)
    return (
      <Alert
        type="error"
        title="加载链上钱包失败"
        action={<Button onClick={() => load()}>重试</Button>}
      />
    );
  if (!status) return <Spin />;

  const address = (value: string | null) =>
    value ? (
      <Text copyable style={{ overflowWrap: 'anywhere' }}>
        {value}
      </Text>
    ) : (
      <Tag color="warning">未配置</Tag>
    );
  const configuration = [];
  if (access.canAdminNetwork) {
    configuration.push({
      key: 'vault',
      label: '平台收款地址',
      children: (
        <Form
          name="vault"
          form={vaultForm}
          disabled={saving}
          layout="vertical"
          onFinish={(values) =>
            save(
              () => setVaultAddress(values.symbol, values.address, networkId),
              '平台收款地址已保存',
              'vault',
            )
          }
        >
          <Paragraph type="secondary">用户充值归集后进入此地址。</Paragraph>
          <Form.Item name="symbol" label="代币" rules={[{ required: true }]}>
            <Select
              options={status.tokens.map((token) => ({
                label: token.symbol,
                value: token.symbol,
              }))}
              onChange={(symbol) =>
                vaultForm.setFieldValue(
                  'address',
                  status.tokens.find((token) => token.symbol === symbol)
                    ?.vaultAddress ?? '',
                )
              }
            />
          </Form.Item>
          <Form.Item
            name="address"
            label="收款地址"
            rules={[
              { required: true },
              {
                pattern: /^0x[0-9a-fA-F]{40}$/,
                message: '请输入有效的链上地址',
              },
            ]}
          >
            <Input />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={saving}>
            保存收款地址
          </Button>
        </Form>
      ),
    });
    configuration.push({
      key: 'mnemonic',
      label: '充值总密钥',
      children: (
        <Space orientation="vertical">
          <Paragraph>
            用于派生用户充值地址和归集加油地址。已有密钥以配置状态显示。
          </Paragraph>
          <Tag color={status.hasMnemonic ? 'success' : 'warning'}>
            {status.hasMnemonic ? '已配置' : '未配置'}
          </Tag>
          <Button danger onClick={createMnemonic}>
            生成充值总密钥
          </Button>
        </Space>
      ),
    });
  }
  if (access.canAdminWithdrawals && status.isWithdrawalNetwork) {
    configuration.push({
      key: 'withdraw',
      label: '提现打款钱包',
      children: (
        <Form
          name="withdraw"
          form={withdrawForm}
          disabled={saving}
          layout="vertical"
          onFinish={(values) =>
            save(
              () =>
                setWithdrawVault(values.address, values.privateKey, networkId),
              '提现钱包已保存',
              'withdraw',
            )
          }
        >
          <Paragraph type="secondary">
            保存时会核对地址与私钥。之后的广播使用此钱包，早先的广播仍按当时使用的钱包核对。
          </Paragraph>
          <Form.Item
            name="address"
            label="打款地址"
            rules={[
              { required: true },
              {
                pattern: /^0x[0-9a-fA-F]{40}$/,
                message: '请输入有效的链上地址',
              },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="privateKey"
            label="私钥"
            rules={[{ required: true }]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={saving}>
            保存提现钱包
          </Button>
        </Form>
      ),
    });
  }

  return (
    <Spin spinning={loading}>
      <Space orientation="vertical" size={16} style={{ width: '100%' }}>
        {failed && (
          <Alert
            type="error"
            title="刷新钱包配置失败"
            action={<Button onClick={() => load(false, 'none')}>重试</Button>}
          />
        )}
        <Space>
          <Button
            onClick={() => load(true)}
            loading={balanceLoading}
            disabled={loading || saving}
          >
            查询钱包余额
          </Button>
          <Text type="secondary">网络 {status.networkName}</Text>
        </Space>
        {balanceError && (
          <Alert
            type="warning"
            showIcon
            title="暂时无法读取链上余额，请检查节点配置后重试。"
          />
        )}
        <Row gutter={[16, 16]}>
          <Col xs={24} lg={8}>
            <Card title="平台收款钱包" style={{ height: '100%' }}>
              <Space orientation="vertical" style={{ width: '100%' }}>
                {status.tokens.map((token) => (
                  <div key={token.symbol}>
                    <Paragraph>{token.symbol}</Paragraph>
                    <Paragraph>{address(token.vaultAddress)}</Paragraph>
                    {balances && (
                      <Text type="secondary">
                        余额{' '}
                        {balances.tokens.find(
                          (item) => item.symbol === token.symbol,
                        )?.vaultBalance ?? '—'}
                      </Text>
                    )}
                  </div>
                ))}
                {!status.tokens.length && <Empty description="暂无代币通道" />}
              </Space>
            </Card>
          </Col>
          <Col xs={24} lg={8}>
            <Card title="提现打款钱包" style={{ height: '100%' }}>
              <Space orientation="vertical">
                {!status.isWithdrawalNetwork && (
                  <Text type="secondary">
                    当前提现使用 {status.withdrawalNetworkName ?? '未配置'} 网络
                  </Text>
                )}
                {address(status.withdrawAddress)}
                <Tag color={status.hasWithdrawKey ? 'success' : 'warning'}>
                  {status.hasWithdrawKey ? '私钥已配置' : '私钥未配置'}
                </Tag>
                {balances && (
                  <>
                    <Text>BNB {balances.withdrawBnb ?? '—'}</Text>
                    {balances.tokens.map((token) => (
                      <Text key={token.symbol}>
                        {token.symbol} {token.withdrawBalance ?? '—'}
                      </Text>
                    ))}
                  </>
                )}
              </Space>
            </Card>
          </Col>
          <Col xs={24} lg={8}>
            <Card title="归集加油钱包" style={{ height: '100%' }}>
              <Space orientation="vertical">
                {status.hasMnemonic && !status.fuelAddress ? (
                  <Tag color="warning">地址暂不可读取</Tag>
                ) : (
                  address(status.fuelAddress)
                )}
                {balances && <Text>BNB {balances.fuelBnb ?? '—'}</Text>}
                <Text type="secondary">
                  由充值总密钥派生，用于支付归集手续费。
                </Text>
              </Space>
            </Card>
          </Col>
        </Row>
        <Collapse items={configuration} />
        <Modal
          title="备份充值总密钥"
          open={!!mnemonic}
          width={600}
          footer={<Button onClick={() => setMnemonic(undefined)}>关闭</Button>}
          onCancel={() => setMnemonic(undefined)}
          mask={{ closable: false }}
        >
          <Input.TextArea readOnly value={mnemonic ?? ''} rows={4} />
        </Modal>
      </Space>
    </Spin>
  );
}

export default function CollectionWalletsPage() {
  const access = useAccess();
  const [networks, setNetworks] = useState<CollectionNetworkSummaryDto[]>([]);
  const [networkId, setNetworkId] = useState<string>();
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const loadNetworks = useCallback(async () => {
    if (!access.canManageCollectionWallets) return;
    setLoading(true);
    try {
      const values = await getWalletNetworks();
      setNetworks(values);
      setNetworkId((current) =>
        values.some((row) => row.id === current) ? current : values[0]?.id,
      );
      setFailed(false);
    } catch {
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, [access.canManageCollectionWallets]);
  useEffect(() => {
    void loadNetworks();
  }, [loadNetworks]);
  if (!access.canManageCollectionWallets) {
    return <Empty description="暂无链上钱包管理权限" />;
  }

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      <Select
        aria-label="选择链网络"
        value={networkId}
        style={{ minWidth: 200 }}
        loading={loading}
        options={networks.map((row) => ({
          value: row.id,
          label: row.name,
        }))}
        onChange={setNetworkId}
      />
      {failed ? (
        <Alert
          type="error"
          title="加载网络列表失败"
          action={<Button onClick={loadNetworks}>重试</Button>}
        />
      ) : networkId ? (
        <WalletConfiguration key={networkId} networkId={networkId} />
      ) : (
        !loading && <Empty description="暂无链网络" />
      )}
    </Space>
  );
}
