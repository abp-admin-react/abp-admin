import {
  PageContainer,
  ProCard,
  ProDescriptions,
  ProTable,
} from '@ant-design/pro-components';
import { App, Breadcrumb, Button, Drawer, Typography } from 'antd';
import React, { useState } from 'react';
import {
  getApiAppVirtualFileExplorer,
  getApiAppVirtualFileExplorerContent,
} from '@/services/abpadmin/virtualFileExplorer';

const VirtualFileExplorerPage: React.FC = () => {
  const { message } = App.useApp();
  const [path, setPath] = useState('/');
  const [parentPath, setParentPath] = useState<string>();
  const [preview, setPreview] = useState<{
    name?: string;
    path?: string;
    length?: number;
    isBinary?: boolean;
    truncated?: boolean;
    content?: string;
  }>();

  const crumbs = path
    .split('/')
    .filter(Boolean)
    .reduce<{ title: string; href: string }[]>((items, part) => {
      const href = `${items.at(-1)?.href || ''}/${part}`.replace(/\/+/g, '/');
      items.push({
        title: part,
        href: href.startsWith('/') ? href : `/${href}`,
      });
      return items;
    }, []);

  return (
    <PageContainer>
      <ProCard>
        <Breadcrumb
          items={[
            {
              title: <a onClick={() => setPath('/')}>根目录</a>,
            },
            ...crumbs.map((item, index) => ({
              title:
                index === crumbs.length - 1 ? (
                  item.title
                ) : (
                  <a onClick={() => setPath(item.href)}>{item.title}</a>
                ),
            })),
          ]}
        />
        {parentPath ? (
          <Button type="link" onClick={() => setPath(parentPath)}>
            返回上级
          </Button>
        ) : null}
        <ProTable
          rowKey="path"
          search={false}
          pagination={false}
          params={{ path }}
          columns={[
            {
              title: '名称',
              dataIndex: 'name',
              render: (_, record) =>
                record.isDirectory ? (
                  <a onClick={() => setPath(record.path ?? '/')}>
                    {record.name}/
                  </a>
                ) : (
                  record.name
                ),
            },
            {
              title: '类型',
              dataIndex: 'isDirectory',
              render: (_, record) => (record.isDirectory ? '目录' : '文件'),
            },
            { title: '大小', dataIndex: 'length' },
            {
              title: '操作',
              valueType: 'option',
              render: (_, record) =>
                record.isDirectory
                  ? [
                      <a key="open" onClick={() => setPath(record.path ?? '/')}>
                        打开
                      </a>,
                    ]
                  : [
                      <a
                        key="preview"
                        onClick={async () => {
                          const content =
                            await getApiAppVirtualFileExplorerContent({
                              Path: record.path,
                            });
                          setPreview(content);
                          if (content.isBinary) {
                            message.info('二进制文件，仅显示元数据');
                          }
                        }}
                      >
                        预览
                      </a>,
                    ],
            },
          ]}
          request={async () => {
            const result = await getApiAppVirtualFileExplorer({ Path: path });
            setParentPath(result.parentPath);
            return { data: result.items ?? [], success: true };
          }}
        />
      </ProCard>
      <Drawer
        title={preview?.name}
        open={!!preview}
        size={720}
        destroyOnHidden
        onClose={() => setPreview(undefined)}
      >
        <ProDescriptions
          column={1}
          bordered
          dataSource={preview}
          columns={[
            { title: '路径', dataIndex: 'path' },
            { title: '大小', dataIndex: 'length' },
            {
              title: '内容',
              dataIndex: 'content',
              render: (_, record) => {
                if (record.isBinary) {
                  return (
                    <Typography.Text type="secondary">
                      二进制文件不可预览
                    </Typography.Text>
                  );
                }
                if (record.truncated) {
                  return (
                    <Typography.Text type="secondary">
                      文件过大，已跳过预览
                    </Typography.Text>
                  );
                }
                return (
                  <Typography.Paragraph copyable>
                    <Typography.Text code>
                      {record.content || ''}
                    </Typography.Text>
                  </Typography.Paragraph>
                );
              },
            },
          ]}
        />
      </Drawer>
    </PageContainer>
  );
};

export default VirtualFileExplorerPage;
