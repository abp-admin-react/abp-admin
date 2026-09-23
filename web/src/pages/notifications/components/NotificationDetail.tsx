import { useQuery } from '@tanstack/react-query';
import { Descriptions, Drawer, Tag, Timeline } from 'antd';
import React from 'react';
import { getNotificationDetail } from '../service';

interface Props {
  id?: string;
  open: boolean;
  onClose: () => void;
}

function statusTag(success?: boolean | null) {
  if (success === null || success === undefined) {
    return <Tag color="processing">待发送</Tag>;
  }
  return success ? (
    <Tag color="success">成功</Tag>
  ) : (
    <Tag color="error">失败</Tag>
  );
}

/** 通知详情抽屉：基本信息 + 正文（ExtraProperties）+ 完整尝试链（T3.5 第 12 步）。 */
const NotificationDetailDrawer: React.FC<Props> = ({ id, open, onClose }) => {
  const { data, isLoading } = useQuery({
    queryKey: ['notification-management', 'detail', id],
    queryFn: () => getNotificationDetail(id as string),
    enabled: open && !!id,
  });

  return (
    <Drawer
      title="通知详情"
      size={560}
      open={open}
      onClose={onClose}
      destroyOnHidden
      loading={isLoading}
    >
      {data && (
        <>
          <Descriptions
            column={1}
            size="small"
            bordered
            items={[
              { key: 'id', label: 'ID', children: data.id },
              {
                key: 'method',
                label: '渠道',
                children: data.notificationMethod,
              },
              {
                key: 'user',
                label: '接收人',
                children: `${data.userName ?? ''} (${data.userId})`,
              },
              {
                key: 'state',
                label: '状态',
                children: statusTag(data.success),
              },
              {
                key: 'time',
                label: '创建时间',
                children: new Date(data.creationTime).toLocaleString(),
              },
              {
                key: 'completion',
                label: '完成时间',
                children: data.completionTime
                  ? new Date(data.completionTime).toLocaleString()
                  : '—',
              },
              {
                key: 'failure',
                label: '失败原因',
                children: data.failureReason ?? '—',
              },
              {
                key: 'retryFor',
                label: '重试指向',
                children: data.retryForNotificationId ?? '—（原始记录）',
              },
            ]}
          />
          <h4 style={{ marginTop: 16 }}>内容</h4>
          {/* 正文在 NotificationInfo.ExtraProperties 里（模块没有结构化正文列） */}
          <pre
            style={{
              background: '#f5f5f5',
              padding: 12,
              maxHeight: 200,
              overflow: 'auto',
            }}
          >
            {JSON.stringify(data.notificationInfoProperties ?? {}, null, 2)}
          </pre>
          <h4>尝试链（共 {data.attempts?.length ?? 0} 次）</h4>
          <Timeline
            items={(data.attempts ?? []).map((a, i) => ({
              key: a.id,
              color:
                a.success === true
                  ? 'green'
                  : a.success === false
                    ? 'red'
                    : 'blue',
              // antd v6：items.children 已弃用，改用 items.content（消除控制台弃用告警）。
              // children 仍兼容渲染、不会丢内容，只是触发告警；
              // 渲染契约由 notifications/index.test.tsx 的「详情抽屉渲染尝试链」用例钉住
              content: (
                <>
                  <div>
                    第 {i + 1} 次 {statusTag(a.success)}{' '}
                    {new Date(a.creationTime).toLocaleString()}
                  </div>
                  {a.failureReason && (
                    <div style={{ color: 'rgba(0,0,0,0.45)', fontSize: 12 }}>
                      {a.failureReason}
                    </div>
                  )}
                </>
              ),
            }))}
          />
        </>
      )}
    </Drawer>
  );
};

export default NotificationDetailDrawer;
