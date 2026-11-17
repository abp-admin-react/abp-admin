import { Alert, Button, Card, Space, Statistic, Typography } from 'antd';
import { type ReactNode, useEffect, useState } from 'react';
import type { AnkrBalanceDto } from '@/abp/chainAdmin';
import { getDashboardAnkrBalance } from '@/abp/scanProviders';

export function AnkrBalanceAmounts({
  credits,
  balanceUsd,
}: Pick<AnkrBalanceDto, 'credits' | 'balanceUsd'>) {
  return (
    <Space size={32} wrap>
      <Statistic title="剩余 Credits" value={credits ?? '—'} />
      <Statistic
        title="美元余额（USD）"
        value={balanceUsd ?? '—'}
        prefix={balanceUsd === null ? undefined : '$'}
      />
    </Space>
  );
}

export default function AnkrBalanceCard({
  unconfiguredAction,
}: {
  unconfiguredAction?: ReactNode;
}) {
  const [balance, setBalance] = useState<
    (AnkrBalanceDto & { label: string | null }) | null
  >();
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    getDashboardAnkrBalance().then(
      (result) => {
        if (active) setBalance(result);
      },
      () => {
        if (active) setBalance(null);
      },
    );
    return () => {
      active = false;
    };
  }, [revision]);

  const loading = balance === undefined;
  const title = 'Ankr 账户余额';
  return (
    <Card
      title={title}
      role="region"
      aria-label={title}
      aria-busy={loading}
      extra={
        <Button
          disabled={loading}
          size="small"
          onClick={() => {
            setBalance(undefined);
            setRevision((value) => value + 1);
          }}
        >
          刷新余额
        </Button>
      }
    >
      <div role="status">
        {balance?.label && (
          <Typography.Paragraph type="secondary">
            {balance.label}
          </Typography.Paragraph>
        )}
        {loading ? (
          <Typography.Text type="secondary">正在查询余额…</Typography.Text>
        ) : balance?.status === 'available' ? (
          <Space orientation="vertical" size={12}>
            <AnkrBalanceAmounts
              credits={balance.credits}
              balanceUsd={balance.balanceUsd}
            />
            {balance.checkedAt && (
              <Typography.Text type="secondary">
                查询于 {new Date(balance.checkedAt).toLocaleString()}
              </Typography.Text>
            )}
          </Space>
        ) : balance?.status === 'notConfigured' ? (
          <Space wrap>
            <Typography.Text type="secondary">
              未配置 Ankr Platform Auth Key
            </Typography.Text>
            {unconfiguredAction}
          </Space>
        ) : (
          <Alert type="warning" showIcon title="暂时无法查询 Ankr 账户余额" />
        )}
      </div>
    </Card>
  );
}
