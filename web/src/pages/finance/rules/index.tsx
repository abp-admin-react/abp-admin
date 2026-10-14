import FinanceHelpTitle from '@/components/FinanceHelp';
import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { Alert, Tag } from 'antd';
import React, { useRef } from 'react';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import { type FinPostingRuleDto, getPostingRules } from '@/abp/finance';

const RulesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  return (
    <PageContainer title={<FinanceHelpTitle page="A2b" title="A2b 过账规则" />}>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="本阶段只读：改规则走配置发布，管理端不提供编辑。"
      />
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <AutoHeightProTable<FinPostingRuleDto>
        key={load.nonce}
        rowKey="id"
        locale={{ emptyText: <FinanceEmpty text={'暂无过账规则'} /> }}
        actionRef={actionRef}
        search={{ labelWidth: 'auto' }}
        toolBarRender={false}
        request={async (params) => {
          const maxResultCount = params.pageSize ?? 20;
          const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
          const result = await load.guard(() =>
            getPostingRules({
            skipCount,
            maxResultCount,
            filter: params.bizType,
          }),
          );
          if (!result) return { data: [], total: 0, success: false };
          return { data: result.items, total: result.totalCount, success: true };
        }}
        columns={[
          { title: 'BizType', dataIndex: 'bizType', width: 140 },
          { title: 'Phase', dataIndex: 'phase', width: 120 },
          { title: '版本', dataIndex: 'version', width: 70, search: false },
          {
            title: '状态',
            dataIndex: 'status',
            width: 90,
            search: false,
            render: (_, r) => <Tag color="green">{r.status}</Tag>,
          },
          {
            title: '借方科目',
            dataIndex: 'debitAccountCode',
            width: 110,
            search: false,
          },
          {
            title: '贷方科目',
            dataIndex: 'creditAccountCode',
            width: 110,
            search: false,
          },
          { title: '说明', dataIndex: 'summary', ellipsis: true, search: false },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default RulesPage;
