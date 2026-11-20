import {
  PageContainer,
  ProCard,
  ProDescriptions,
} from '@ant-design/pro-components';
import type { ICellRendererParams } from 'ag-grid-community';
import { AgGridReact } from 'ag-grid-react';
import { App, Breadcrumb, Button, Drawer, Typography } from 'antd';
import React, { useCallback, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import {
  getVirtualFileContent,
  queryVirtualFiles,
  type VirtualFileContentDto,
  type VirtualFileItemDto,
} from '@/abp/virtualFileExplorer';
import {
  type RemoteColumn,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const VirtualFileExplorerPage: React.FC = () => {
  const { message } = App.useApp();
  const [path, setPath] = useState('/');
  const parentPath =
    path === '/' ? undefined : path.slice(0, path.lastIndexOf('/')) || '/';
  const [preview, setPreview] = useState<VirtualFileContentDto>();
  const query = useCallback(
    (input: GridQuery, signal: AbortSignal) =>
      queryVirtualFiles(path, input, signal),
    [path],
  );
  const columns: RemoteColumn<VirtualFileItemDto>[] = [
    {
      field: 'name',
      headerName: '名称',
      minWidth: 210,
      flex: 1,
      cellRenderer: ({ data }: ICellRendererParams<VirtualFileItemDto>) =>
        data?.isDirectory ? (
          <Button type="link" onClick={() => setPath(data.path)}>
            {data.name}/
          </Button>
        ) : (
          data?.name
        ),
    },
    {
      field: 'isDirectory',
      headerName: '类型',
      kind: 'boolean',
      booleanLabels: { true: '目录', false: '文件' },
      valueFormatter: ({ value }) => (value ? '目录' : '文件'),
    },
    { field: 'length', headerName: '大小', kind: 'number' },
    {
      colId: 'actions',
      headerName: '操作',
      kind: 'display',
      width: 110,
      cellRenderer: ({ data }: ICellRendererParams<VirtualFileItemDto>) =>
        data ? (
          <Button
            type="link"
            onClick={async () => {
              if (data.isDirectory) {
                setPath(data.path);
                return;
              }
              const content = await getVirtualFileContent(data.path);
              setPreview(content);
              if (content.isBinary) message.info('二进制文件，仅显示元数据');
            }}
          >
            {data.isDirectory ? '打开' : '预览'}
          </Button>
        ) : null,
    },
  ];
  const { gridProps, searchBox } = useSearchableRemoteGrid({
    columnDefs: columns,
    query,
    getRowId: ({ data }) => data.path,
    ariaLabel: '当前目录',
    search: { placeholder: '搜索当前目录的名称或路径' },
  });
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
        <div style={{ marginBlock: 12 }}>{searchBox}</div>
        <AgGridReact {...gridProps} />
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
