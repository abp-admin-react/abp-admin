import { financeHelpHeader } from '@/components/FinanceHelp';
import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { Select, Space, Tag } from 'antd';
import { formatForDisplay } from '@/utils/time';
import React, { useRef, useState } from 'react';
import { Link, useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  type FinBalanceProjectionDto,
  getBalanceProjections,
  isMock,
} from '@/abp/finance';

const BalancesPage: React.FC = () => {
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  const [agentId, setAgentId] = useState<string | undefined>();
  const isAgentScoped = !!access.canFinanceScopeAgent;
  // 普通同步谓词（非 hook），渲染期读 localStorage、非响应式；live 下代理筛选整体隐藏（空 options 的可点下拉是坏 UX）
  const mockMode = isMock();

  return (
    <PageContainer
      {...financeHelpHeader('A7', '用户余额核对')}
      extra={
        !load.ok ? null : (
        !isAgentScoped && mockMode ? (
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
                // mock 演示代理项只在 mock 模式出现（OCR 修复：真实 agentId 是 GUID，live 选中必查空）
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
          { title: mockMode ? '用户' : '科目', dataIndex: 'ownerName', width: 120 },
          { title: mockMode ? '用户 ID' : '归属 ID', dataIndex: 'ownerId', width: 220, ellipsis: true },
          { title: '币种', dataIndex: 'currency', width: 80, search: false },
          { title: '可用', dataIndex: 'available', width: 90, search: false },
          { title: '冻结', dataIndex: 'frozen', width: 90, search: false },
          { title: '预收', dataIndex: 'prepayment', width: 90, search: false },
          {
            title: '账面余额合计',
            dataIndex: 'ledgerDerived',
            width: 100,
            search: false,
          },
          {
            title: '差异',
            dataIndex: 'diff',
            width: 90,
            search: false,
            hideInTable: !mockMode,
            render: (_, r) => (
              <Tag color={r.diff === 0 ? 'success' : 'error'}>{r.diff}</Tag>
            ),
          },
          {
            title: '代理',
            dataIndex: 'agentId',
            width: 100,
            search: false,
            hideInTable: isAgentScoped || !mockMode,
          },
          {
            title: '同步时间',
            dataIndex: 'syncedAt',
            search: false,
            hideInTable: !mockMode,
            render: (_, r) => formatForDisplay(r.syncedAt),
            width: 170,
          },
          {
            title: '操作',
            valueType: 'option',
            width: 120,
            hideInTable: !mockMode || !access.canViewFinanceReconcile,
            render: (_, r) =>
              mockMode && r.diff !== 0 && access.canViewFinanceReconcile ? (
                <Link to="/finance/reconcile">查看账务对账</Link>
              ) : null,
          },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default BalancesPage;
