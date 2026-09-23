import { Drawer, Table, Tag } from 'antd';
import React, { useEffect, useState } from 'react';
import { getExecutions, type ScheduledJobExecution } from '../service';

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
  const [items, setItems] = useState<ScheduledJobExecution[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);

  useEffect(() => {
    if (!open || !jobId) {
      return;
    }

    const load = async () => {
      setLoading(true);
      try {
        const result = await getExecutions(jobId, {
          current: page,
          pageSize: 10,
        });
        setItems(result.items ?? []);
        setTotal(result.totalCount ?? 0);
      } finally {
        setLoading(false);
      }
    };

    // 错误提示由全局 request 拦截器统一弹出，这里只需兜住 rejection 避免 unhandled rejection
    load().catch(() => undefined);
  }, [open, jobId, page]);

  return (
    <Drawer
      title={`执行历史 - ${jobName ?? ''}`}
      size={720}
      open={open}
      onClose={() => {
        setPage(1);
        onClose();
      }}
      destroyOnHidden
    >
      <Table<ScheduledJobExecution>
        rowKey="id"
        size="small"
        loading={loading}
        dataSource={items}
        pagination={{
          current: page,
          pageSize: 10,
          total,
          onChange: setPage,
        }}
        columns={[
          {
            title: '开始时间',
            dataIndex: 'startTime',
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
        ]}
      />
    </Drawer>
  );
};

export default ExecutionDrawer;
