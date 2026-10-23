import { financeHelpHeader } from '@/components/FinanceHelp';
import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { Select, Space, Tag } from 'antd';
import { formatForDisplay } from '@/utils/time';
import React, { useRef, useState } from 'react';
import { history, useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import { type FinBizDocDto, getBizDocs, isMock } from '@/abp/finance';

const BizDocsPage: React.FC = () => {
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  /** null=全部；true=仅演示；false=排除演示。代理 Scope 时 agentId 由后端强制 */
  const [isDemo, setIsDemo] = useState<boolean | null>(null);
  const [agentId, setAgentId] = useState<string | undefined>();
  const isAgentScoped = !!access.canFinanceScopeAgent;
  // 普通同步谓词（非 hook），渲染期读 localStorage、非响应式；live 下代理筛选整体隐藏（空 options 的可点下拉是坏 UX）
  const mockMode = isMock();

  return (
    <PageContainer
      {...financeHelpHeader('A6', '业务单据')}
      extra={
        !load.ok ? null : (
        <Space>
          {!isAgentScoped && mockMode && (
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
          )}
          <Select
            allowClear
            placeholder="演示单据"
            style={{ width: 140 }}
            value={isDemo === null ? undefined : isDemo ? '1' : '0'}
            onChange={(v) => {
              setIsDemo(v === undefined ? null : v === '1');
              actionRef.current?.reload();
            }}
            options={[
              { value: '1', label: '仅演示' },
              { value: '0', label: '排除演示' },
            ]}
          />
        </Space>
        )
      }
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <AutoHeightProTable<FinBizDocDto>
        key={load.nonce}
        rowKey="id"
        locale={{ emptyText: <FinanceEmpty text={'暂无业务单据'} /> }}
        actionRef={actionRef}
        search={{ labelWidth: 'auto' }}
        params={{ isDemo, agentId }}
        request={async (params) => {
          const maxResultCount = params.pageSize ?? 20;
          const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
          const result = await load.guard(() =>
            getBizDocs({
            skipCount,
            maxResultCount,
            filter: params.bizRefId || params.bizType,
            // 代理 Scope：不传 agentId，服务端按当前用户 AgentId 强制过滤
            agentId: isAgentScoped ? undefined : agentId,
            isDemo,
          }),
          );
          if (!result) return { data: [], total: 0, success: false };
          return { data: result.items, total: result.totalCount, success: true };
        }}
        columns={[
          { title: '业务类型', dataIndex: 'bizType', width: 120 },
          { title: '业务单号', dataIndex: 'bizRefId', width: 140 },
          { title: 'Phase', dataIndex: 'phase', width: 100, search: false },
          {
            title: '金额',
            dataIndex: 'amount',
            width: 110,
            search: false,
            render: (_, r) => `${r.amount} ${r.currency}`,
          },
          { title: '状态', dataIndex: 'status', width: 100, search: false },
          {
            title: '代理',
            dataIndex: 'agentId',
            width: 100,
            search: false,
            hideInTable: isAgentScoped,
          },
          {
            title: '演示',
            dataIndex: 'isDemo',
            width: 80,
            search: false,
            render: (_, r) =>
              r.isDemo ? <Tag color="purple">演示</Tag> : <Tag>正式</Tag>,
          },
          {
            title: '时间',
            dataIndex: 'creationTime',
            search: false,
            render: (_: unknown, r: any) => formatForDisplay(r.creationTime),
            width: 170,
          },
          {
            title: '操作',
            valueType: 'option',
            width: 120,
            render: (_, r) =>
              r.voucherId
                ? [
                    <a
                      key="v"
                      onClick={() =>
                        history.push(`/finance/vouchers?voucherId=${r.voucherId}`)
                      }
                    >
                      关联凭证
                    </a>,
                  ]
                : ['-'],
          },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default BizDocsPage;
