import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { App, Button, Popconfirm, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { useDictionary } from '@/hooks/useDictionary';
import { toDayEnd } from '@/utils/format';
import NotificationDetailDrawer from './components/NotificationDetail';
import SendNotificationForm from './components/SendNotificationForm';
import type { NotificationListItem } from './service';
import { getNotifications, retryNotification } from './service';

/** 发送状态标签：null=待发送，true=成功，false=失败。finalSuccess 含重试后的最终结果。 */
function StatusTag({ success }: { success?: boolean | null }) {
  if (success === null || success === undefined) {
    return <Tag color="processing">待发送</Tag>;
  }
  return success ? (
    <Tag color="success">成功</Tag>
  ) : (
    <Tag color="error">失败</Tag>
  );
}

/**
 * 通知发送记录管理页（T3.5）。管理员视角，要 Notification.Manage 权限。
 * 注意：列表刻意没有"按内容搜索"——正文在 NotificationInfo.ExtraProperties 里，
 * 数据库没有结构化正文列，技术上不支持（不是 bug，见规格 T3.5 第 13 步）。
 */
const NotificationsPage: React.FC = () => {
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);
  const [detailId, setDetailId] = useState<string>();
  const [detailOpen, setDetailOpen] = useState(false);
  const [sendOpen, setSendOpen] = useState(false);
  const methodDict = useDictionary('NotificationMethod');

  return (
    <PageContainer>
      <AutoHeightProTable<NotificationListItem>
        rowKey="id"
        actionRef={actionRef}
        headerTitle="发送记录"
        toolBarRender={() => [
          <Button key="send" type="primary" onClick={() => setSendOpen(true)}>
            发送通知 / 公告
          </Button>,
        ]}
        request={async (params) => {
          const range = params.creationTime as [string, string] | undefined;
          const result = await getNotifications({
            current: params.current,
            pageSize: params.pageSize,
            notificationMethod: params.notificationMethod,
            success:
              params.success === 'true'
                ? true
                : params.success === 'false'
                  ? false
                  : undefined,
            pendingOnly: params.success === 'null' ? true : undefined,
            userName: params.userName,
            creationTimeStart: range?.[0],
            creationTimeEnd: toDayEnd(range?.[1]),
            includeRetries:
              params.includeRetries === 'true'
                ? true
                : params.includeRetries === 'false'
                  ? false
                  : undefined,
          });
          return {
            data: result.items ?? [],
            total: result.totalCount ?? 0,
            success: true,
          };
        }}
        columns={[
          {
            title: '创建时间',
            dataIndex: 'creationTime',
            valueType: 'dateRange',
            render: (_, record) =>
              new Date(record.creationTime).toLocaleString(),
          },
          {
            title: '渠道',
            dataIndex: 'notificationMethod',
            valueType: 'select',
            fieldProps: { options: methodDict.options },
            render: (_, record) => {
              const item = methodDict.options.find(
                (o) => o.value === record.notificationMethod,
              );
              return <Tag>{item?.label ?? record.notificationMethod}</Tag>;
            },
          },
          {
            title: '接收人',
            dataIndex: 'userName',
            render: (_, record) => record.userName ?? record.userId,
          },
          {
            title: '状态',
            dataIndex: 'success',
            valueType: 'select',
            fieldProps: {
              options: [
                { label: '成功', value: 'true' },
                { label: '失败', value: 'false' },
                { label: '待发送', value: 'null' },
              ],
            },
            // 状态列显示最终状态（有重试时取最后一次尝试）
            render: (_, record) => <StatusTag success={record.finalSuccess} />,
          },
          {
            title: '重试次数',
            dataIndex: 'retryCount',
            search: false,
            width: 90,
          },
          {
            title: '包含重试记录',
            dataIndex: 'includeRetries',
            valueType: 'select',
            hideInTable: true,
            fieldProps: {
              allowClear: false,
              options: [
                { label: '否', value: 'false' },
                { label: '是', value: 'true' },
              ],
            },
          },
          {
            title: '失败原因',
            dataIndex: 'failureReason',
            search: false,
            ellipsis: true,
            render: (_, record) => record.failureReason ?? '—',
          },
          {
            title: '操作',
            valueType: 'option',
            width: 150,
            render: (_, record) => [
              <Button
                key="detail"
                type="link"
                size="small"
                onClick={() => {
                  setDetailId(record.id);
                  setDetailOpen(true);
                }}
              >
                详情
              </Button>,
              record.finalSuccess === false ? (
                <Popconfirm
                  key="retry"
                  title="创建一条重试记录并重新发送？"
                  okText="重试"
                  cancelText="取消"
                  onConfirm={async () => {
                    await retryNotification(record.id);
                    message.success('已创建重试，稍后可在详情查看尝试链');
                    actionRef.current?.reload();
                  }}
                >
                  <Button type="link" size="small">
                    重试
                  </Button>
                </Popconfirm>
              ) : null,
            ],
          },
        ]}
      />
      <NotificationDetailDrawer
        id={detailId}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
      />
      <SendNotificationForm
        open={sendOpen}
        onOpenChange={setSendOpen}
        onSent={() => actionRef.current?.reload()}
      />
    </PageContainer>
  );
};

export default NotificationsPage;
