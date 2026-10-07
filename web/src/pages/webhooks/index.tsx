import {
  type ActionType,
  PageContainer,
  type ProColumns,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Input, Modal, Popconfirm, Space, Switch, Tabs, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import {
  createWebhookSubscription,
  deleteWebhookSendRecord,
  deleteWebhookSubscription,
  getWebhookSendRecords,
  getWebhookSubscriptions,
  resendWebhookSendRecord,
  type WebhookSendRecord,
  type WebhookSubscription,
  updateWebhookSubscription,
} from '@/abp/webhooks';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { firstFilterValue } from '@/components/tableColumnFilters';

/**
 * Webhook 管理：Tab1 订阅 CRUD（密钥写后不回显，留空=保持）；
 * Tab2 发送记录（终态查看 + 重发/删除）。重发以记录内负载重新入队交付作业。
 */
const WebhooksPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const subscriptionActionRef = useRef<ActionType>(undefined);
  const recordActionRef = useRef<ActionType>(undefined);

  const [subModalOpen, setSubModalOpen] = useState(false);
  const [editingSub, setEditingSub] = useState<WebhookSubscription | null>(null);
  const [subForm, setSubForm] = useState({
    webhookUri: '',
    secret: '',
    description: '',
    isActive: true,
    events: 'identity.user.created',
  });

  const openCreateSub = () => {
    setEditingSub(null);
    setSubForm({ webhookUri: '', secret: '', description: '', isActive: true, events: '' });
    setSubModalOpen(true);
  };

  const openEditSub = (record: WebhookSubscription) => {
    setEditingSub(record);
    setSubForm({
      webhookUri: record.webhookUri,
      secret: '',
      description: record.description ?? '',
      isActive: record.isActive,
      events: record.events.join(','),
    });
    setSubModalOpen(true);
  };

  const submitSub = async () => {
    const events = subForm.events
      .split(',')
      .map((e) => e.trim())
      .filter(Boolean);
    if (!subForm.webhookUri.trim() || events.length === 0 || (!editingSub && !subForm.secret.trim())) {
      message.warning('地址、事件（逗号分隔）为必填；新建时密钥必填');
      return;
    }
    try {
      if (editingSub) {
        await updateWebhookSubscription(editingSub.id, {
          webhookUri: subForm.webhookUri,
          secret: subForm.secret || undefined,
          description: subForm.description || undefined,
          isActive: subForm.isActive,
          events,
        });
        message.success('订阅已更新');
      } else {
        await createWebhookSubscription({
          webhookUri: subForm.webhookUri,
          secret: subForm.secret,
          description: subForm.description || undefined,
          isActive: subForm.isActive,
          events,
        });
        message.success('订阅已创建');
      }
      setSubModalOpen(false);
      subscriptionActionRef.current?.reload();
    } catch {
      // 服务端拒绝（重名事件等）由全局 errorHandler 呈现，保持弹窗打开
    }
  };

  const subscriptionColumns: ProColumns<WebhookSubscription>[] = [
    { title: '地址', dataIndex: 'webhookUri', ellipsis: true, copyable: true },
    {
      title: '事件',
      dataIndex: 'events',
      ellipsis: true,
      render: (_, record) => record.events.join('、'),
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 80,
      render: (_, record) =>
        record.isActive ? <Tag color="green">启用</Tag> : <Tag>暂停</Tag>,
    },
    { title: '描述', dataIndex: 'description', ellipsis: true },
    {
      title: '操作',
      width: 130,
      render: (_, record) => (
        <Space>
          {access.canUpdateWebhookSubscriptions ? (
            <Button size="small" type="link" onClick={() => openEditSub(record)}>
              编辑
            </Button>
          ) : null}
          {access.canDeleteWebhookSubscriptions ? (
            <Popconfirm
              title="删除订阅？"
              description="只停发不抹历史：已有发送记录保留。"
              onConfirm={async () => {
                await deleteWebhookSubscription(record.id);
                message.success('已删除');
                subscriptionActionRef.current?.reload();
              }}
            >
              <Button size="small" type="link" danger>
                删除
              </Button>
            </Popconfirm>
          ) : null}
        </Space>
      ),
    },
  ];

  const recordColumns: ProColumns<WebhookSendRecord>[] = [
    { title: '事件', dataIndex: 'eventName', width: 180 },
    { title: '负载', dataIndex: 'payload', ellipsis: true, copyable: true },
    {
      title: '结果',
      dataIndex: 'succeeded',
      width: 90,
      render: (_, record) =>
        record.succeeded ? <Tag color="green">成功</Tag> : <Tag color="red">失败</Tag>,
    },
    {
      title: '状态码',
      dataIndex: 'responseStatusCode',
      width: 90,
      render: (_, record) => record.responseStatusCode ?? '-',
    },
    { title: '尝试', dataIndex: 'attemptCount', width: 70 },
    { title: '最后尝试', dataIndex: 'lastAttemptTime', width: 160, valueType: 'dateTime' },
    {
      title: '操作',
      width: 130,
      render: (_, record) => (
        <Space>
          {access.canResendWebhookSendRecords ? (
            <Popconfirm
              title="重发此 Webhook？"
              description="以记录内负载重新入队交付作业，新结果覆盖终态。"
              onConfirm={async () => {
                await resendWebhookSendRecord(record.id);
                message.success('已重新入队');
                recordActionRef.current?.reload();
              }}
            >
              <Button size="small" type="link">
                重发
              </Button>
            </Popconfirm>
          ) : null}
          {access.canDeleteWebhookSendRecords ? (
            <Popconfirm
              title="删除此发送记录？"
              onConfirm={async () => {
                await deleteWebhookSendRecord(record.id);
                message.success('已删除');
                recordActionRef.current?.reload();
              }}
            >
              <Button size="small" type="link" danger>
                删除
              </Button>
            </Popconfirm>
          ) : null}
        </Space>
      ),
    },
  ];

  return (
    <PageContainer title="Webhook 管理">
      <Tabs
        items={[
          {
            key: 'subscriptions',
            label: '订阅',
            children: (
              <AutoHeightProTable<WebhookSubscription>
                headerTitle="Webhook 订阅"
                rowKey="id"
                search={false}
                options={false}
                actionRef={subscriptionActionRef}
                toolBarRender={() => [
                  access.canCreateWebhookSubscriptions ? (
                    <Button key="create" type="primary" onClick={openCreateSub}>
                      新建订阅
                    </Button>
                  ) : null,
                ]}
                request={async (params, _sorter, filters) => {
                  const result = await getWebhookSubscriptions({
                    current: params.current,
                    pageSize: params.pageSize,
                    filter: firstFilterValue(filters, 'webhookUri'),
                  });
                  return { data: result.items, total: result.totalCount, success: true };
                }}
                columns={subscriptionColumns}
                pagination={{ pageSize: 20 }}
              />
            ),
          },
          {
            key: 'records',
            label: '发送记录',
            children: (
              <AutoHeightProTable<WebhookSendRecord>
                headerTitle="发送记录（交付终态）"
                rowKey="id"
                search={false}
                options={false}
                actionRef={recordActionRef}
                request={async (params, _sorter, filters) => {
                  const result = await getWebhookSendRecords({
                    current: params.current,
                    pageSize: params.pageSize,
                    eventName: firstFilterValue(filters, 'eventName'),
                    succeeded: firstFilterValue(filters, 'succeeded') === '成功' ? true : undefined,
                  });
                  return { data: result.items, total: result.totalCount, success: true };
                }}
                columns={recordColumns}
                pagination={{ pageSize: 20 }}
              />
            ),
          },
        ]}
      />

      <Modal
        title={editingSub ? '编辑订阅' : '新建订阅'}
        open={subModalOpen}
        onOk={submitSub}
        onCancel={() => setSubModalOpen(false)}
        destroyOnHidden
      >
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          <div>
            <div style={{ marginBottom: 4 }}>Webhook 地址</div>
            <Input
              value={subForm.webhookUri}
              onChange={(e) => setSubForm((f) => ({ ...f, webhookUri: e.target.value }))}
              placeholder="https://receiver.example.com/hook"
            />
          </div>
          <div>
            <div style={{ marginBottom: 4 }}>
              {editingSub ? '密钥（留空 = 保持原值）' : 'HMAC 密钥'}
            </div>
            <Input.Password
              value={subForm.secret}
              onChange={(e) => setSubForm((f) => ({ ...f, secret: e.target.value }))}
              placeholder="至少 16 位随机字符串"
            />
          </div>
          <div>
            <div style={{ marginBottom: 4 }}>订阅事件（逗号分隔）</div>
            <Input
              value={subForm.events}
              onChange={(e) => setSubForm((f) => ({ ...f, events: e.target.value }))}
              placeholder="identity.user.created, identity.user.updated"
            />
          </div>
          <div>
            <div style={{ marginBottom: 4 }}>描述</div>
            <Input
              value={subForm.description}
              onChange={(e) => setSubForm((f) => ({ ...f, description: e.target.value }))}
            />
          </div>
          <div>
            <span style={{ marginRight: 8 }}>启用</span>
            <Switch
              checked={subForm.isActive}
              onChange={(checked) => setSubForm((f) => ({ ...f, isActive: checked }))}
            />
          </div>
        </Space>
      </Modal>
    </PageContainer>
  );
};

export default WebhooksPage;
