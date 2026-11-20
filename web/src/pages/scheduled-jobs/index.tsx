import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { App, Button, Popconfirm, Switch, Tag } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { queryScheduledJobs } from '@/abp/scheduledJobs';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import ExecutionDrawer from './components/ExecutionDrawer';
import ScheduledJobForm from './components/ScheduledJobForm';
import {
  createScheduledJob,
  deleteScheduledJob,
  getJobTypes,
  type JobTypeOption,
  type ScheduledJob,
  setJobEnabled,
  triggerJob,
  updateScheduledJob,
} from './service';

/**
 * 定时作业（cron 调度配置）管理页。
 * 与 background-jobs（一次性队列作业）职责分离：这里管周期性调度配置，
 * 数据源是 AppScheduledJobs 表 + Quartz trigger 状态，没有队列/重试语义。
 */
const ScheduledJobsPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const [historyJob, setHistoryJob] = useState<ScheduledJob>();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [jobTypes, setJobTypes] = useState<JobTypeOption[]>([]);

  // 共用处理器清单，表单打开时无需逐行重新请求。
  const loadJobTypes = useCallback(async () => {
    try {
      const result = await getJobTypes();
      setJobTypes(result.items ?? []);
    } catch {
      message.error('加载作业类型失败，请刷新页面重试');
    }
  }, [message]);

  useEffect(() => {
    if (access.canCreateScheduledJobs || access.canUpdateScheduledJobs)
      loadJobTypes();
  }, [
    loadJobTypes,
    access.canCreateScheduledJobs,
    access.canUpdateScheduledJobs,
  ]);

  const grid = useSearchableRemoteGrid<ScheduledJob>({
    columnDefs: toGridColumns<ScheduledJob>(
      [
        { title: '名称', dataIndex: 'name', ellipsis: true },
        { title: '作业类型', dataIndex: 'jobType', ellipsis: true },
        {
          title: 'cron 表达式',
          dataIndex: 'cronExpression',
        },
        {
          title: '启用',
          dataIndex: 'isEnabled',

          render: (_, record) => (
            <Switch
              checked={record.isEnabled}
              disabled={!access.canUpdateScheduledJobs}
              onChange={async (checked) => {
                await setJobEnabled(record.id, checked);
                message.success(checked ? '已启用' : '已停用');
                grid.refresh();
              }}
            />
          ),
        },
        {
          title: '上次执行',
          dataIndex: 'lastRunTime',
          valueType: 'dateTime',
        },
        {
          title: '上次结果',
          dataIndex: 'lastRunSuccess',

          render: (_, record) => {
            if (
              record.lastRunSuccess === null ||
              record.lastRunSuccess === undefined
            ) {
              return '—';
            }
            return record.lastRunSuccess ? (
              <Tag color="green">成功</Tag>
            ) : (
              <Tag color="red">失败</Tag>
            );
          },
        },
        {
          title: '下次执行',
          dataIndex: 'nextRunTime',
          valueType: 'dateTime',
        },
        {
          title: '操作',
          valueType: 'option',
          render: (_, record) => [
            access.canUpdateScheduledJobs && (
              <ScheduledJobForm
                key="edit"
                title="编辑定时作业"
                trigger={<a>编辑</a>}
                jobTypes={jobTypes}
                initialValues={record}
                onFinish={async (values) => {
                  await updateScheduledJob(record.id, {
                    name: values.name,
                    cronExpression: values.cronExpression,
                    description: values.description,
                    payload: values.payload,
                  });
                  // 更新接口不含 isEnabled（启停走独立 set-enabled 端点），
                  // 编辑弹窗里的开关状态有变化时必须补一次调用，否则会被静默丢弃
                  if (
                    values.isEnabled !== undefined &&
                    values.isEnabled !== record.isEnabled
                  ) {
                    await setJobEnabled(record.id, values.isEnabled);
                  }
                  message.success('已保存');
                  grid.refresh();
                  return true;
                }}
              />
            ),
            access.canTriggerScheduledJobs && record.isEnabled && (
              <Popconfirm
                key="trigger"
                title="确认立即触发一次？"
                description="不影响原有 cron 计划"
                onConfirm={async () => {
                  await triggerJob(record.id);
                  // 前端未订阅 ReceiveJobCompleted 推送（SignalR 已注册事件清单里没有它）：
                  // 触发结果以列表「上次执行/上次结果」列为准，手动刷新可见
                  message.loading({
                    content: '已提交触发，结果以列表为准',
                    key: record.id,
                    duration: 2,
                  });
                }}
              >
                <a>立即触发</a>
              </Popconfirm>
            ),
            <a
              key="history"
              onClick={() => {
                setHistoryJob(record);
                setDrawerOpen(true);
              }}
            >
              历史
            </a>,
            access.canDeleteScheduledJobs && (
              <Popconfirm
                key="delete"
                title="确认删除该作业？"
                onConfirm={async () => {
                  await deleteScheduledJob(record.id);
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
      { pro: true, kinds: { isEnabled: 'boolean', lastRunSuccess: 'boolean' } },
    ),
    getRowId: ({ data }) => data.id,
    ariaLabel: '定时作业',
    query: queryScheduledJobs,
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
        {[
          access.canCreateScheduledJobs && (
            <ScheduledJobForm
              key="create"
              title="新建定时作业"
              trigger={<Button type="primary">新建作业</Button>}
              jobTypes={jobTypes}
              onFinish={async (values) => {
                if (!values.jobType) {
                  message.error('请选择作业类型');
                  return false;
                }
                await createScheduledJob({
                  name: values.name,
                  jobType: values.jobType,
                  cronExpression: values.cronExpression,
                  isEnabled: values.isEnabled,
                  description: values.description,
                  payload: values.payload,
                });
                message.success('已创建');
                grid.refresh();
                return true;
              }}
            />
          ),
        ]}
      </div>
      <AgGridReact<ScheduledJob> {...grid.gridProps} />

      <ExecutionDrawer
        jobId={historyJob?.id}
        jobName={historyJob?.name}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
      />
    </PageContainer>
  );
};

export default ScheduledJobsPage;
