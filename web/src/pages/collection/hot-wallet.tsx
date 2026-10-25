import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Input,
  Modal,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  onChainConnState,
  onChainHotWalletChanged,
  startChainSignalR,
  type ChainConnState,
} from '@/abp/chainSignalR';
import {
  ackHotWalletTxn,
  getHotWallet,
  getHotWalletTxns,
  getHotWalletWatchStatus,
  getWithdrawStatus,
  HW_DIRECTION_LABELS,
  HW_TXN_STATUS_LABELS,
  type HotWalletTxnDto,
  type HotWalletWatchStatusDto,
  type WithdrawHotWalletStatusDto,
  type WithdrawStatusDto,
} from '@/abp/withdrawal';
import { financeErrorText } from '@/abp/finance';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 热钱包逐笔监控页（/collection/hot-wallet）：
 * 状态卡（热钱包余额/熔断 + 各币监控游标 + 未处置异常红点）+ 流水表
 * （出/入方向、匹配提现单、异常行「确认已知」终态化）；SignalR 新异常实时刷新。
 */
export default function HotWalletWatchPage() {
  const { message } = App.useApp();
  const [data, setData] = useState<HotWalletTxnDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [directionFilter, setDirectionFilter] = useState<string | undefined>();
  const [watch, setWatch] = useState<HotWalletWatchStatusDto>();
  const [hotWallet, setHotWallet] = useState<WithdrawHotWalletStatusDto>();
  const [status, setStatus] = useState<WithdrawStatusDto>();
  const [ackTarget, setAckTarget] = useState<HotWalletTxnDto>();
  const [ackNote, setAckNote] = useState('');
  const [acting, setActing] = useState(false);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getHotWalletTxns({
        skipCount: (page - 1) * pageSize,
        maxResultCount: pageSize,
        status: statusFilter,
        direction: directionFilter,
      });
      setData(result.items);
      setTotal(result.totalCount);
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, [page, statusFilter, directionFilter]);

  const reloadCards = useCallback(async () => {
    try {
      setWatch(await getHotWalletWatchStatus());
    } catch {
      /* interceptor */
    }
    try {
      setHotWallet(await getHotWallet());
    } catch {
      setHotWallet(undefined);
    }
    try {
      setStatus(await getWithdrawStatus());
    } catch {
      /* interceptor */
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);
  useEffect(() => {
    reloadCards();
  }, [reloadCards]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    // 异常突发（一轮可到上百条）合并刷新：3s 尾沿防抖，避免 K 条事件 × K 组请求打满后端/RPC
    let timer: ReturnType<typeof setTimeout> | null = null;
    const offHotWallet = onChainHotWalletChanged(() => {
      if (timer != null) {
        clearTimeout(timer);
      }
      timer = setTimeout(() => {
        timer = null;
        reload();
        reloadCards();
      }, 3000);
    });
    startChainSignalR();
    return () => {
      if (timer != null) {
        clearTimeout(timer);
      }
      offConn();
      offHotWallet();
    };
  }, [reload, reloadCards]);

  const doAck = async () => {
    if (!ackTarget) return;
    setActing(true);
    try {
      await ackHotWalletTxn(ackTarget.id, ackNote.trim() || undefined);
      message.success('已确认（终态留痕）');
      setAckTarget(undefined);
      setAckNote('');
      await reload();
      await reloadCards();
    } catch (e) {
      message.error(financeErrorText(e));
    } finally {
      setActing(false);
    }
  };

  const fmtTime = (v?: string) => {
    if (!v) return '-';
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };
  const connView =
    connState === 'connected'
      ? { color: 'success' as const, text: '实时推送：已连接' }
      : connState === 'connecting'
        ? { color: 'warning' as const, text: '连接中…' }
        : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    { title: '代币', dataIndex: 'symbol', width: 70 },
    {
      title: '方向',
      dataIndex: 'direction',
      width: 60,
      render: (v: string) => {
        const cfg = HW_DIRECTION_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
    },
    {
      title: '对手方',
      dataIndex: 'counterparty',
      width: 150,
      ellipsis: true,
      render: (_: unknown, row: HotWalletTxnDto) => {
        const addr = row.direction === 'Out' ? row.to : row.from;
        return (
          <Tooltip title={addr}>
            <Text copyable={{ text: addr }} style={{ fontFamily: 'monospace', fontSize: 11 }}>
              {`${addr.slice(0, 10)}...${addr.slice(-6)}`}
            </Text>
          </Tooltip>
        );
      },
    },
    {
      title: '金额',
      dataIndex: 'amount',
      width: 120,
      align: 'right' as const,
      render: (v: string | undefined, row: HotWalletTxnDto) =>
        v ?? (
          <Tooltip title={`RawAmount: ${row.rawAmount}`}>
            <Text type="warning">超精度</Text>
          </Tooltip>
        ),
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 110,
      render: (v: string) => {
        const cfg = HW_TXN_STATUS_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
    },
    {
      title: '匹配提现单',
      dataIndex: 'matchedWithdrawId',
      width: 130,
      render: (v: string | undefined) =>
        v ? (
          <Tooltip title={v}>
            <Text style={{ fontFamily: 'monospace', fontSize: 11 }}>{`${v.slice(0, 8)}...`}</Text>
          </Tooltip>
        ) : (
          '-'
        ),
    },
    {
      title: '交易哈希',
      dataIndex: 'txHash',
      width: 160,
      ellipsis: true,
      render: (v: string) => (
        <Text copyable={{ text: v }} style={{ fontFamily: 'monospace', fontSize: 11 }}>
          {`${v.slice(0, 12)}...${v.slice(-6)}`}
        </Text>
      ),
    },
    { title: '区块', dataIndex: 'blockNumber', width: 90 },
    {
      title: '确认留痕',
      dataIndex: 'ackedBy',
      width: 160,
      ellipsis: true,
      render: (v: string | undefined, row: HotWalletTxnDto) =>
        v ? (
          <Tooltip title={`${row.ackNote ?? ''}（${fmtTime(row.ackedAt)}）`}>
            {`${v} · ${fmtTime(row.ackedAt)}`}
          </Tooltip>
        ) : (
          '-'
        ),
    },
    {
      title: '操作',
      width: 100,
      fixed: 'right' as const,
      render: (_: unknown, row: HotWalletTxnDto) =>
        row.status === 'Anomaly' ? (
          <Button
            size="small"
            type="primary"
            danger
            onClick={() => {
              setAckTarget(row);
              setAckNote('');
            }}
          >
            确认已知
          </Button>
        ) : (
          <Text type="secondary">—</Text>
        ),
    },
  ];

  const {
    columns: resizableColumns,
    components,
    scrollX,
  } = useResizableColumns(columns);

  return (
    <PageContainer>
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        <Alert
          type="info"
          showIcon
          message="热钱包逐笔监控：出站流水逐笔对账提现单哈希；未匹配出账即异常（高危——私钥泄漏/误操作），确认前请先链上核实。入站为外部补币，提示性展示。"
        />
        <Card
          title="监控状态"
          size="small"
          extra={<Tag color={connView.color}>{connView.text}</Tag>}
        >
          {watch ? (
            <Space size={32} wrap>
              {watch.anomalyOpenCount > 0 ? (
                <Tag color="error">未处置异常出账 {watch.anomalyOpenCount} 笔</Tag>
              ) : (
                <Tag color="success">无未处置异常</Tag>
              )}
              {watch.tokens.map((t) => (
                <Text key={t.symbol}>
                  {t.symbol} 游标{' '}
                  <Text style={{ fontFamily: 'monospace' }}>
                    {t.lastScannedBlock?.toLocaleString() ?? '未首跑'}
                  </Text>
                  {t.lastError ? (
                    <Tag color="warning" style={{ marginLeft: 8 }}>
                      上轮失败
                    </Tag>
                  ) : null}{' '}
                  · {fmtTime(t.lastScanAt)}
                </Text>
              ))}
            </Space>
          ) : (
            <Spin size="small" />
          )}
          <div style={{ marginTop: 8 }}>
            {hotWallet?.configured ? (
              <Space size={24} wrap>
                <Text>
                  热钱包{' '}
                  <Text copyable style={{ fontFamily: 'monospace', fontSize: 11 }}>
                    {hotWallet.address}
                  </Text>
                </Text>
                {Object.entries(hotWallet.tokenBalances).map(([sym, bal]) => (
                  <Text key={sym}>
                    {sym} {bal}
                  </Text>
                ))}
                <Text>BNB {hotWallet.bnb}</Text>
                {hotWallet.breakerOpen ? (
                  <Tag color="error">熔断中</Tag>
                ) : (
                  <Tag color="success">偿付正常</Tag>
                )}
                {status && <Text>待审 {status.pendingCount} · 待广播 {status.processingCount}</Text>}
              </Space>
            ) : (
              <Tag color="warning">热钱包未配置（监控无对象）</Tag>
            )}
          </div>
        </Card>

        <Card
          title="热钱包流水"
          extra={
            <Space>
              <Select
                placeholder="状态"
                allowClear
                style={{ width: 130 }}
                value={statusFilter}
                onChange={(v) => {
                  setStatusFilter(v);
                  setPage(1);
                }}
                options={Object.entries(HW_TXN_STATUS_LABELS).map(([value, cfg]) => ({
                  value,
                  label: cfg.text,
                }))}
              />
              <Select
                placeholder="方向"
                allowClear
                style={{ width: 90 }}
                value={directionFilter}
                onChange={(v) => {
                  setDirectionFilter(v);
                  setPage(1);
                }}
                options={Object.entries(HW_DIRECTION_LABELS).map(([value, cfg]) => ({
                  value,
                  label: cfg.text,
                }))}
              />
            </Space>
          }
        >
          <Table<HotWalletTxnDto>
            rowKey={(r) => `${r.txHash}-${r.logIndex}`}
            loading={loading}
            dataSource={data}
            columns={resizableColumns}
            components={components}
            scroll={{ x: scrollX }}
            size="small"
            pagination={{
              current: page,
              pageSize,
              total,
              showSizeChanger: false,
              onChange: (p) => setPage(p),
            }}
          />
        </Card>
      </Space>

      <Modal
        title={`确认已知（${ackTarget?.txHash.slice(0, 10)}...）`}
        open={!!ackTarget}
        confirmLoading={acting}
        okText="确认"
        cancelText="取消"
        onCancel={() => setAckTarget(undefined)}
        onOk={doAck}
      >
        <Descriptions column={1} size="small" style={{ marginBottom: 12 }}>
          <Descriptions.Item label="金额">
            {ackTarget?.amount ?? ackTarget?.rawAmount} {ackTarget?.symbol}
          </Descriptions.Item>
          <Descriptions.Item label="对手方">
            <Text style={{ fontFamily: 'monospace', fontSize: 11 }}>
              {ackTarget ? (ackTarget.direction === 'Out' ? ackTarget.to : ackTarget.from) : ''}
            </Text>
          </Descriptions.Item>
        </Descriptions>
        <Input.TextArea
          value={ackNote}
          onChange={(e) => setAckNote(e.target.value)}
          maxLength={500}
          showCount
          placeholder="核实备注（如：运营手动转出至冷钱包）"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          确认后终态留痕（操作人/时间/备注）；请先在区块链浏览器核实该笔转账的来龙去脉。
        </Text>
      </Modal>
    </PageContainer>
  );
}
