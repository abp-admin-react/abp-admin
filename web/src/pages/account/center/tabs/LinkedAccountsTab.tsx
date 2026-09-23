import { App, Button, Form, Input, Modal, Space, Table, Typography } from 'antd';
import React, { useState } from 'react';
import { applyImpersonatedTokens } from '@/abp/oidc';
import {
  getLinkedAccounts,
  linkAccount,
  switchToLinkedAccount,
  unlinkAccount,
  type LinkedAccountDto,
} from '@/abp/accountLink';
import { useAsyncData } from '@/hooks/useAsyncData';

const extractErrorMessage = (error: unknown, fallback: string) => {
  const detail = (error as { response?: { data?: { error?: { message?: unknown } } } })
    ?.response?.data?.error?.message;
  return typeof detail === 'string' && detail ? detail : fallback;
};

/** Linked Accounts 页签：绑定同租户的其他账号（密码确认），并在账号间切换（对标 ABP Pro） */
const LinkedAccountsTab: React.FC = () => {
  const { message, modal } = App.useApp();
  const { data: items, loading, refresh } = useAsyncData(async () => {
    const result = await getLinkedAccounts();
    return result ?? [];
  });
  const [linkModalOpen, setLinkModalOpen] = useState(false);
  const [linking, setLinking] = useState(false);
  const [switchingId, setSwitchingId] = useState<string>();
  const [form] = Form.useForm();

  const handleLink = async () => {
    const values = await form.validateFields();
    setLinking(true);
    try {
      await linkAccount({
        userNameOrEmail: values.userNameOrEmail.trim(),
        password: values.password,
      });
      message.success('账号已关联');
      setLinkModalOpen(false);
      form.resetFields();
      await refresh();
    } catch (error) {
      message.error(extractErrorMessage(error, '关联失败：账号不存在、已停用或密码错误'));
    } finally {
      setLinking(false);
    }
  };

  const handleSwitch = async (record: LinkedAccountDto) => {
    modal.confirm({
      title: `切换到 ${record.userName}？`,
      content: '切换后将以该账号身份操作系统（等同该账号的一次正常登录），当前页面会刷新。',
      okText: '切换',
      onOk: async () => {
        setSwitchingId(record.linkId);
        try {
          const result = await switchToLinkedAccount(record.linkId);
          await applyImpersonatedTokens(result);
          message.success(`已切换到 ${record.userName}，正在刷新…`);
          window.location.reload();
        } catch {
          message.error('切换失败，请重试');
        } finally {
          setSwitchingId(undefined);
        }
      },
    });
  };

  const handleUnlink = (record: LinkedAccountDto) => {
    modal.confirm({
      title: `解除与 ${record.userName} 的关联？`,
      content: '解除后将不能再一键切换到该账号（可重新绑定）。',
      okText: '解除关联',
      okButtonProps: { danger: true },
      onOk: async () => {
        await unlinkAccount(record.linkId);
        message.success('已解除关联');
        await refresh();
      },
    });
  };

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        关联你的其他账号（限同一租户），绑定需输入对方账号密码确认，绑定后可在账号间一键切换。
      </Typography.Paragraph>
      <Button type="primary" onClick={() => setLinkModalOpen(true)}>
        关联其他账号
      </Button>
      <Table<LinkedAccountDto>
        rowKey="linkId"
        size="small"
        loading={loading}
        dataSource={items ?? []}
        pagination={false}
        columns={[
          { title: '用户名', dataIndex: 'userName' },
          { title: '邮箱', dataIndex: 'emailAddress' },
          {
            title: '操作',
            width: 200,
            render: (_, record) => (
              <Space>
                <Button
                  size="small"
                  type="primary"
                  loading={switchingId === record.linkId}
                  onClick={() => handleSwitch(record)}
                >
                  切换到此账号
                </Button>
                <Button size="small" danger onClick={() => handleUnlink(record)}>
                  解除关联
                </Button>
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title="关联其他账号"
        open={linkModalOpen}
        onCancel={() => setLinkModalOpen(false)}
        onOk={handleLink}
        confirmLoading={linking}
        okText="关联"
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="userNameOrEmail"
            label="对方用户名或邮箱"
            rules={[{ required: true, message: '请输入对方用户名或邮箱' }]}
          >
            <Input placeholder="用户名或邮箱" autoComplete="off" />
          </Form.Item>
          <Form.Item
            name="password"
            label="对方账号密码"
            rules={[{ required: true, message: '请输入对方账号密码以确认身份' }]}
          >
            <Input.Password
              placeholder="用于证明该账号归你所有"
              autoComplete="new-password"
            />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
};

export default LinkedAccountsTab;
