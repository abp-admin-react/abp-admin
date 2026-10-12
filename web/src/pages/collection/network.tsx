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
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  NetworkStatusDto,
  TokenStatusDto,
  getNetworkStatus,
  generateMnemonic,
  setMinDeposit,
  setNodeLogResultCap,
  setRpcUrl,
  setTokenEnabled,
  setVaultAddress,
} from '@/abp/chainAdmin';
import { startChainSignalR, onChainScanProgress, onChainConnState, ChainScanProgressPayload, ChainConnState } from '@/abp/chainSignalR';

const { Text, Paragraph } = Typography;

/**
 * 链网配置页（/collection/network）。扫链进度由 SignalR 实时推送（ChainScanProgress），无需刷新按钮。
 */
export default function NetworkConfigPage() {
  const { message, modal } = App.useApp();
  const [status, setStatus] = useState<NetworkStatusDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const [progressMap, setProgressMap] = useState<Map<string, ChainScanProgressPayload>>(new Map());

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
  const [mnemonicResult, setMnemonicResult] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try { setStatus(await getNetworkStatus()); } catch { /* interceptor */ } finally { setLoading(false); }
  }, []);

  useEffect(() => { reload(); }, [reload]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offProgress = onChainScanProgress((p) => {
      setProgressMap((prev) => { const next = new Map(prev); next.set(p.symbol, p); return next; });
    });
    startChainSignalR();
    return () => { offConn(); offProgress(); };
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
  const mergedHead = [...progressMap.values()][0]?.latestChainHead ?? status?.latestChainHead;

  const submitRpc = async () => {
    if (!rpcUrl.startsWith('https://')) { message.warning('RPC 地址必须以 https:// 开头'); return; }
    setRpcSaving(true);
    try { await setRpcUrl(rpcUrl); message.success('RPC 已配置'); setRpcOpen(false); setRpcUrl(''); reload(); } finally { setRpcSaving(false); }
  };
  const submitCap = async () => {
    if (capVal < 100) { message.warning('上限必须不小于 100'); return; }
    setCapSaving(true);
    try { await setNodeLogResultCap(capVal); message.success('测量上限已更新'); setCapOpen(false); reload(); } finally { setCapSaving(false); }
  };
  const submitVault = async () => {
    if (!/^0x[0-9a-fA-F]{40}$/.test(vaultAddr)) { message.warning('金库地址需 0x 开头的 40 位十六进制'); return; }
    setVaultSaving(true);
    try { await setVaultAddress(vaultSymbol, vaultAddr); message.success('金库地址已更新'); setVaultOpen(false); setVaultAddr(''); reload(); } finally { setVaultSaving(false); }
  };
  const submitMinDep = async () => {
    const v = parseFloat(minDepVal);
    if (isNaN(v) || v <= 0) { message.warning('最低充值额必须为正数'); return; }
    setMinDepSaving(true);
    try { await setMinDeposit(minDepSymbol, minDepVal); message.success('最低充值额已更新'); setMinDepOpen(false); reload(); } finally { setMinDepSaving(false); }
  };
  const doGenerateMnemonic = () => {
    modal.confirm({
      title: '生成助记词',
      content: '将生成新的 24 词助记词。如果已有充值地址或未完成归集，会被拒绝。助记词只显示一次，请立即抄写并安全保管。',
      okText: '生成', cancelText: '取消',
      onOk: async () => { try { setMnemonicResult(await generateMnemonic()); } catch { /* interceptor */ } },
    });
  };

  const fmtBlock = (v?: number) => (v == null ? '-' : Number(v).toLocaleString());
  const fmtTime = (v?: string) => { if (!v) return '-'; const d = new Date(v); return isNaN(d.getTime()) ? v : d.toLocaleString(); };
  const connView = connState === 'connected' ? { color: 'success' as const, text: '实时推送：已连接' }
    : connState === 'connecting' ? { color: 'warning' as const, text: '实时推送：连接中…连上后自动更新' }
    : { color: 'error' as const, text: '实时推送：已断开，自动重连中' };

  const tokenColumns = [
    { title: '代币', dataIndex: 'symbol', width: 80 },
    { title: '合约', dataIndex: 'contractAddress', width: 140, ellipsis: true,
      render: (v: string) => <Text copyable={{ text: v }} style={{ fontSize: 12 }}>{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text> },
    { title: '最低充值', dataIndex: 'minDeposit', width: 100,
      render: (v: string, row: TokenStatusDto) => (
        <Space size={4}><Text>{v}</Text><Button size="small" type="link" onClick={() => { setMinDepSymbol(row.symbol); setMinDepVal(row.minDeposit); setMinDepOpen(true); }}>改</Button></Space>
      ) },
    { title: '金库地址', dataIndex: 'vaultAddress', width: 140, ellipsis: true,
      render: (v: string | null | undefined, row: TokenStatusDto) => (
        <Space size={4}>{v ? <Text copyable={{ text: v }} style={{ fontSize: 12 }}>{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text> : <Tag color="warning">未配置</Tag>}<Button size="small" type="link" onClick={() => { setVaultSymbol(row.symbol); setVaultAddr(row.vaultAddress ?? ''); setVaultOpen(true); }}>改</Button></Space>
      ) },
    { title: '扫链游标', dataIndex: 'lastScannedBlock', width: 120,
      render: (v?: number | null) => <Text style={{ fontFamily: 'monospace' }}>{fmtBlock(v ?? undefined)}</Text> },
    { title: '落后', dataIndex: 'blocksBehind', width: 80,
      render: (v?: number | null) => v != null ? <span style={{ color: v > 5000 ? '#cf1322' : v > 1000 ? '#fa8c16' : '#389e0d' }}>{fmtBlock(v)}</span> : '-' },
    { title: '最近扫链', dataIndex: 'lastScanAt', width: 150, render: (v?: string | null) => fmtTime(v ?? undefined) },
    { title: '启用', dataIndex: 'isEnabled', width: 70,
      render: (v: boolean, row: TokenStatusDto) => (
        <Switch checked={v} size="small" onChange={async (checked: boolean) => {
          try { await setTokenEnabled(row.symbol, checked); message.success(`${row.symbol} 已${checked ? '启用' : '停用'}`); reload(); } catch { /* interceptor */ }
        }} />
      ) },
  ];

  return (
    <PageContainer>
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        <Alert type="info" showIcon message="链网配置（高危运维）：RPC、助记词、金库、扫链上限全部在此页面管理。扫链进度由 SignalR 实时推送，无需刷新。" />
        <div><Tag color={connView.color}>{connView.text}</Tag></div>
        <Card title="网络状态" loading={loading}>
          {status && (
            <Descriptions bordered size="small" column={4}>
              <Descriptions.Item label="网络">{status.network}</Descriptions.Item>
              <Descriptions.Item label="Chain ID">{status.chainId}</Descriptions.Item>
              <Descriptions.Item label="链上高度"><Text style={{ fontFamily: 'monospace' }}>{fmtBlock(mergedHead ?? undefined)}</Text></Descriptions.Item>
              <Descriptions.Item label="扫链心跳">{fmtTime(status.scanHeartbeatAt)}</Descriptions.Item>
              <Descriptions.Item label="助记词">
                <Space>{status.hasMnemonic ? <Tag color="green">已配置</Tag> : <Tag color="red">未配置</Tag>}<Button size="small" danger onClick={doGenerateMnemonic}>生成</Button></Space>
              </Descriptions.Item>
              <Descriptions.Item label="RPC 地址">
                <Space><Text style={{ fontFamily: 'monospace', fontSize: 12 }}>{status.rpcUrlMasked ?? '未配置'}</Text><Button size="small" type="link" onClick={() => setRpcOpen(true)}>配置</Button></Space>
              </Descriptions.Item>
              <Descriptions.Item label="扫链上限">
                <Space><Text>{status.nodeLogResultCap ?? '未配置（不开扫链）'}</Text><Button size="small" type="link" onClick={() => { setCapVal(status.nodeLogResultCap ?? 10000); setCapOpen(true); }}>改</Button></Space>
              </Descriptions.Item>
              <Descriptions.Item label="归集心跳">{fmtTime(status.sweepHeartbeatAt)}</Descriptions.Item>
            </Descriptions>
          )}
        </Card>
        <Card title="代币通道（SignalR 实时推送）">
          <Table<TokenStatusDto> rowKey="id" dataSource={mergedTokens} columns={tokenColumns} pagination={false} size="small" />
        </Card>
        {status && (status.scanBehindAlertedAt || status.sweepStuckAlertedAt || status.gasLowAlertedAt) && (
          <Card title="最近告警" size="small">
            <Space direction="vertical">
              {status.scanBehindAlertedAt && <Text type="warning">扫链落后告警：{fmtTime(status.scanBehindAlertedAt)}</Text>}
              {status.sweepStuckAlertedAt && <Text type="danger">归集卡住告警：{fmtTime(status.sweepStuckAlertedAt)}</Text>}
              {status.gasLowAlertedAt && <Text type="warning">Gas 不足告警：{fmtTime(status.gasLowAlertedAt)}</Text>}
            </Space>
          </Card>
        )}
      </Space>

      <Modal title="配置 RPC 地址" open={rpcOpen} onOk={submitRpc} onCancel={() => setRpcOpen(false)} confirmLoading={rpcSaving} okText="保存" cancelText="取消">
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>填写 Ankr BSC RPC 完整地址（含项目密钥）。仅允许 https。密文入库，不落日志。</Paragraph>
        <Input placeholder="https://rpc.ankr.com/bsc/你的密钥" value={rpcUrl} onChange={(e) => setRpcUrl(e.target.value)} />
      </Modal>
      <Modal title="设置扫链响应上限" open={capOpen} onOk={submitCap} onCancel={() => setCapOpen(false)} confirmLoading={capSaving} okText="保存" cancelText="取消">
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>单次 getLogs 响应的条数上限。达到上限八成时游标不推进。</Paragraph>
        <InputNumber min={100} value={capVal} onChange={(v) => setCapVal(v ?? 10000)} style={{ width: '100%' }} />
      </Modal>
      <Modal title={`设置金库地址（${vaultSymbol}）`} open={vaultOpen} onOk={submitVault} onCancel={() => setVaultOpen(false)} confirmLoading={vaultSaving} okText="保存" cancelText="取消">
        <Paragraph type="secondary" style={{ marginBottom: 12, color: '#cf1322' }}>金库地址是归集转账的目标地址。填错意味着用户资金会不可逆地转到错误地址。</Paragraph>
        <Input placeholder="0x..." value={vaultAddr} onChange={(e) => setVaultAddr(e.target.value)} style={{ fontFamily: 'monospace' }} />
      </Modal>
      <Modal title={`设置最低充值额（${minDepSymbol}）`} open={minDepOpen} onOk={submitMinDep} onCancel={() => setMinDepOpen(false)} confirmLoading={minDepSaving} okText="保存" cancelText="取消">
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>低于此金额的转入会被跳过。</Paragraph>
        <Input placeholder="10" value={minDepVal} onChange={(e) => setMinDepVal(e.target.value)} />
      </Modal>
      <Modal
        title="助记词（只显示这一次）" open={!!mnemonicResult} onCancel={() => setMnemonicResult(null)}
        footer={[
          <Button key="copy" type="primary" onClick={() => { navigator.clipboard?.writeText(mnemonicResult ?? ''); message.success('已复制'); }}>复制</Button>,
          <Button key="ok" danger onClick={() => setMnemonicResult(null)}>我已安全保管</Button>,
        ]} width={600}
      >
        <Alert type="warning" showIcon style={{ marginBottom: 16 }} message="请立即抄写并安全保管。关闭后无法再次查看。" />
        <Paragraph style={{ fontFamily: 'monospace', fontSize: 16, wordBreak: 'break-all', padding: 16, background: '#fafafa', borderRadius: 8 }}>{mnemonicResult}</Paragraph>
      </Modal>
    </PageContainer>
  );
}
