import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { App, Popconfirm, Tag } from 'antd';
import React from 'react';
import {
  abandonBackgroundJob,
  type BackgroundJobDto,
  deleteBackgroundJob,
  queryBackgroundJobs,
  retryBackgroundJob,
} from '@/abp/proModules';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const BackgroundJobsPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();

  const grid = useSearchableRemoteGrid<BackgroundJobDto>({
    columnDefs: toGridColumns<BackgroundJobDto>(
      [
        { title: '作业名', dataIndex: 'jobName', ellipsis: true },
        {
          title: '已放弃',
          dataIndex: 'isAbandoned',
          valueType: 'select',
          valueEnum: {
            true: { text: '是' },
            false: { text: '否' },
          },
          render: (_, record) =>
            record.isAbandoned ? (
              <Tag color="red">已放弃</Tag>
            ) : (
              <Tag>排队中</Tag>
            ),
        },
        { title: '重试次数', dataIndex: 'tryCount' },
        {
          title: '下次执行',
          dataIndex: 'nextTryTime',
          valueType: 'dateTime',
        },
        {
          title: '创建时间',
          dataIndex: 'creationTime',
          valueType: 'dateTime',
        },
        {
          title: '完成时间',
          dataIndex: 'completionTime',
          valueType: 'dateTime',
        },
        {
          title: '操作',
          valueType: 'option',
          hideInTable:
            !access.canRetryBackgroundJobs &&
            !access.canAbandonBackgroundJobs &&
            !access.canDeleteBackgroundJobs,
          render: (_, record) => [
            record.isAbandoned && access.canRetryBackgroundJobs ? (
              <a
                key="retry"
                onClick={async () => {
                  await retryBackgroundJob(record.id);
                  message.success('已重新入队');
                  grid.refresh();
                }}
              >
                重试
              </a>
            ) : !record.isAbandoned && access.canAbandonBackgroundJobs ? (
              <a
                key="abandon"
                onClick={async () => {
                  await abandonBackgroundJob(record.id);
                  message.success('已放弃');
                  grid.refresh();
                }}
              >
                放弃
              </a>
            ) : null,
            access.canDeleteBackgroundJobs && (
              <Popconfirm
                key="delete"
                title="确认删除该作业？"
                onConfirm={async () => {
                  await deleteBackgroundJob(record.id);
                  message.success('已删除');
                  grid.refresh();
                }}
              >
                <a>删除</a>
              </Popconfirm>
            ),
          ],
        },
      ],
      { pro: true, kinds: { isAbandoned: 'boolean', tryCount: 'number' } },
    ),
    getRowId: ({ data }) => data.id,
    ariaLabel: '后台作业',
    query: queryBackgroundJobs,
  });

  return (
    <PageContainer>
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
      <AgGridReact<BackgroundJobDto> {...grid.gridProps} />
    </PageContainer>
  );
};

export default BackgroundJobsPage;
