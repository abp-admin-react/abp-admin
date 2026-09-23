import { App, Button, Input, Space, Table, Tag, Typography } from 'antd';
import React, { useRef, useState } from 'react';
import {
  createGdprRequest,
  deleteCurrentUserAccount,
  type GdprRequestDto,
  getGdprDownloadToken,
  getGdprDownloadUrl,
  getGdprRequests,
  isNewGdprRequestAllowed,
} from '@/abp/gdpr';
import { useAsyncData } from '@/hooks/useAsyncData';

/** GDPR 个人数据 Tab：发起收集请求 → 就绪后两步下载 ZIP → 删除账户（自 index.tsx 拆分，见重构报告问题 4） */
const PersonalDataTab: React.FC = () => {
  const { message, modal } = App.useApp();
  const { data, loading, refresh } = useAsyncData(async () => {
    const [list, canCreate] = await Promise.all([
      getGdprRequests({ current: 1, pageSize: 20 }),
      isNewGdprRequestAllowed(),
    ]);
    return { requests: list.items ?? [], allowed: !!canCreate };
  });
  const [creating, setCreating] = useState(false);
  // modal.confirm 的 content/onOk 是打开瞬间的一次性快照（antd useModal 语义，只有
  // instance.update 会刷新），受控 state 在快照闭包里永远是旧值——首次打开读到空、
  // 重开读到上一次输入。改用 ref 承载实时输入，onOk 执行时读取（输入框本身非受控，
  // 每次 confirm 重建 content，天然为空）
  const deletePasswordRef = useRef('');

  const handleCreate = async () => {
    setCreating(true);
    try {
      await createGdprRequest();
      message.success('已发起个人数据收集请求，数据准备完成后可下载');
      await refresh();
    } finally {
      setCreating(false);
    }
  };

  const handleDownload = async (record: GdprRequestDto) => {
    try {
      const token = await getGdprDownloadToken(record.id);
      // 第二步：用 token 换 ZIP。新开标签触发下载（与 files 页一致），而非改写
      // window.location.href——下载失败时后者会把 SPA 整页替换成错误 JSON
      window.open(getGdprDownloadUrl(record.id, token), '_blank', 'noopener');
    } catch {
      // 错误由全局 errorHandler 统一提示
    }
  };

  const handleDeleteAccount = () => {
    deletePasswordRef.current = '';
    modal.confirm({
      title: '永久删除账户',
      content: (
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Typography.Text type="danger">
            此操作不可逆：账户将被匿名化并删除，个人数据请求记录一并清除。
          </Typography.Text>
          <Typography.Text>请输入当前密码确认：</Typography.Text>
          <Input.Password
            placeholder="当前密码"
            onChange={(e) => {
              deletePasswordRef.current = e.target.value;
            }}
          />
        </Space>
      ),
      okText: '永久删除',
      okButtonProps: { danger: true },
      cancelText: '取消',
      onOk: async () => {
        const password = deletePasswordRef.current;
        if (!password) {
          message.error('请输入当前密码');
          return Promise.reject(new Error('password required'));
        }
        await deleteCurrentUserAccount(password);
        message.success('账户已删除，即将退出登录');
        // 账户已删除，会话随即失效，刷新回到登录页
        setTimeout(() => window.location.reload(), 800);
      },
    });
  };

  const now = Date.now();
  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Space>
        <Button
          type="primary"
          loading={creating}
          disabled={!data?.allowed}
          onClick={handleCreate}
        >
          发起数据收集请求
        </Button>
        {!data?.allowed && (
          <Typography.Text type="secondary">
            请求过于频繁，请稍后再试
          </Typography.Text>
        )}
      </Space>
      <Table<GdprRequestDto>
        rowKey="id"
        size="small"
        loading={loading}
        dataSource={data?.requests ?? []}
        pagination={false}
        columns={[
          {
            title: '请求时间',
            dataIndex: 'creationTime',
            render: (v: string) => new Date(v).toLocaleString(),
          },
          {
            title: '就绪时间',
            dataIndex: 'readyTime',
            render: (v: string) => new Date(v).toLocaleString(),
          },
          {
            title: '状态',
            key: 'status',
            render: (_, record) =>
              new Date(record.readyTime).getTime() <= now ? (
                <Tag color="success">已就绪</Tag>
              ) : (
                <Tag color="processing">准备中</Tag>
              ),
          },
          {
            title: '操作',
            key: 'actions',
            render: (_, record) => (
              <Button
                type="link"
                size="small"
                disabled={new Date(record.readyTime).getTime() > now}
                onClick={() => handleDownload(record)}
              >
                下载 ZIP
              </Button>
            ),
          },
        ]}
      />
      <Button danger onClick={handleDeleteAccount}>
        永久删除我的账户
      </Button>
    </Space>
  );
};

export default PersonalDataTab;
