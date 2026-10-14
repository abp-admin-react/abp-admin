import FinanceHelpTitle from '@/components/FinanceHelp';
import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { App, Button, DatePicker, Space, Switch, Tag } from 'antd';
import dayjs from 'dayjs';
import { formatForDisplay, rangeToUtcBounds } from '@/utils/time';
import React, { useRef, useState } from 'react';
import { Access, useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  type FinReconcileRunDto,
  financeErrorText,
  getReconcileRuns,
  runReconcile,
} from '@/abp/finance';

const { RangePicker } = DatePicker;

const ReconcilePage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([
    dayjs().startOf('month'),
    dayjs(),
  ]);
  /** 产品 v1.1：「包含演示单据」默认关（= 排除 IsDemo），映射 includeDemo */
  const [includeDemo, setIncludeDemo] = useState(false);
  const EMPTY_TEXT = access.canRunFinanceReconcile
    ? '还没有对账记录，选择日期范围后跑对账'
    : '还没有对账记录';
  const [running, setRunning] = useState(false);

  return (
    <PageContainer
      title={<FinanceHelpTitle page="A5" title="A5 对账" />}
      extra={
        !load.ok ? null : (
        <Space>
          {/* 有 Finance.Ledger.View 即可见（页面本身就要 Ledger.View） */}
          <Access accessible={!!access.canViewFinanceLedger}>
            <span>
              包含演示单据{' '}
              <Switch
                checked={includeDemo}
                onChange={(v) => {
                  setIncludeDemo(v);
                  actionRef.current?.reload();
                }}
              />
            </span>
          </Access>
          <Access accessible={!!access.canRunFinanceReconcile}>
          <Space>
            <span>对账区间（按过账时间）</span>
            <RangePicker
              value={range}
              onChange={(v) => {
                if (v?.[0] && v?.[1]) setRange([v[0], v[1]]);
              }}
            />
            <Button
              type="primary"
              loading={running}
              onClick={async () => {
                setRunning(true);
                try {
                  const row = await runReconcile({
                    ...(() => { const b = rangeToUtcBounds(range[0], range[1]); return { rangeStart: b.startUtc, rangeEnd: b.endUtc }; })(),
                    
                    includeDemo,
                  });
                  message.success(
                    row.diffCount === 0
                      ? '对账完成：0 差异'
                      : `对账完成：${row.diffCount} 笔差异`,
                  );
                  actionRef.current?.reload();
                } catch (e) {
                  message.error(financeErrorText(e));
                } finally {
                  setRunning(false);
                }
              }}
            >
              跑对账
            </Button>
          </Space>
          </Access>
        </Space>
        )
      }
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <AutoHeightProTable<FinReconcileRunDto>
        key={load.nonce}
        rowKey="id"
        locale={{ emptyText: <FinanceEmpty text={EMPTY_TEXT} /> }}
        actionRef={actionRef}
        search={false}
        params={{ includeDemo }}
        request={async (params) => {
          const maxResultCount = params.pageSize ?? 20;
          const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
          const result = await load.guard(() =>
            getReconcileRuns({ skipCount, maxResultCount, includeDemo }),
          );
          if (!result) return { data: [], total: 0, success: false };
          return { data: result.items, total: result.totalCount, success: true };
        }}
        columns={[
          { title: '类型', dataIndex: 'type', width: 140 },
          { title: '起始', dataIndex: 'rangeStart', width: 200, render: (_: unknown, r: any) => formatForDisplay(r.rangeStart) },
          { title: '截止', dataIndex: 'rangeEnd', width: 200, render: (_: unknown, r: any) => formatForDisplay(r.rangeEnd) },
          {
            title: '差异数',
            dataIndex: 'diffCount',
            width: 90,
            render: (_, r) => (
              <Tag color={r.diffCount === 0 ? 'success' : 'error'}>
                {r.diffCount}
              </Tag>
            ),
          },
          {
            title: '演示单据',
            dataIndex: 'excludedDemo',
            width: 100,
            render: (_, r) => (r.excludedDemo ? '已排除' : '已包含'),
          },
          { title: '状态', dataIndex: 'status', width: 90 },
          {
            title: '时间',
            dataIndex: 'createdAt',
            search: false,
            render: (_: unknown, r: any) => formatForDisplay(r.createdAt),
            width: 170,
          },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default ReconcilePage;
