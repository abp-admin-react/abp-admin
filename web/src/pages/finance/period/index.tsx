import { ProCard } from '@ant-design/pro-components';
import { Descriptions, Spin, Tag } from 'antd';
import { useEffect, useState } from 'react';
import { type FinPeriodDto, getCurrentPeriod } from '@/abp/finance';
import {
  FinanceEmpty,
  FinanceStateView,
  useFinanceLoad,
} from '@/components/FinanceState';

export function FinancePeriod() {
  const [data, setData] = useState<FinPeriodDto>();
  const [loading, setLoading] = useState(true);

  const load = useFinanceLoad();

  useEffect(() => {
    setLoading(true);
    load
      .guard(() => getCurrentPeriod())
      .then((d) => setData(d ?? undefined))
      .finally(() => setLoading(false));
  }, [load.nonce]);

  return (
    <>
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        <Spin spinning={loading}>
          {!loading && !data ? (
            <FinanceEmpty text="暂无期间" />
          ) : (
            <ProCard title="当前会计期间">
              <Descriptions column={2} size="small">
                <Descriptions.Item label="期间">
                  {data?.label || '-'}
                </Descriptions.Item>
                <Descriptions.Item label="状态">
                  {data ? (
                    <Tag
                      color={data.status === 'Open' ? 'processing' : 'default'}
                    >
                      {data.status === 'Open' ? '开账中' : '已关账'}
                    </Tag>
                  ) : (
                    '-'
                  )}
                </Descriptions.Item>
                <Descriptions.Item label="最近过账失败" span={2}>
                  {data?.lastPostFailure || '无'}
                </Descriptions.Item>
              </Descriptions>
            </ProCard>
          )}
        </Spin>
      </FinanceStateView>
    </>
  );
}
