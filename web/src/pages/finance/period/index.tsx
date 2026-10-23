import { PageContainer, ProCard } from '@ant-design/pro-components';
import { Alert, Descriptions, Spin, Tag } from 'antd';
import React, { useEffect, useState } from 'react';
import { type FinPeriodDto, getCurrentPeriod } from '@/abp/finance';
import { financeHelpHeader } from '@/components/FinanceHelp';
import {
  FinanceEmpty,
  FinanceStateView,
  useFinanceLoad,
} from '@/components/FinanceState';

const PeriodPage: React.FC = () => {
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
    <PageContainer {...financeHelpHeader('A4', '会计期间')}>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="本阶段仅展示当前 Open 期间与过账失败原因；关账/反结账二期不做。"
      />
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
    </PageContainer>
  );
};

export default PeriodPage;
