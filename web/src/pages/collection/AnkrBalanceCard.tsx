import { Alert, Button, Card, Space, Statistic, Typography } from 'antd';
import { type ReactNode, useEffect, useState } from 'react';
import { type AnkrBalanceDto, getAnkrBalance } from '@/abp/chainAdmin';

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
  networkId,
  savedGroup,
  unconfiguredAction,
}: {
  networkId?: string;
  savedGroup?: string | null;
  unconfiguredAction?: ReactNode;
}) {
  const [balance, setBalance] = useState<AnkrBalanceDto | null>();
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    const request = networkId ? getAnkrBalance(networkId) : getAnkrBalance();
    request.then(
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
  }, [networkId, revision]);

  const loading = balance === undefined;
  const title = savedGroup === undefined ? 'Ankr 账户余额' : '已保存账户的余额';
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
        {loading ? (
          <Typography.Text type="secondary">正在查询余额…</Typography.Text>
        ) : balance?.status === 'available' ? (
          <Space orientation="vertical" size={12}>
            {savedGroup !== undefined && (
              <Typography.Text type="secondary">
                {savedGroup ? `团队账户 · ${savedGroup}` : '个人账户'}
              </Typography.Text>
            )}
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
