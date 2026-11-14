import { financeHelpHeader } from '@/components/FinanceHelp';
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
    ? '暂无对账批次，选择日期范围后开始对账'
    : '暂无对账批次';
  const [running, setRunning] = useState(false);

  return (
    <PageContainer
      {...financeHelpHeader('A5', '账务对账')}
      extra={
        !load.ok ? null : (
        <Space>
          <Access accessible={!!access.canViewFinanceReconcile}>
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
                  // 后端 runReconcile 按 UTC 半开区间过滤过账时间（rangeToUtcBounds：
                  // 结束日取次日 0 点，避免漏掉结束日当天数据），本地日选择需先转 UTC 边界
                  const b = rangeToUtcBounds(range[0], range[1]);
                  const row = await runReconcile({
                    rangeStart: b.startUtc,
                    rangeEnd: b.endUtc,
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
              开始对账
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
          {
            title: '对账类型',
            dataIndex: 'type',
            width: 140,
            render: (_, r) => r.type === 'ledger-internal' ? '账簿内部核对' : r.type,
          },
          { title: '过账起始', dataIndex: 'rangeStart', width: 200, render: (_, r) => formatForDisplay(r.rangeStart) },
          { title: '过账截止', dataIndex: 'rangeEnd', width: 200, render: (_, r) => formatForDisplay(r.rangeEnd) },
          {
            title: '差异项数',
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
          {
            title: '对账结果',
            dataIndex: 'status',
            width: 100,
            render: (_, r) => r.diffCount === 0 ? '无差异' : '有差异',
          },
          {
            title: '执行时间',
            dataIndex: 'createdAt',
            search: false,
            render: (_, r) => formatForDisplay(r.createdAt),
            width: 170,
          },
        ]}
      />
      </FinanceStateView>
    </PageContainer>
  );
};

export default ReconcilePage;
