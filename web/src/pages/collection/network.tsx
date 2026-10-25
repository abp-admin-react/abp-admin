import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  addApiKey,
  type ChainApiKeyDto,
  generateMnemonic,
  getApiKeys,
  getNetworkStatus,
  type NetworkStatusDto,
  removeApiKey,
  setApiKeyEnabled,
  setChainSwitches,
  setDepositPolicy,
  setMinDeposit,
  setNodeLogResultCap,
  // 组件内有同名 useState setter（rpcUrl 输入框），别名避免遮蔽——
  // 不别名的话 submitRpc 里 await 的是 setter，后端接口永远不会被调用
  setRpcUrl as setRpcUrlApi,
  setTokenEnabled,
  setVaultAddress,
  type TokenStatusDto,
} from '@/abp/chainAdmin';
import {
  type ChainConnState,
  type ChainScanProgressPayload,
  onChainConnState,
  onChainScanProgress,
  startChainSignalR,
} from '@/abp/chainSignalR';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text, Paragraph } = Typography;

/**
 * 链网配置页（/collection/network）。扫链进度由 SignalR 实时推送（ChainScanProgress），无需刷新按钮。
 */
export default function NetworkConfigPage() {
  const { message, modal } = App.useApp();
  const [status, setStatus] = useState<NetworkStatusDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const [progressMap, setProgressMap] = useState<
    Map<string, ChainScanProgressPayload>
  >(new Map());

  const [rpcOpen, setRpcOpen] = useState(false);
  const [rpcUrl, setRpcUrl] = useState('');
  const [rpcSaving, setRpcSaving] = useState(false);
  const [capOpen, setCapOpen] = useState(false);
  const [capVal, setCapVal] = useState<number>(10000);
  const [capSaving, setCapSaving] = useState(false);
  const [vaultOpen, setVaultOpen] = useState(false);
  const [vaultSymbol, setVaultSymbol] = useState('USDT');
  const [vaultAddr, setVaultAddr] = useState('');
  const [vaultSaving, setVaultSaving] = useState(false);
  const [minDepOpen, setMinDepOpen] = useState(false);
  const [minDepSymbol, setMinDepSymbol] = useState('USDT');
  const [minDepVal, setMinDepVal] = useState('');
  const [minDepSaving, setMinDepSaving] = useState(false);
  const [policyOpen, setPolicyOpen] = useState(false);
  const [policyFeeRate, setPolicyFeeRate] = useState('');
  const [policyMax, setPolicyMax] = useState('');
  const [policySaving, setPolicySaving] = useState(false);
  const [mnemonicResult, setMnemonicResult] = useState<string | null>(null);

  // 扫链 KEY 池（同服务商多把，网关遇限流/认证错误自动轮换）
  const [rpcKeys, setRpcKeys] = useState<ChainApiKeyDto[]>([]);
  const [etherscanKeys, setEtherscanKeys] = useState<ChainApiKeyDto[]>([]);
  const [rpcFallbackMasked, setRpcFallbackMasked] = useState<string | null>(
    null,
  );
  const [etherscanFallbackPresent, setEtherscanFallbackPresent] =
    useState(false);
  const [newKeyProvider, setNewKeyProvider] = useState<string>('RpcBsc');
  const [newKeyValue, setNewKeyValue] = useState('');
  const [newKeyLabel, setNewKeyLabel] = useState('');
  const [newKeySaving, setNewKeySaving] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setStatus(await getNetworkStatus());
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
    try {
      const keys = await getApiKeys();
      setRpcKeys(keys.rpcKeys ?? []);
      setEtherscanKeys(keys.etherscanKeys ?? []);
      setRpcFallbackMasked(keys.rpcFallbackMasked ?? null);
      setEtherscanFallbackPresent(keys.etherscanFallbackPresent);
    } catch {
      /* interceptor */
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offProgress = onChainScanProgress((p) => {
      setProgressMap((prev) => {
        const next = new Map(prev);
        next.set(p.symbol, p);
        return next;
      });
    });
    startChainSignalR();
    return () => {
      offConn();
      offProgress();
    };
  }, []);

  const mergedTokens: TokenStatusDto[] = (status?.tokens ?? []).map((t) => {
    const p = progressMap.get(t.symbol);
    if (!p) return t;
    return {
      ...t,
      lastScannedBlock: p.lastScannedBlock ?? t.lastScannedBlock,
      lastScanAt: p.lastScanAt ?? t.lastScanAt,
      isEnabled: p.isEnabled,
    };
  });
  const mergedHead =
    [...progressMap.values()][0]?.latestChainHead ?? status?.latestChainHead;

  const submitRpc = async () => {
    if (!rpcUrl.startsWith('https://')) {
      message.warning('RPC 地址必须以 https:// 开头');
      return;
    }
    setRpcSaving(true);
    try {
      await setRpcUrlApi(rpcUrl);
      message.success('RPC 已配置');
      setRpcOpen(false);
      setRpcUrl('');
      reload();
    } finally {
      setRpcSaving(false);
    }
  };
  const submitCap = async () => {
    if (capVal < 100) {
      message.warning('上限必须不小于 100');
      return;
    }
    setCapSaving(true);
    try {
      await setNodeLogResultCap(capVal);
      message.success('测量上限已更新');
      setCapOpen(false);
      reload();
    } finally {
      setCapSaving(false);
    }
  };
  const submitVault = async () => {
    if (!/^0x[0-9a-fA-F]{40}$/.test(vaultAddr)) {
      message.warning('金库地址需 0x 开头的 40 位十六进制');
      return;
    }
    setVaultSaving(true);
    try {
      await setVaultAddress(vaultSymbol, vaultAddr);
      message.success('金库地址已更新');
      setVaultOpen(false);
      setVaultAddr('');
      reload();
    } finally {
      setVaultSaving(false);
    }
  };
  const submitMinDep = async () => {
    const v = parseFloat(minDepVal);
    if (Number.isNaN(v) || v <= 0) {
      message.warning('最低充值额必须为正数');
      return;
    }
    setMinDepSaving(true);
    try {
      await setMinDeposit(minDepSymbol, minDepVal);
      message.success('最低充值额已更新');
      setMinDepOpen(false);
      reload();
    } finally {
      setMinDepSaving(false);
    }
  };
  const submitPolicy = async () => {
    const fee = parseFloat(policyFeeRate);
    const max = parseFloat(policyMax);
    if (Number.isNaN(fee) || fee < 0 || fee > 1) {
      message.warning('充值费率必须是 [0,1] 内的小数');
      return;
    }
    if (Number.isNaN(max) || max < 0) {
      message.warning('大额放行门必须 ≥ 0（0 = 全部等放行）');
      return;
    }
    setPolicySaving(true);
    try {
      // 发送解析后的值（原始串里 1e-3/尾空格类输入能过 parseFloat 但过不了后端模型绑定）
      await setDepositPolicy(String(fee), String(max));
      message.success('充值政策已更新（费率只影响新单——建单快照）');
      setPolicyOpen(false);
      reload();
    } catch {
      /* interceptor：全局弹错，Modal 保持打开可改可重试 */
    } finally {
      setPolicySaving(false);
    }
  };
  const doToggleSwitch = async (
    key: 'scanEnabled' | 'sweepEnabled' | 'hotWalletWatchEnabled',
    checked: boolean,
  ) => {
    const label =
      key === 'scanEnabled' ? '扫链' : key === 'sweepEnabled' ? '归集' : '热钱包监控';
    try {
      await setChainSwitches({ [key]: checked });
      message.success(`${label}已${checked ? '开启' : '关闭'}（worker 下一轮生效）`);
      reload();
    } catch {
      /* interceptor */
    }
  };
  const doGenerateMnemonic = () => {
    modal.confirm({
      title: '生成助记词',
      content:
        '将生成新的 24 词助记词。如果已有充值地址或未完成归集，会被拒绝。助记词只显示一次，请立即抄写并安全保管。',
      okText: '生成',
      cancelText: '取消',
      onOk: async () => {
        try {
          setMnemonicResult(await generateMnemonic());
        } catch {
          /* interceptor */
        }
      },
    });
  };

  const submitAddKey = async () => {
    const k = newKeyValue.trim();
    if (newKeyProvider === 'RpcBsc' && !/^[0-9a-fA-F]{64}$/.test(k)) {
      message.warning('Ankr 项目密钥为 64 位十六进制');
      return;
    }
    if (newKeyProvider === 'EtherscanV2' && !/^[A-Za-z0-9]{34}$/.test(k)) {
      message.warning('Etherscan API Key 为 34 位字母数字');
      return;
    }
    setNewKeySaving(true);
    try {
      await addApiKey(newKeyProvider, k, newKeyLabel.trim() || undefined);
      message.success('KEY 已添加并进入轮换（下一轮扫链生效）');
      setNewKeyValue('');
      setNewKeyLabel('');
      reload();
    } catch {
      /* interceptor */
    } finally {
      setNewKeySaving(false);
    }
  };
  const doRemoveKey = async (row: ChainApiKeyDto) => {
    try {
      await removeApiKey(row.id);
      message.success('KEY 已删除');
      reload();
    } catch {
      /* interceptor */
    }
  };
  const doToggleKey = async (row: ChainApiKeyDto, checked: boolean) => {
    try {
      await setApiKeyEnabled(row.id, checked);
      message.success(`KEY 已${checked ? '启用' : '停用'}`);
      reload();
    } catch {
      /* interceptor */
    }
  };

  const fmtBlock = (v?: number) =>
    v == null ? '-' : Number(v).toLocaleString();
  const fmtTime = (v?: string) => {
    if (!v) return '-';
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };
  const connView =
    connState === 'connected'
      ? { color: 'success' as const, text: '实时推送：已连接' }
      : connState === 'connecting'
        ? { color: 'warning' as const, text: '实时推送：连接中…连上后自动更新' }
        : { color: 'error' as const, text: '实时推送：已断开，自动重连中' };

  const tokenColumns = [
    { title: '代币', dataIndex: 'symbol', width: 80 },
    {
      title: '合约',
      dataIndex: 'contractAddress',
      width: 140,
      ellipsis: true,
      render: (v: string) => (
        <Text
          copyable={{ text: v }}
          style={{ fontSize: 12 }}
        >{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text>
      ),
    },
    {
      title: '最低充值',
      dataIndex: 'minDeposit',
      width: 100,
      render: (v: string, row: TokenStatusDto) => (
        <Space size={4}>
          <Text>{v}</Text>
          <Button
            size="small"
            type="link"
            onClick={() => {
              setMinDepSymbol(row.symbol);
              setMinDepVal(row.minDeposit);
              setMinDepOpen(true);
            }}
          >
            改
          </Button>
        </Space>
      ),
    },
    {
      title: '金库地址',
      dataIndex: 'vaultAddress',
      width: 140,
      ellipsis: true,
      render: (v: string | null | undefined, row: TokenStatusDto) => (
        <Space size={4}>
          {v ? (
            <Text
              copyable={{ text: v }}
              style={{ fontSize: 12 }}
            >{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text>
          ) : (
            <Tag color="warning">未配置</Tag>
          )}
          <Button
            size="small"
            type="link"
            onClick={() => {
              setVaultSymbol(row.symbol);
              setVaultAddr(row.vaultAddress ?? '');
              setVaultOpen(true);
            }}
          >
            改
          </Button>
        </Space>
      ),
    },
    {
      title: '扫链游标',
      dataIndex: 'lastScannedBlock',
      width: 120,
      render: (v?: number | null) => (
        <Text style={{ fontFamily: 'monospace' }}>
          {fmtBlock(v ?? undefined)}
        </Text>
      ),
    },
    {
      title: '落后',
      dataIndex: 'blocksBehind',
      width: 80,
      render: (v?: number | null) =>
        v != null ? (
          <span
            style={{
              color: v > 5000 ? '#cf1322' : v > 1000 ? '#fa8c16' : '#389e0d',
            }}
          >
            {fmtBlock(v)}
          </span>
        ) : (
          '-'
        ),
    },
    {
      title: '最近扫链',
      dataIndex: 'lastScanAt',
      width: 150,
      render: (v?: string | null) => fmtTime(v ?? undefined),
    },
    {
      title: '启用',
      dataIndex: 'isEnabled',
      width: 70,
      render: (v: boolean, row: TokenStatusDto) => (
        <Switch
          checked={v}
          size="small"
          onChange={async (checked: boolean) => {
            try {
              await setTokenEnabled(row.symbol, checked);
              message.success(`${row.symbol} 已${checked ? '启用' : '停用'}`);
              reload();
            } catch {
              /* interceptor */
            }
          }}
        />
      ),
    },
  ];

  const {
    columns: tokenResizableColumns,
    components: tokenComponents,
    scrollX: tokenScrollX,
  } = useResizableColumns(tokenColumns);

  const keyRows: ChainApiKeyDto[] = [...rpcKeys, ...etherscanKeys];
  const keyColumns = [
    {
      title: '服务商',
      dataIndex: 'provider',
      width: 120,
      render: (v: string) =>
        v === 'RpcBsc' ? (
          <Tag color="purple">Ankr RPC</Tag>
        ) : (
          <Tag color="geekblue">Etherscan V2</Tag>
        ),
    },
    {
      title: '备注',
      dataIndex: 'label',
      width: 140,
      render: (v?: string | null) => v || '-',
    },
    {
      title: 'KEY（掩码）',
      dataIndex: 'maskedKey',
      width: 140,
      render: (v: string) => (
        <Text style={{ fontFamily: 'monospace' }}>{v}</Text>
      ),
    },
    {
      title: '启用',
      dataIndex: 'isEnabled',
      width: 70,
      render: (v: boolean, row: ChainApiKeyDto) => (
        <Switch
          checked={v}
          size="small"
          onChange={(checked: boolean) => doToggleKey(row, checked)}
        />
      ),
    },
    {
      title: '限流次数',
      dataIndex: 'consecutiveFailures',
      width: 90,
      render: (v: number) =>
        v > 0 ? <Tag color="warning">{v}</Tag> : <Tag>0</Tag>,
    },
    {
      title: '最近使用',
      dataIndex: 'lastUsedAt',
      width: 150,
      render: (v?: string | null) => fmtTime(v ?? undefined),
    },
    {
      title: '添加时间',
      dataIndex: 'creationTime',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    {
      title: '操作',
      width: 70,
      render: (_: unknown, row: ChainApiKeyDto) => (
        <Popconfirm
          title={`确定删除 KEY ${row.maskedKey}？`}
          onConfirm={() => doRemoveKey(row)}
          okText="删除"
          cancelText="取消"
        >
          <Button size="small" type="link" danger>
            删除
          </Button>
        </Popconfirm>
      ),
    },
  ];

  const {
    columns: keyResizableColumns,
    components: keyComponents,
    scrollX: keyScrollX,
  } = useResizableColumns(keyColumns);

  return (
    <PageContainer>
      <Space orientation="vertical" style={{ width: '100%' }} size={16}>
        <Alert
          type="info"
          showIcon
          message="链网配置（高危运维）：RPC、助记词、金库、扫链上限全部在此页面管理。扫链进度由 SignalR 实时推送，无需刷新。"
        />
        <div>
          <Tag color={connView.color}>{connView.text}</Tag>
        </div>
        <Card title="网络状态" loading={loading}>
          {status && (
            <Descriptions bordered size="small" column={4}>
              <Descriptions.Item label="网络">
                {status.network}
              </Descriptions.Item>
              <Descriptions.Item label="Chain ID">
                {status.chainId}
              </Descriptions.Item>
              <Descriptions.Item label="链上高度">
                <Text style={{ fontFamily: 'monospace' }}>
                  {fmtBlock(mergedHead ?? undefined)}
                </Text>
              </Descriptions.Item>
              <Descriptions.Item label="扫链心跳">
                {fmtTime(status.scanHeartbeatAt)}
              </Descriptions.Item>
              <Descriptions.Item label="助记词">
                <Space>
                  {status.hasMnemonic ? (
                    <Tag color="green">已配置</Tag>
                  ) : (
                    <Tag color="red">未配置</Tag>
                  )}
                  <Button size="small" danger onClick={doGenerateMnemonic}>
                    生成
                  </Button>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="RPC 地址">
                <Space>
                  <Text style={{ fontFamily: 'monospace', fontSize: 12 }}>
                    {status.rpcUrlMasked ?? '未配置'}
                  </Text>
                  <Button
                    size="small"
                    type="link"
                    onClick={() => setRpcOpen(true)}
                  >
                    配置
                  </Button>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="扫链上限">
                <Space>
                  <Text>{status.nodeLogResultCap ?? '未配置（不开扫链）'}</Text>
                  <Button
                    size="small"
                    type="link"
                    onClick={() => {
                      setCapVal(status.nodeLogResultCap ?? 10000);
                      setCapOpen(true);
                    }}
                  >
                    改
                  </Button>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="归集心跳">
                {fmtTime(status.sweepHeartbeatAt)}
              </Descriptions.Item>
              <Descriptions.Item label="大额门持有">
                {status.heldDepositCount > 0 ? (
                  <Tag color="purple">
                    {status.heldDepositCount} 笔 / {status.heldDepositTotal}
                    （等放行）
                  </Tag>
                ) : (
                  <Tag color="default">无持有</Tag>
                )}
              </Descriptions.Item>
              <Descriptions.Item label="充值费率">
                <Space>
                  <Text>{(status.depositFeeRate * 100).toFixed(2)}%</Text>
                  <Button
                    size="small"
                    type="link"
                    onClick={() => {
                      setPolicyFeeRate(String(status.depositFeeRate));
                      setPolicyMax(status.autoCreditMaxAmount);
                      setPolicyOpen(true);
                    }}
                  >
                    政策
                  </Button>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="大额放行门">
                <Text>{status.autoCreditMaxAmount}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="链上开关">
                <Space size={12}>
                  <Space size={4}>
                    <Text type="secondary">扫链</Text>
                    <Popconfirm
                      title={`确认${status.scanEnabled ? '关闭' : '开启'}扫链？`}
                      description="误开会扫链烧 Gas；worker 下一轮热生效。"
                      onConfirm={() => doToggleSwitch('scanEnabled', !status.scanEnabled)}
                      okText="确认"
                      cancelText="取消"
                    >
                      <Switch checked={status.scanEnabled} size="small" />
                    </Popconfirm>
                  </Space>
                  <Space size={4}>
                    <Text type="secondary">归集</Text>
                    <Popconfirm
                      title={`确认${status.sweepEnabled ? '关闭' : '开启'}归集？`}
                      description="归集会向用户地址打 BNB 并签名转账；worker 下一轮热生效。"
                      onConfirm={() => doToggleSwitch('sweepEnabled', !status.sweepEnabled)}
                      okText="确认"
                      cancelText="取消"
                    >
                      <Switch checked={status.sweepEnabled} size="small" />
                    </Popconfirm>
                  </Space>
                  <Space size={4}>
                    <Text type="secondary">热钱包监控</Text>
                    <Popconfirm
                      title={`确认${status.hotWalletWatchEnabled ? '关闭' : '开启'}热钱包逐笔监控？`}
                      description="只读监控不碰签名面；worker 下一轮热生效。"
                      onConfirm={() =>
                        doToggleSwitch('hotWalletWatchEnabled', !status.hotWalletWatchEnabled)
                      }
                      okText="确认"
                      cancelText="取消"
                    >
                      <Switch checked={status.hotWalletWatchEnabled} size="small" />
                    </Popconfirm>
                  </Space>
                </Space>
              </Descriptions.Item>
            </Descriptions>
          )}
        </Card>
        <Card title="扫链 KEY 池（同服务商多把，限流自动轮换）">
          <Space orientation="vertical" style={{ width: '100%' }} size={12}>
            <Alert
              type="info"
              showIcon
              message={
                <>
                  扫链 RPC 与 Etherscan 各自可挂多把 KEY：网关遇 429/401/403
                  或配额类错误自动切下一把，逐轮从不同 KEY 起步分摊限流。
                  {rpcKeys.length === 0 && rpcFallbackMasked && (
                    <>
                      {' '}
                      当前 RPC 池为空，使用回退地址{' '}
                      <Text code>{rpcFallbackMasked}</Text>（网络状态里的 RPC
                      地址）。
                    </>
                  )}
                  {etherscanKeys.length === 0 && etherscanFallbackPresent && (
                    <> Etherscan 池为空，使用宿主配置端点回退。</>
                  )}
                </>
              }
            />
            <Space wrap>
              <Select
                value={newKeyProvider}
                onChange={(v: string) => setNewKeyProvider(v)}
                style={{ width: 200 }}
                popupMatchSelectWidth={false}
                options={[
                  { value: 'RpcBsc', label: 'Ankr RPC（扫链）' },
                  { value: 'EtherscanV2', label: 'Etherscan V2（兜底）' },
                ]}
              />
              <Input.Password
                placeholder="粘贴 KEY（明文只传不存）"
                value={newKeyValue}
                onChange={(e) => setNewKeyValue(e.target.value)}
                style={{ width: 380, fontFamily: 'monospace' }}
                onPressEnter={submitAddKey}
              />
              <Input
                placeholder="备注（如 ankr-backup）"
                value={newKeyLabel}
                onChange={(e) => setNewKeyLabel(e.target.value)}
                style={{ width: 200 }}
                onPressEnter={submitAddKey}
              />
              <Button
                type="primary"
                loading={newKeySaving}
                onClick={submitAddKey}
              >
                添加
              </Button>
            </Space>
            <Table<ChainApiKeyDto>
              rowKey="id"
              dataSource={keyRows}
              columns={keyResizableColumns}
              components={keyComponents}
              scroll={{ x: keyScrollX }}
              pagination={false}
              size="small"
              locale={{ emptyText: '暂无 KEY（使用回退来源）' }}
            />
          </Space>
        </Card>
        <Card title="代币通道（SignalR 实时推送）">
          <Table<TokenStatusDto>
            rowKey="id"
            dataSource={mergedTokens}
            columns={tokenResizableColumns}
            components={tokenComponents}
            scroll={{ x: tokenScrollX }}
            pagination={false}
            size="small"
          />
        </Card>
        {status &&
          (status.scanBehindAlertedAt ||
            status.sweepStuckAlertedAt ||
            status.gasLowAlertedAt) && (
            <Card title="最近告警" size="small">
              <Space orientation="vertical">
                {status.scanBehindAlertedAt && (
                  <Text type="warning">
                    扫链落后告警：{fmtTime(status.scanBehindAlertedAt)}
                  </Text>
                )}
                {status.sweepStuckAlertedAt && (
                  <Text type="danger">
                    归集卡住告警：{fmtTime(status.sweepStuckAlertedAt)}
                  </Text>
                )}
                {status.gasLowAlertedAt && (
                  <Text type="warning">
                    Gas 不足告警：{fmtTime(status.gasLowAlertedAt)}
                  </Text>
                )}
              </Space>
            </Card>
          )}
      </Space>

      <Modal
        title="配置 RPC 地址"
        open={rpcOpen}
        onOk={submitRpc}
        onCancel={() => setRpcOpen(false)}
        confirmLoading={rpcSaving}
        okText="保存"
        cancelText="取消"
      >
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>
          填写 Ankr BSC RPC 完整地址（含项目密钥）。仅允许
          https。密文入库，不落日志。
        </Paragraph>
        <Input
          placeholder="https://rpc.ankr.com/bsc/你的密钥"
          value={rpcUrl}
          onChange={(e) => setRpcUrl(e.target.value)}
        />
      </Modal>
      <Modal
        title="设置扫链响应上限"
        open={capOpen}
        onOk={submitCap}
        onCancel={() => setCapOpen(false)}
        confirmLoading={capSaving}
        okText="保存"
        cancelText="取消"
      >
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>
          单次 getLogs 响应的条数上限。达到上限八成时游标不推进。
        </Paragraph>
        <InputNumber
          min={100}
          value={capVal}
          onChange={(v) => setCapVal(v ?? 10000)}
          style={{ width: '100%' }}
        />
      </Modal>
      <Modal
        title={`设置金库地址（${vaultSymbol}）`}
        open={vaultOpen}
        onOk={submitVault}
        onCancel={() => setVaultOpen(false)}
        confirmLoading={vaultSaving}
        okText="保存"
        cancelText="取消"
      >
        <Paragraph
          type="secondary"
          style={{ marginBottom: 12, color: '#cf1322' }}
        >
          金库地址是归集转账的目标地址。填错意味着用户资金会不可逆地转到错误地址。
        </Paragraph>
        <Input
          placeholder="0x..."
          value={vaultAddr}
          onChange={(e) => setVaultAddr(e.target.value)}
          style={{ fontFamily: 'monospace' }}
        />
      </Modal>
      <Modal
        title={`设置最低充值额（${minDepSymbol}）`}
        open={minDepOpen}
        onOk={submitMinDep}
        onCancel={() => setMinDepOpen(false)}
        confirmLoading={minDepSaving}
        okText="保存"
        cancelText="取消"
      >
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>
          低于此金额的转入会被跳过。
        </Paragraph>
        <Input
          placeholder="10"
          value={minDepVal}
          onChange={(e) => setMinDepVal(e.target.value)}
        />
      </Modal>
      <Modal
        title="充值政策（费率 + 大额放行门）"
        open={policyOpen}
        onOk={submitPolicy}
        onCancel={() => setPolicyOpen(false)}
        confirmLoading={policySaving}
        okText="保存"
        cancelText="取消"
      >
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>
          费率对充值毛额计（建单时快照，修改只影响新单）；毛额超过放行门的进款单停在
          Confirmed 等管理端放行，0 = 全部等放行。
        </Paragraph>
        <Space direction="vertical" style={{ width: '100%' }} size={12}>
          <div>
            <Text type="secondary">充值手续费率（0-1，如 0.001 = 0.1%）</Text>
            <Input
              placeholder="0.001"
              value={policyFeeRate}
              onChange={(e) => setPolicyFeeRate(e.target.value)}
            />
          </div>
          <div>
            <Text type="secondary">
              大额放行门（≥ 0；超过它的进款单等人工放行）
            </Text>
            <Input
              placeholder="5000"
              value={policyMax}
              onChange={(e) => setPolicyMax(e.target.value)}
            />
          </div>
        </Space>
      </Modal>
      <Modal
        title="助记词（只显示这一次）"
        open={!!mnemonicResult}
        onCancel={() => setMnemonicResult(null)}
        footer={[
          <Button
            key="copy"
            type="primary"
            onClick={() => {
              navigator.clipboard?.writeText(mnemonicResult ?? '');
              message.success('已复制');
            }}
          >
            复制
          </Button>,
          <Button key="ok" danger onClick={() => setMnemonicResult(null)}>
            我已安全保管
          </Button>,
        ]}
        width={600}
      >
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message="请立即抄写并安全保管。关闭后无法再次查看。"
        />
        <Paragraph
          style={{
            fontFamily: 'monospace',
            fontSize: 16,
            wordBreak: 'break-all',
            padding: 16,
            background: '#fafafa',
            borderRadius: 8,
          }}
        >
          {mnemonicResult}
        </Paragraph>
      </Modal>
    </PageContainer>
  );
}
