import { PageContainer } from '@ant-design/pro-components';
import { AgGridReact } from 'ag-grid-react';
import { App, Button, Checkbox, Popconfirm, Tag } from 'antd';
import React, { useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import {
  type NotificationListItem,
  queryNotifications,
  retryNotification,
} from '@/abp/notificationManagement';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useDictionary } from '@/hooks/useDictionary';
import FrameworkConfiguration from '@/pages/settings/FrameworkConfiguration';
import NotificationDetailDrawer from './components/NotificationDetail';
import SendNotificationForm from './components/SendNotificationForm';

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
const NotificationsPage: React.FC<{ configurationEntry?: React.ReactNode }> = ({
  configurationEntry,
}) => {
  const { message } = App.useApp();
  const [detailId, setDetailId] = useState<string>();
  const [detailOpen, setDetailOpen] = useState(false);
  const [sendOpen, setSendOpen] = useState(false);
  const [includeRetries, setIncludeRetries] = useState(false);
  const methodDict = useDictionary('NotificationMethod');

  const gridQuery = React.useCallback(
    (input: GridQuery, signal: AbortSignal) =>
      queryNotifications(includeRetries, input, signal),
    [includeRetries],
  );
  const grid = useSearchableRemoteGrid<NotificationListItem>({
    columnDefs: toGridColumns<NotificationListItem>(
      [
        {
          title: '创建时间',
          dataIndex: 'creationTime',
          valueType: 'dateTime',
          render: (_, record) => new Date(record.creationTime).toLocaleString(),
        },
        {
          title: '渠道',
          dataIndex: 'notificationMethod',
          valueType: 'select',
          valueEnum: Object.fromEntries(
            methodDict.options.map((o) => [o.value, String(o.label)]),
          ),
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
          dataIndex: 'finalSuccess',
          valueEnum: { true: '成功', false: '失败', pending: '待发送' },
          valueType: 'select',
          // 状态列显示最终状态（有重试时取最后一次尝试）
          render: (_, record) => <StatusTag success={record.finalSuccess} />,
        },
        {
          title: '重试次数',
          dataIndex: 'retryCount',

          width: 90,
        },
        {
          title: '失败原因',
          dataIndex: 'failureReason',

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
                  grid.refresh();
                }}
              >
                <Button type="link" size="small">
                  重试
                </Button>
              </Popconfirm>
            ) : null,
          ],
        },
      ],
      { pro: true, kinds: { creationTime: 'date', retryCount: 'number' } },
    ),
    getRowId: ({ data }) => data.id,
    ariaLabel: '发送记录',
    query: gridQuery,
  });

  return (
    <PageContainer extra={configurationEntry}>
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          marginBottom: 8,
        }}
      >
        {'发送记录'}
        {grid.searchBox}
        {[
          <Button key="send" type="primary" onClick={() => setSendOpen(true)}>
            发送通知 / 公告
          </Button>,
        ]}
        {
          <>
            <Checkbox
              checked={includeRetries}
              onChange={(event) => setIncludeRetries(event.target.checked)}
            >
              包含重试记录
            </Checkbox>
          </>
        }
      </div>
      <AgGridReact<NotificationListItem> {...grid.gridProps} />

      <NotificationDetailDrawer
        id={detailId}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
      />
      <SendNotificationForm
        open={sendOpen}
        onOpenChange={setSendOpen}
        onSent={() => grid.refresh()}
      />
    </PageContainer>
  );
};

export default function Page() {
  return (
    <FrameworkConfiguration owner="notification">
      {(configurationEntry) => (
        <NotificationsPage configurationEntry={configurationEntry} />
      )}
    </FrameworkConfiguration>
  );
}
