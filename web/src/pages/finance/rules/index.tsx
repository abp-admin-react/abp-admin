import { AgGridReact } from 'ag-grid-react';
import { Alert, Tag } from 'antd';
import { useEffect, useState } from 'react';
import { type FinPostingRuleDto, getPostingRules } from '@/abp/finance';
import { FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import { toGridColumns, useSearchableLocalGrid } from '@/components/remoteGrid';

export function FinancePostingRules() {
  const load = useFinanceLoad();
  const [rules, setRules] = useState<FinPostingRuleDto[]>([]);
  const { guard, nonce } = load;
  useEffect(() => {
    let active = true;
    void guard(() => getPostingRules()).then((page) => {
      if (active) setRules(page?.items ?? []);
    });
    return () => {
      active = false;
    };
  }, [guard, nonce]);
  const grid = useSearchableLocalGrid<FinPostingRuleDto>({
    ariaLabel: '过账规则',
    rowData: rules,
    keywordFields: [
      'bizType',
      'phase',
      'status',
      'summary',
      'debitAccountCode',
      'creditAccountCode',
    ],
    getRowId: ({ data }) => data.id,
    columnDefs: toGridColumns<FinPostingRuleDto>(
      [
        { title: 'BizType', dataIndex: 'bizType', width: 140 },
        { title: 'Phase', dataIndex: 'phase', width: 120 },
        { title: '版本', dataIndex: 'version', width: 70 },
        {
          title: '状态',
          dataIndex: 'status',
          width: 90,
          render: (_, r) => <Tag color="green">{r.status}</Tag>,
        },
        {
          title: '借方科目',
          dataIndex: 'debitAccountCode',
          width: 110,
        },
        {
          title: '贷方科目',
          dataIndex: 'creditAccountCode',
          width: 110,
        },
        { title: '说明', dataIndex: 'summary', ellipsis: true },
      ],
      { pro: true, kinds: { version: 'number' } },
    ),
  });
  return (
    <>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        title="本阶段只读：改规则走配置发布，管理端不提供编辑。"
      />
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        {grid.searchBox}
        <AgGridReact {...grid.gridProps} />
      </FinanceStateView>
    </>
  );
}
