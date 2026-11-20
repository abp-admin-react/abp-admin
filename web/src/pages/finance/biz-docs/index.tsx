import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess, useLocation } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { Select, Space, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import { businessGridQueries, gridScope } from '@/abp/businessGrid';
import { type FinBizDocDto, isMock } from '@/abp/finance';
import { financeHelpHeader } from '@/components/FinanceHelp';
import { FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import { formatForDisplay } from '@/utils/time';
import {
  voucherBusinessTypes,
  voucherDetailsUrl,
} from '../vouchers/navigation';

const BizDocsPage: React.FC = () => {
  const access = useAccess();
  const { search } = useLocation();
  const sourceReference =
    new URLSearchParams(search).get('bizRefId') ?? undefined;
  const actionRef = useRef<{ reload: () => void }>(undefined);
  const load = useFinanceLoad();
  /** null=全部；true=仅演示；false=排除演示。代理 Scope 时 agentId 由后端强制 */
  const [isDemo, setIsDemo] = useState<boolean | null>(null);
  const [agentId, setAgentId] = useState<string | undefined>();
  const isAgentScoped = !!access.canFinanceScopeAgent;
  // 普通同步谓词（非 hook），渲染期读 localStorage、非响应式；live 下代理筛选整体隐藏（空 options 的可点下拉是坏 UX）
  const mockMode = isMock();

  const bizDocsGridScope = gridScope({
    bizRefId: sourceReference,
    agentId: isAgentScoped ? undefined : agentId,
    isDemo,
  });
  const bizDocsGrid = useSearchableRemoteGrid<FinBizDocDto>({
    ariaLabel: '业务单据',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.bizDocs, bizDocsGridScope),
    columnDefs: toGridColumns<FinBizDocDto>(
      [
        {
          title: '业务类型',
          dataIndex: 'bizType',
          width: 130,
          valueEnum: voucherBusinessTypes,
        },
        { title: '业务单号', dataIndex: 'bizRefId', width: 140 },
        { title: '业务阶段', dataIndex: 'phase', width: 100 },
        {
          title: '金额',
          dataIndex: 'amount',
          width: 110,
          render: (_, r) => `${r.amount} ${r.currency}`,
        },
        { title: '状态', dataIndex: 'status', width: 100 },
        {
          title: '代理',
          dataIndex: 'agentId',
          width: 100,
          hideInTable: isAgentScoped,
        },
        {
          title: '演示',
          dataIndex: 'isDemo',
          width: 80,
          render: (_, r) =>
            r.isDemo ? <Tag color="purple">演示</Tag> : <Tag>正式</Tag>,
        },
        {
          title: '时间',
          dataIndex: 'creationTime',
          render: (_: unknown, r: any) => formatForDisplay(r.creationTime),
          width: 170,
        },
        {
          title: '操作',
          valueType: 'option',
          width: 120,
          render: (_, row) => {
            // Live rows come from voucher heads, so their row ID is the voucher ID.
            const voucherId = mockMode ? row.voucherId : row.id;
            return access.canViewFinanceVouchers && voucherId ? (
              <Link to={voucherDetailsUrl(voucherId)}>查看会计凭证</Link>
            ) : (
              '—'
            );
          },
        },
      ],
      {
        pro: true,
        kinds: { amount: 'decimal', isDemo: 'boolean', creationTime: 'date' },
      },
    ),
  });
  actionRef.current = { reload: bizDocsGrid.refresh };
  return (
    <PageContainer
      {...financeHelpHeader('A6', '凭证来源查询')}
      extra={
        <Space wrap>
          {access.canViewFinanceVouchers && (
            <Link to="/finance/vouchers">会计凭证</Link>
          )}
          {load.ok && !isAgentScoped && mockMode && (
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
          {load.ok && (
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
          )}
        </Space>
      }
    >
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        {bizDocsGrid.searchBox}
        <AgGridReact<FinBizDocDto>
          {...bizDocsGrid.gridProps}
          key={`${load.nonce}:${sourceReference ?? ''}`}
        />
      </FinanceStateView>
    </PageContainer>
  );
};

export default BizDocsPage;
