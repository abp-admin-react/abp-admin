import { FolderOutlined } from '@ant-design/icons';
import type { ProColumns } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import { Popconfirm, Space } from 'antd';
import React from 'react';
import { FileType } from '@/services/fileManagement';
import FileThumbnailCell from './FileThumbnailCell';
import RenameAction from './RenameAction';
import ShareAction from './ShareAction';
import type { FileRow } from './useFileActions';
import { formatByteSize } from './utils';

/** 操作列/各列渲染所需的权限与回调集合 */
export type FileColumnsContext = {
  canDownload: boolean;
  canUpdate: boolean;
  canMove: boolean;
  canDelete: boolean;
  onDownload: (record: API.FileInfoDto) => void;
  onPreview: (record: FileRow) => void;
  onRename: (file: API.FileInfoDto, fileName: string) => Promise<void>;
  onDelete: (record: API.FileInfoDto) => Promise<void>;
  onMove: (record: API.FileInfoDto) => void;
  onReload: () => void;
  onEnterDirectory: (record: API.FileInfoDto) => void;
};

/**
 * 文件表格列定义（含操作列）。从 index.tsx 拆出（问题 7）：
 * 分享/重命名等操作组件在对应文件里，这里只做装配与权限裁剪。
 */
export function useFileColumns(ctx: FileColumnsContext): ProColumns<FileRow>[] {
  const intl = useIntl();
  const {
    canDownload,
    canUpdate,
    canMove,
    canDelete,
    onDownload,
    onPreview,
    onRename,
    onDelete,
    onMove,
    onReload,
    onEnterDirectory,
  } = ctx;

  return [
    {
      title: intl.formatMessage({ id: 'pages.files.columns.preview' }),
      dataIndex: 'thumbnailUrl',
      width: 64,
      search: false,
      render: (_, record) =>
        record.fileType === FileType.Directory ? null : (
          <FileThumbnailCell
            thumbnailUrl={record.thumbnailUrl}
            fileName={record.fileName}
            onPreview={() => onPreview(record)}
          />
        ),
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.name' }),
      dataIndex: 'fileName',
      render: (_, record) =>
        record.fileType === FileType.Directory ? (
          <Space>
            <FolderOutlined />
            <a onClick={() => onEnterDirectory(record)}>{record.fileName}</a>
          </Space>
        ) : (
          record.fileName
        ),
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.type' }),
      dataIndex: 'fileType',
      search: false,
      width: 90,
      render: (_, record) =>
        record.fileType === FileType.Directory
          ? intl.formatMessage({ id: 'pages.files.type.directory' })
          : record.mimeType ||
            intl.formatMessage({ id: 'pages.files.type.file' }),
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.size' }),
      dataIndex: 'byteSize',
      search: false,
      width: 110,
      render: (_, record) =>
        record.fileType === FileType.Directory
          ? '-'
          : formatByteSize(record.byteSize),
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.subFiles' }),
      dataIndex: 'subFilesQuantity',
      search: false,
      width: 70,
      render: (_, record) =>
        record.fileType === FileType.Directory
          ? (record.subFilesQuantity ?? 0)
          : '-',
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.creationTime' }),
      dataIndex: 'creationTime',
      search: false,
      valueType: 'dateTime',
      width: 170,
    },
    {
      title: intl.formatMessage({ id: 'pages.files.columns.actions' }),
      valueType: 'option',
      width: 240,
      render: (_, record) => {
        const ops: React.ReactNode[] = [];
        if (record.fileType !== FileType.Directory && canDownload) {
          ops.push(
            <a key="download" onClick={() => onDownload(record)}>
              {intl.formatMessage({ id: 'pages.files.actions.download' })}
            </a>,
            <ShareAction key="share" file={record} onShared={onReload} />,
          );
        }
        if (canUpdate) {
          ops.push(
            <RenameAction
              key="rename"
              file={record}
              onRenamed={onReload}
              onRename={onRename}
            />,
          );
        }
        if (canMove) {
          ops.push(
            <a key="move" onClick={() => onMove(record)}>
              {intl.formatMessage({ id: 'pages.files.actions.move' })}
            </a>,
          );
        }
        if (canDelete) {
          ops.push(
            <Popconfirm
              key="delete"
              title={intl.formatMessage(
                {
                  id:
                    record.fileType === FileType.Directory
                      ? 'pages.files.delete.confirmDir'
                      : 'pages.files.delete.confirmFile',
                },
                { name: record.fileName },
              )}
              onConfirm={() => onDelete(record).then(onReload)}
            >
              <a style={{ color: '#ff4d4f' }}>
                {intl.formatMessage({ id: 'pages.files.actions.delete' })}
              </a>
            </Popconfirm>,
          );
        }
        return ops;
      },
    },
  ];
}
