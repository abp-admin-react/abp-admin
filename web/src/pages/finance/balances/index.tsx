import FinanceHelpTitle from '@/components/FinanceHelp';
import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { Button, Select, Space, Tag } from 'antd';
import { formatForDisplay, rangeToUtcBounds } from '@/utils/time';
import React, { useRef, useState } from 'react';
import { history, useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  type FinBalanceProjectionDto,
  getBalanceProjections,
} from '@/abp/finance';

const BalancesPage: React.FC = () => {
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  const [agentId, setAgentId] = useState<string | undefined>();
  const isAgentScoped = !!access.canFinanceScopeAgent;

  return (
    <PageContainer
      title={<FinanceHelpTitle page="A7" title="A7 用户余额投影" />}
      extra={
        !load.ok ? null : (
        !isAgentScoped ? (
          <Space>
            <Select
              allowClear
              placeholder="按代理筛选"
              style={{ width: 160 }}
              value={agentId}
              onChange={(v) => {
                setAgentId(v);
                actionRef.current?.reload();
              }}
              options={[
                { value: 'ag-1', label: '代理 ag-1' },
                { value: 'ag-2', label: '代理 ag-2' },
              ]}
            />
          </Space>
        ) : null
        )
      }
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <AutoHeightProTable<FinBalanceProjectionDto>
        key={load.nonce}
        rowKey="id"
        locale={{ emptyText: <FinanceEmpty text={'暂无余额数据'} /> }}
        actionRef={actionRef}
        search={{ labelWidth: 'auto' }}
        params={{ agentId }}
        request={async (params) => {
          const maxResultCount = params.pageSize ?? 20;
          const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
          const result = await load.guard(() =>
            getBalanceProjections({
            skipCount,
            maxResultCount,
            filter: params.ownerName || params.ownerId,
            agentId: isAgentScoped ? undefined : agentId,
          }),
          );
          if (!result) return { data: [], total: 0, success: false };
          return { data: result.items, total: result.totalCount, success: true };
        }}
        columns={[
          { title: '用户', dataIndex: 'ownerName', width: 120 },
          { title: '用户Id', dataIndex: 'ownerId', width: 120, ellipsis: true },
          { title: '币种', dataIndex: 'currency', width: 80, search: false },
          { title: '可用', dataIndex: 'available', width: 90, search: false },
          { title: '冻结', dataIndex: 'frozen', width: 90, search: false },
          { title: '预收', dataIndex: 'prepayment', width: 90, search: false },
          {
            title: '账本推导',
            dataIndex: 'ledgerDerived',
            width: 100,
            search: false,
          },
          {
            title: '差异',
            dataIndex: 'diff',
            width: 90,
            search: false,
            render: (_, r) =>
              r.diff === 0 ? (
                <Tag color="success">0</Tag>
              ) : (
                <Tag
                  color="error"
                  style={{ cursor: 'pointer' }}
                  onClick={() => history.push('/finance/reconcile')}
                >
                  {r.diff}
                </Tag>
              ),
          },
          {
            title: '代理',
            dataIndex: 'agentId',
            width: 100,
            search: false,
            hideInTable: isAgentScoped,
          },
          {
            title: '同步时间',
            dataIndex: 'syncedAt',
            search: false,
            render: (_: unknown, r: any) => formatForDisplay(r.syncedAt),
            width: 170,
          },
          {
            title: '操作',
            valueType: 'option',
            width: 100,
            render: (_, r) =>
              r.diff !== 0
                ? [
                    <Button
                      key="r"
                      type="link"
                      size="small"
                      onClick={() => history.push('/finance/reconcile')}
                    >
                      去对账
                    </Button>,
                  ]
                : [],
          },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default BalancesPage;
