import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { App, Button, Popconfirm, Tag } from 'antd';
import React, { useRef } from 'react';
import {
  abandonBackgroundJob,
  deleteBackgroundJob,
  enqueueTestBackgroundJob,
  getBackgroundJobs,
  retryBackgroundJob,
} from '@/abp/proModules';
import AutoHeightProTable from '@/components/AutoHeightProTable';

/** ProTable select 的 valueEnum 布尔会被序列化成字符串，统一在这里还原。 */
const toBool = (v: unknown): boolean | undefined =>
  v === true || v === 'true'
    ? true
    : v === false || v === 'false'
      ? false
      : undefined;

const BackgroundJobsPage: React.FC = () => {
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);

  return (
    <PageContainer>
      <AutoHeightProTable
        rowKey="id"
        actionRef={actionRef}
        columns={[
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
          { title: '重试次数', dataIndex: 'tryCount', search: false },
          {
            title: '下次执行',
            dataIndex: 'nextTryTime',
            valueType: 'dateTime',
            search: false,
          },
          {
            title: '创建时间',
            dataIndex: 'creationTime',
            valueType: 'dateTime',
            search: false,
          },
          {
            title: '完成时间',
            dataIndex: 'completionTime',
            valueType: 'dateTime',
            search: false,
          },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => [
              record.isAbandoned ? (
                <a
                  key="retry"
                  onClick={async () => {
                    await retryBackgroundJob(record.id);
                    message.success('已重新入队');
                    actionRef.current?.reload();
                  }}
                >
                  重试
                </a>
              ) : (
                <a
                  key="abandon"
                  onClick={async () => {
                    await abandonBackgroundJob(record.id);
                    message.success('已放弃');
                    actionRef.current?.reload();
                  }}
                >
                  放弃
                </a>
              ),
              <Popconfirm
                key="delete"
                title="确认删除该作业？"
                onConfirm={async () => {
                  await deleteBackgroundJob(record.id);
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
          const result = await getBackgroundJobs({
            current: params.current,
            pageSize: params.pageSize,
            jobName: params.jobName,
            isAbandoned: toBool(params.isAbandoned),
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() => [
          <ModalForm
            key="enqueue"
            title="入队测试邮件作业"
            trigger={<Button type="primary">入队测试作业</Button>}
            modalProps={{ destroyOnHidden: true }}
            onFinish={async (values) => {
              await enqueueTestBackgroundJob({
                emailAddress: values.emailAddress,
                subject: values.subject,
                body: values.body,
              });
              message.success('已入队');
              actionRef.current?.reload();
              return true;
            }}
          >
            <ProFormText
              name="emailAddress"
              label="收件人"
              rules={[
                { required: true, message: '请输入收件人' },
                { type: 'email', message: '邮箱格式不正确' },
              ]}
            />
            <ProFormText
              name="subject"
              label="主题"
              rules={[{ required: true, message: '请输入主题' }]}
            />
            <ProFormTextArea name="body" label="正文" />
          </ModalForm>,
        ]}
      />
    </PageContainer>
  );
};

export default BackgroundJobsPage;
