import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Select,
  Space,
  Spin,
  Table,
  Tag,
} from 'antd';
import { useEffect, useState } from 'react';
import {
  type CollectionNetworkSummaryDto,
  getNetworks,
  getNetworkStatus,
  type NetworkStatusDto,
  type TokenStatusDto,
} from '@/abp/chainAdmin';

const formatBlock = (value?: number) => value?.toLocaleString() ?? '—';
const formatTime = (value?: string) =>
  value ? new Date(value).toLocaleString() : '—';

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
      <Table<TokenStatusDto>
        size="small"
        pagination={false}
        rowKey="id"
        dataSource={status.tokens}
        scroll={{ x: 600 }}
        columns={[
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
        ]}
      />
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
