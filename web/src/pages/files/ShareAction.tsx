import { useIntl } from '@umijs/max';
import {
  App,
  Button,
  DatePicker,
  Form,
  InputNumber,
  Modal,
  Popconfirm,
  Space,
  Table,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import React, { useCallback, useEffect, useState } from 'react';
import {
  createFileShare,
  deleteFileShare,
  type FileShareLinkDto,
  getAnonymousShareUrl,
  getFileShares,
} from '@/abp/fileShare';

/** 后端 FileShareLinkConsts：相对现在最多 30 天 */
const MAX_EXPIRE_DAYS = 30;

type ShareFormValues = {
  expireTime: Dayjs;
  maxDownloads?: number;
};

/**
 * 「分享」操作：创建表单（过期时间 ≤30 天、次数 ≥1 可选）+ 该文件已有分享列表（撤销）。
 * 后端与 API 封装本就支持（expireTime/maxDownloads/getFileShares/deleteFileShare），
 * 此前 UI 只能创建「默认 7 天、创建即复制、无法撤销」的链接（问题 9）。
 */
const ShareAction: React.FC<{
  file: API.FileInfoDto;
  onShared?: () => void;
}> = ({ file, onShared }) => {
  const intl = useIntl();
  const { message, modal } = App.useApp();
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm<ShareFormValues>();
  const [submitting, setSubmitting] = useState(false);
  const [shares, setShares] = useState<FileShareLinkDto[]>([]);

  const loadShares = useCallback(async () => {
    if (!file.id) return;
    try {
      const res = await getFileShares(file.id);
      setShares(res.items || []);
    } catch {
      message.error(intl.formatMessage({ id: 'pages.files.share.loadFailed' }));
    }
  }, [file.id, intl, message]);

  useEffect(() => {
    if (open) {
      form.setFieldsValue({
        expireTime: dayjs().add(7, 'day'),
        maxDownloads: undefined,
      });
      loadShares();
    }
  }, [open, form, loadShares]);

  const handleSubmit = useCallback(async () => {
    if (!file.id) return;
    let values: ShareFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return; // 校验失败：错误已落在字段内，不向外抛（onFinish 不会捕获 rejection）
    }
    setSubmitting(true);
    try {
      const share = await createFileShare({
        fileId: file.id,
        isPublic: true,
        expireTime: values.expireTime.toISOString(),
        maxDownloads: values.maxDownloads ?? undefined,
      });
      const url = getAnonymousShareUrl(share.downloadUrl);
      try {
        await navigator.clipboard.writeText(url);
        message.success(
          intl.formatMessage({ id: 'pages.files.share.copied' }, { url }),
        );
      } catch {
        // 剪贴板不可用（非安全上下文等）时退化为提示框内展示
        // （modal 实例与 App.useApp() 的 message 同源，消费 ConfigProvider 上下文）
        modal.success({
          content: url,
          title: intl.formatMessage({
            id: 'pages.files.share.copyFallbackTitle',
          }),
        });
      }
      await loadShares();
      onShared?.();
    } catch {
      // 创建失败：错误由全局 errorHandler 统一提示，弹窗与表单值保留供重试
    } finally {
      setSubmitting(false);
    }
  }, [file.id, form, intl, loadShares, message, modal, onShared]);

  const handleRevoke = useCallback(
    async (id: string) => {
      try {
        await deleteFileShare(id);
        message.success(
          intl.formatMessage({ id: 'pages.files.share.revoked' }),
        );
        await loadShares();
      } catch {
        // 错误由全局 errorHandler 统一提示（Popconfirm 的 onConfirm 不会捕获 rejection）
      }
    },
    [intl, loadShares, message],
  );

  const disabledDate = useCallback((current: Dayjs) => {
    return (
      current.isBefore(dayjs().endOf('day')) ||
      current.isAfter(dayjs().add(MAX_EXPIRE_DAYS, 'day').endOf('day'))
    );
  }, []);

  return (
    <>
      <a onClick={() => setOpen(true)}>
        {intl.formatMessage({ id: 'pages.files.actions.share' })}
      </a>
      <Modal
        title={intl.formatMessage(
          { id: 'pages.files.share.title' },
          { name: file.fileName },
        )}
        open={open}
        onCancel={() => setOpen(false)}
        destroyOnHidden
        footer={null}
        width={560}
      >
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          <Space align="start" style={{ display: 'flex' }} size="middle">
            <Form.Item
              name="expireTime"
              label={intl.formatMessage({ id: 'pages.files.share.expireTime' })}
              rules={[
                { required: true },
                {
                  // disabledDate 只能约束到「当天」，允许选到 now+30d 当天的 23:59:59，
                  // 而后端上界是提交时刻的精确 now+30d——提交前按后端口径校验，错误落在字段内
                  validator: (_, value?: Dayjs) =>
                    value?.isAfter(dayjs().add(MAX_EXPIRE_DAYS, 'day'))
                      ? Promise.reject(
                          new Error(
                            intl.formatMessage({
                              id: 'pages.files.share.expireTooFar',
                            }),
                          ),
                        )
                      : Promise.resolve(),
                },
              ]}
              style={{ flex: 1 }}
            >
              <DatePicker
                showTime
                disabledDate={disabledDate}
                style={{ width: '100%' }}
              />
            </Form.Item>
            <Form.Item
              name="maxDownloads"
              label={intl.formatMessage({
                id: 'pages.files.share.maxDownloads',
              })}
              extra={intl.formatMessage({
                id: 'pages.files.share.maxDownloadsHint',
              })}
              style={{ flex: 1 }}
            >
              <InputNumber min={1} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Button type="primary" htmlType="submit" loading={submitting}>
            {intl.formatMessage({ id: 'pages.files.share.create' })}
          </Button>
        </Form>

        {shares.length > 0 && (
          <>
            <Typography.Title level={5} style={{ marginTop: 16 }}>
              {intl.formatMessage({ id: 'pages.files.share.existing' })}
            </Typography.Title>
            <Table<FileShareLinkDto>
              rowKey="id"
              size="small"
              pagination={false}
              dataSource={shares}
              columns={[
                {
                  title: intl.formatMessage({
                    id: 'pages.files.share.expiresAt',
                  }),
                  dataIndex: 'expireTime',
                  render: (value: string) =>
                    dayjs(value).format('YYYY-MM-DD HH:mm'),
                },
                {
                  title: intl.formatMessage({
                    id: 'pages.files.share.downloads',
                  }),
                  dataIndex: 'downloadCount',
                  width: 110,
                  render: (_, record) =>
                    record.maxDownloads == null
                      ? intl.formatMessage(
                          { id: 'pages.files.share.downloadsCount' },
                          { count: record.downloadCount },
                        )
                      : intl.formatMessage(
                          { id: 'pages.files.share.downloadsCountWithMax' },
                          {
                            count: record.downloadCount,
                            max: record.maxDownloads,
                          },
                        ),
                },
                {
                  title: intl.formatMessage({
                    id: 'pages.files.columns.actions',
                  }),
                  width: 90,
                  render: (_, record) => (
                    <Popconfirm
                      title={intl.formatMessage({
                        id: 'pages.files.share.revokeConfirm',
                      })}
                      onConfirm={() => handleRevoke(record.id)}
                    >
                      <a style={{ color: '#ff4d4f' }}>
                        {intl.formatMessage({ id: 'pages.files.share.revoke' })}
                      </a>
                    </Popconfirm>
                  ),
                },
              ]}
            />
          </>
        )}
      </Modal>
    </>
  );
};

export default ShareAction;
