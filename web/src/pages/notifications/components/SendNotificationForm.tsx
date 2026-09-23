import {
  ModalForm,
  ProFormCheckbox,
  ProFormDependency,
  ProFormRadio,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { App, Modal, Progress } from 'antd';
import React, { useState } from 'react';
import { getRoles, getUsers } from '@/abp/identity';
import { getOrganizationUnits } from '@/abp/identityAdmin';
import { useDictionary } from '@/hooks/useDictionary';
import {
  type BroadcastInput,
  broadcastNotification,
  getBroadcast,
  sendNotificationToUsers,
} from '../service';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSent: () => void;
}

/** 广播进度轮询间隔（终态 Completed/Failed 时停止）。 */
const BROADCAST_POLL_INTERVAL_MS = 2000;

/** 渠道标识，与后端 NotificationMethodConsts / 字典 NotificationMethod 的值对齐。 */
const NOTIFICATION_METHOD = {
  mailing: 'Mailing',
  sms: 'Sms',
  inApp: 'InApp',
} as const;

/**
 * 手动发送 / 发公告表单（T3.5 第 13 步）。
 * 广播提交后弹出批次进度（每 2 秒轮询直到完成/失败）。
 */
const SendNotificationForm: React.FC<Props> = ({
  open,
  onOpenChange,
  onSent,
}) => {
  const { message } = App.useApp();
  const methodDict = useDictionary('NotificationMethod');
  const [batchId, setBatchId] = useState<string>();

  const { data: batch } = useQuery({
    queryKey: ['notification-broadcast', batchId],
    queryFn: () => getBroadcast(batchId as string),
    enabled: !!batchId,
    refetchInterval: (query) =>
      query.state.data?.state === 'Completed' ||
      query.state.data?.state === 'Failed'
        ? false
        : BROADCAST_POLL_INTERVAL_MS,
  });

  const buildSmsProperties = (values: {
    notificationMethods?: string[];
    smsTemplateCode?: string;
    smsSignName?: string;
  }) =>
    values.notificationMethods?.includes(NOTIFICATION_METHOD.sms) &&
    values.smsTemplateCode
      ? {
          // 两套 key 都放，两家 sender 各取所需
          TemplateCode: values.smsTemplateCode,
          TemplateID: values.smsTemplateCode,
          ...(values.smsSignName ? { SignName: values.smsSignName } : {}),
        }
      : undefined;

  return (
    <>
      <ModalForm
        title="发送通知 / 公告"
        open={open}
        onOpenChange={onOpenChange}
        modalProps={{ destroyOnHidden: true }}
        width={640}
        onFinish={async (values) => {
          const methods = (values.notificationMethods as string[]) ?? [];
          const smsProperties = buildSmsProperties(values);

          if (values.mode === 'broadcast') {
            const input: BroadcastInput = {
              targetType: values.targetType,
              targetId: values.targetId || undefined,
              notificationMethods: methods,
              title: values.title,
              body: values.body,
              smsText: values.smsText || undefined,
              smsProperties,
            };
            const id = await broadcastNotification(input);
            message.success('已提交，正在后台发送');
            setBatchId(id);
            onOpenChange(false);
            onSent();
            return true;
          }

          await sendNotificationToUsers({
            userIds: values.userIds ?? [],
            notificationMethods: methods,
            title: values.title,
            body: values.body,
            smsText: values.smsText || undefined,
            smsProperties,
          });
          message.success('已提交发送');
          onOpenChange(false);
          onSent();
          return true;
        }}
      >
        <ProFormRadio.Group
          name="mode"
          label="发送方式"
          initialValue="broadcast"
          options={[
            { label: '发公告（批量）', value: 'broadcast' },
            { label: '指定用户', value: 'users' },
          ]}
        />
        <ProFormDependency name={['mode']}>
          {({ mode }) =>
            mode === 'broadcast' ? (
              <>
                <ProFormRadio.Group
                  name="targetType"
                  label="目标类型"
                  initialValue="All"
                  options={[
                    { label: '全部用户', value: 'All' },
                    { label: '角色', value: 'Role' },
                    { label: '组织单元', value: 'OrganizationUnit' },
                  ]}
                />
                <ProFormDependency name={['targetType']}>
                  {({ targetType }) => {
                    if (targetType === 'Role') {
                      return (
                        <ProFormSelect
                          name="targetId"
                          label="目标角色"
                          showSearch
                          rules={[{ required: true, message: '请选择角色' }]}
                          request={async ({ keyWords }) => {
                            const result = await getRoles({
                              filter: keyWords,
                              pageSize: 20,
                            });
                            return (result.items ?? []).map((r) => ({
                              label: r.name,
                              value: r.id,
                            }));
                          }}
                        />
                      );
                    }
                    if (targetType === 'OrganizationUnit') {
                      return (
                        <ProFormSelect
                          name="targetId"
                          label="目标组织单元"
                          showSearch
                          rules={[
                            { required: true, message: '请选择组织单元' },
                          ]}
                          request={async () => {
                            const result = await getOrganizationUnits();
                            return (result ?? []).map((o) => ({
                              label: o.displayName,
                              value: o.id,
                            }));
                          }}
                        />
                      );
                    }
                    return null;
                  }}
                </ProFormDependency>
              </>
            ) : (
              <ProFormSelect
                name="userIds"
                label="接收用户"
                mode="multiple"
                showSearch
                rules={[{ required: true, message: '请选择接收用户' }]}
                request={async ({ keyWords }) => {
                  const result = await getUsers({
                    filter: keyWords,
                    pageSize: 20,
                  });
                  return (result.items ?? []).map((u) => ({
                    label: `${u.userName} (${u.email ?? '-'})`,
                    value: u.id,
                  }));
                }}
              />
            )
          }
        </ProFormDependency>
        <ProFormCheckbox.Group
          name="notificationMethods"
          label="渠道"
          rules={[{ required: true, message: '至少选择一个渠道' }]}
          options={methodDict.options}
        />
        <ProFormText
          name="title"
          label="标题"
          rules={[{ required: true, message: '请输入标题' }]}
          fieldProps={{ maxLength: 256, showCount: true }}
        />
        <ProFormTextArea
          name="body"
          label="正文"
          rules={[{ required: true, message: '请输入正文' }]}
        />
        <ProFormDependency name={['notificationMethods']}>
          {({ notificationMethods }) =>
            notificationMethods?.includes(NOTIFICATION_METHOD.sms) ? (
              <>
                <ProFormText
                  name="smsTemplateCode"
                  label="短信模板编码"
                  tooltip="阿里云填模板 CODE，腾讯云填 TemplateID"
                  rules={[
                    { required: true, message: '渠道含短信时必填模板编码' },
                  ]}
                />
                <ProFormText
                  name="smsSignName"
                  label="短信签名"
                  tooltip="可选。不填时用设置项里的缺省签名"
                />
                <ProFormTextArea
                  name="smsText"
                  label="短信模板参数"
                  tooltip='阿里云路径当模板参数 JSON（如 {"code":"123456"}）；腾讯云按位置参数'
                  placeholder='{"code":"123456"}'
                />
              </>
            ) : null
          }
        </ProFormDependency>
      </ModalForm>
      <Modal
        title="广播进度"
        open={!!batchId}
        footer={null}
        onCancel={() => setBatchId(undefined)}
      >
        {batch && (
          <>
            <p>批次 ID：{batch.id}</p>
            <p>
              状态：
              {batch.state === 'Completed'
                ? '已完成'
                : batch.state === 'Failed'
                  ? '失败'
                  : '进行中'}
            </p>
            <Progress
              percent={
                batch.totalCount > 0
                  ? Math.round((batch.sentCount / batch.totalCount) * 100)
                  : 100
              }
              status={batch.state === 'Failed' ? 'exception' : undefined}
            />
            <p>
              已创建 {batch.sentCount} / {batch.totalCount}
            </p>
          </>
        )}
      </Modal>
    </>
  );
};

export default SendNotificationForm;
