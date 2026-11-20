import { AgGridReact } from 'ag-grid-react';
import { Drawer, Tag } from 'antd';
import React from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import { queryExecutions } from '@/abp/scheduledJobs';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import type { ScheduledJobExecution } from '../service';

interface ExecutionDrawerProps {
  jobId?: string;
  jobName?: string;
  open: boolean;
  onClose: () => void;
}

/** 执行历史抽屉：按开始时间倒序展示 AppScheduledJobExecutions。 */
const ExecutionDrawer: React.FC<ExecutionDrawerProps> = ({
  jobId,
  jobName,
  open,
  onClose,
}) => {
  const gridQuery = React.useCallback(
    (input: GridQuery, signal: AbortSignal) =>
      jobId
        ? queryExecutions(jobId, input, signal)
        : Promise.resolve({ items: [], totalCount: 0 }),
    [jobId],
  );
  const grid = useSearchableRemoteGrid<ScheduledJobExecution>({
    columnDefs: toGridColumns<ScheduledJobExecution>(
      [
        {
          title: '开始时间',
          dataIndex: 'startTime',
          width: 165,
          render: (v: string) => new Date(v).toLocaleString(),
        },
        {
          title: '结果',
          dataIndex: 'success',
          width: 80,
          render: (v: boolean) =>
            v ? <Tag color="green">成功</Tag> : <Tag color="red">失败</Tag>,
        },
        {
          title: '耗时',
          dataIndex: 'durationMs',
          width: 100,
          render: (v: number) => `${v} ms`,
        },
        {
          title: '消息',
          dataIndex: 'message',
          ellipsis: true,
          render: (v: string | null) => v ?? '—',
        },
      ],
      {
        kinds: { startTime: 'date', success: 'boolean', durationMs: 'number' },
      },
    ),
    getRowId: ({ data }) => data.id,
    ariaLabel: '执行历史',
    query: gridQuery,
  });

  return (
    <Drawer
      title={`执行历史 - ${jobName ?? ''}`}
      size={720}
      open={open}
      onClose={onClose}
      destroyOnHidden
    >
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          marginBottom: 8,
        }}
      >
        {grid.searchBox}
      </div>
      <AgGridReact<ScheduledJobExecution> {...grid.gridProps} />
    </Drawer>
  );
};

export default ExecutionDrawer;
