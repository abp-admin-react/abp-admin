import {
  type ActionType,
  PageContainer,
  ProTable,
} from '@ant-design/pro-components';
import { App, Button, Popconfirm, Switch, Tag } from 'antd';
import React, {
  useCallback,
  useEffect,
  useRef,
  useState,
} from 'react';
import ExecutionDrawer from './components/ExecutionDrawer';
import ScheduledJobForm from './components/ScheduledJobForm';
import {
  createScheduledJob,
  deleteScheduledJob,
  getJobTypes,
  type JobTypeOption,
  getScheduledJobs,
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
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);
  const [historyJob, setHistoryJob] = useState<ScheduledJob>();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [jobTypes, setJobTypes] = useState<JobTypeOption[]>([]);

  // 作业类型下拉数据页面级只拉一次：ProTable 操作列每行渲染一个 ScheduledJobForm 实例，
  // 加工具栏一个，pageSize 默认 20 时一次页面加载约挂载 21 个实例——若由表单各自在
  // 挂载时拉取，会放大成 21 个相同请求和 21 条错误提示。
  // 该请求已 skipErrorHandler（见 service.getJobTypes），失败只在这里弹一条
  // 「加载作业类型失败，请刷新页面重试」；恢复手段是浏览器刷新页面：
  // 表单实例随页面常驻不重挂载，仅开关弹窗无法重新触发该请求。
  const loadJobTypes = useCallback(async () => {
    try {
      const result = await getJobTypes();
      setJobTypes(result.items ?? []);
    } catch {
      message.error('加载作业类型失败，请刷新页面重试');
    }
  }, [message]);

  useEffect(() => {
    loadJobTypes();
  }, [loadJobTypes]);

  return (
    <PageContainer>
      <ProTable<ScheduledJob>
        rowKey="id"
        actionRef={actionRef}
        columns={[
          { title: '名称', dataIndex: 'name', ellipsis: true },
          { title: '作业类型', dataIndex: 'jobType', ellipsis: true },
          {
            title: 'cron 表达式',
            dataIndex: 'cronExpression',
            search: false,
          },
          {
            title: '启用',
            dataIndex: 'isEnabled',
            search: false,
            render: (_, record) => (
              <Switch
                checked={record.isEnabled}
                onChange={async (checked) => {
                  await setJobEnabled(record.id, checked);
                  message.success(checked ? '已启用' : '已停用');
                  actionRef.current?.reload();
                }}
              />
            ),
          },
          {
            title: '上次执行',
            dataIndex: 'lastRunTime',
            valueType: 'dateTime',
            search: false,
          },
          {
            title: '上次结果',
            dataIndex: 'lastRunSuccess',
            search: false,
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
            search: false,
          },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => [
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
                  actionRef.current?.reload();
                  return true;
                }}
              />,
              <Popconfirm
                key="trigger"
                title="确认立即触发一次？"
                description="不影响原有 cron 计划"
                onConfirm={async () => {
                  await triggerJob(record.id);
                  // 结果由 SignalR（ReceiveJobCompleted）推送，这里只确认已提交
                  message.loading({
                    content: '已提交，完成后会通知',
                    key: record.id,
                    duration: 2,
                  });
                }}
              >
                <a>立即触发</a>
              </Popconfirm>,
              <a
                key="history"
                onClick={() => {
                  setHistoryJob(record);
                  setDrawerOpen(true);
                }}
              >
                历史
              </a>,
              <Popconfirm
                key="delete"
                title="确认删除该作业？"
                onConfirm={async () => {
                  await deleteScheduledJob(record.id);
                  message.success('已删除');
                  actionRef.current?.reload();
                }}
              >
                <a>删除</a>
              </Popconfirm>,
            ],
          },
        ]}
        request={async (params) => {
          const result = await getScheduledJobs({
            current: params.current,
            pageSize: params.pageSize,
            name: params.name,
            jobType: params.jobType,
          });
          return {
            data: result.items ?? [],
            total: result.totalCount ?? 0,
            success: true,
          };
        }}
        toolBarRender={() => [
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
              actionRef.current?.reload();
              return true;
            }}
          />,
        ]}
      />
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
