import { AgGridReact } from 'ag-grid-react';
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Select,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd';
import { useEffect, useState } from 'react';
import {
  type CollectionNetworkSummaryDto,
  getNetworkStatus,
  getNetworks,
  type NetworkStatusDto,
  type TokenStatusDto,
} from '@/abp/chainAdmin';
import { toGridColumns, useSearchableLocalGrid } from '@/components/remoteGrid';

const formatBlock = (value?: number | null) => value?.toLocaleString() ?? '—';
const formatTime = (value?: string | null) =>
  value ? new Date(value).toLocaleString() : '—';

const scanFailureLabels: Record<string, string> = {
  invalid: '凭据无效',
  planRequired: '套餐未覆盖',
  quota: '额度不足或限流',
  timeout: '查询超时',
  unavailable: '服务暂不可用',
  rangeIncomplete: '区间数据未读全',
  evidenceInvalid: '链上数据校验失败',
  invalidConfiguration: '配置无效',
  notConfigured: '没有适用的服务商',
  stale: '服务商数据滞后',
};

function NetworkScanDetails({ networkId }: { networkId: string }) {
  const [status, setStatus] = useState<NetworkStatusDto>();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    let refresh: number | undefined;

    const load = async () => {
      try {
        const data = await getNetworkStatus(networkId);
        if (!active) return;
        setStatus(data);
        setFailed(false);
      } catch {
        if (active) setFailed(true);
      } finally {
        if (active) refresh = window.setTimeout(load, 30_000);
      }
    };
    void load();
    return () => {
      active = false;
      window.clearTimeout(refresh);
    };
  }, [networkId]);

  const detailGrid1 = useSearchableLocalGrid<TokenStatusDto>({
    ariaLabel: '代币状态',
    rowData: status?.tokens,
    keywordFields: ['symbol', 'lastScanProvider', 'lastScanFailure'],
    pagination: false,
    getRowId: ({ data }) => String(data.id),
    columnDefs: toGridColumns<TokenStatusDto>(
      [
        { title: '代币', dataIndex: 'symbol' },
        {
          title: '状态',
          dataIndex: 'isEnabled',
          render: (enabled: boolean) => (
            <Tag color={enabled ? 'success' : 'default'}>
              {enabled ? '已启用' : '已停用'}
            </Tag>
          ),
        },
        {
          title: '已扫描至区块',
          dataIndex: 'lastScannedBlock',
          render: formatBlock,
        },
        { title: '落后块数', dataIndex: 'blocksBehind', render: formatBlock },
        { title: '最近扫描', dataIndex: 'lastScanAt', render: formatTime },
        {
          title: '最近成功来源',
          dataIndex: 'lastScanProvider',
          render: (_, row) =>
            row.lastScanProvider ? (
              <Space orientation="vertical" size={4}>
                {row.lastScanProvider}
                <Typography.Text type="secondary">
                  {formatTime(row.lastScanProviderAt)}
                </Typography.Text>
              </Space>
            ) : (
              '—'
            ),
        },
        {
          title: '扫链异常',
          dataIndex: 'lastScanFailure',
          render: (_, row) =>
            row.lastScanFailure || row.scanReplayFromBlock != null ? (
              <Space orientation="vertical" size={4}>
                {row.lastScanFailure && (
                  <Tag color="warning">
                    {scanFailureLabels[row.lastScanFailure] ?? '查询失败'}
                  </Tag>
                )}
                {row.scanReplayFromBlock != null && (
                  <Typography.Text type="secondary">
                    待从区块 {formatBlock(row.scanReplayFromBlock)} 复扫
                  </Typography.Text>
                )}
              </Space>
            ) : (
              '—'
            ),
        },
      ],
      {
        kinds: {
          isEnabled: 'boolean',
          blocksBehind: 'number',
          lastScannedBlock: 'number',
          lastScanAt: 'date',
        },
      },
    ),
  });
  if (failed && !status)
    return (
      <Alert
        type="error"
        title="加载扫链状态失败，30 秒后自动重试。"
        showIcon
      />
    );
  if (!status) return <Spin aria-label="加载扫链状态" />;

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      {failed && (
        <Alert
          type="warning"
          title="刷新扫链状态失败，正在显示上次读取的数据。"
          showIcon
        />
      )}
      <Descriptions
        size="small"
        column={{ xs: 1, sm: 2, lg: 3 }}
        items={[
          {
            key: 'head',
            label: '链上最新区块',
            children: formatBlock(status.latestChainHead),
          },
          {
            key: 'scan',
            label: '最近扫链',
            children: formatTime(status.scanHeartbeatAt),
          },
          {
            key: 'sweep',
            label: '最近归集检查',
            children: formatTime(status.sweepHeartbeatAt),
          },
        ]}
      />
      {detailGrid1.searchBox}
      <AgGridReact<TokenStatusDto> {...detailGrid1.gridProps} />
    </Space>
  );
}

export default function ScanStatus() {
  const [networks, setNetworks] = useState<CollectionNetworkSummaryDto[]>();
  const [networkId, setNetworkId] = useState<string>();
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const current = networks?.find(({ id }) => networkId === id) ?? networks?.[0];

  useEffect(() => {
    let active = true;
    setFailed(false);
    getNetworks()
      .then((items) => {
        if (active) setNetworks(items);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [revision]);

  return (
    <Card
      title="网络扫描进度"
      extra={
        current && (
          <Select
            aria-label="选择链网络"
            value={current.id}
            options={networks?.map(({ id, name }) => ({
              value: id,
              label: name,
            }))}
            onChange={setNetworkId}
          />
        )
      }
    >
      {failed ? (
        <Alert
          type="error"
          title="加载网络失败"
          action={
            <Button onClick={() => setRevision((n) => n + 1)}>重试</Button>
          }
          showIcon
        />
      ) : current ? (
        <NetworkScanDetails key={current.id} networkId={current.id} />
      ) : networks ? (
        <Empty description="暂无链网络" />
      ) : (
        <Spin aria-label="加载网络" />
      )}
    </Card>
  );
}
