import { UploadOutlined } from '@ant-design/icons';
import {
  ModalForm,
  PageContainer,
  ProCard,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess, useIntl } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Image,
  Upload,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import { queryFiles } from '@/abp/imaging';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import {
  createDirectory,
  FILE_CONTAINER_NAME,
  FileType,
  getFileConfiguration,
  uploadFile,
} from '@/services/fileManagement';
import DirectoryTreePanel from './DirectoryTreePanel';
import MoveModal from './MoveModal';
import { type FileRow, useFileActions } from './useFileActions';
import { useFileColumns } from './useFileColumns';

/**
 * 文件管理页：目录树 + 文件列表 + 上传/下载/分享/重命名/移动/删除。
 * 各职责的实现在同目录的拆分文件里（问题 7 重构）：
 * - 目录树：DirectoryTreePanel.tsx；列定义：useFileColumns.tsx；纯函数：utils.ts
 * - 操作与提示：useFileActions.ts
 * - 分享（创建/管理/撤销）：ShareAction.tsx；重命名：RenameAction.tsx；移动：MoveModal.tsx
 * 文案全部走 locales（问题 8，key 前缀 pages.files.）。
 */
const FilesPage: React.FC = () => {
  const access = useAccess();
  const intl = useIntl();
  const { message } = App.useApp();

  // 当前选中目录（undefined = 根目录）
  const [currentDirId, setCurrentDirId] = useState<string | undefined>();
  const [currentDirName, setCurrentDirName] = useState<string>(
    intl.formatMessage({ id: 'pages.files.rootDir' }),
  );
  const [config, setConfig] = useState<API.PublicFileContainerConfiguration>();
  // 移动目标
  const [moveTarget, setMoveTarget] = useState<API.FileInfoDto>();
  const [treeVersion, setTreeVersion] = useState(0);
  const canCreate = !!access.canCreateFile;
  // 分享与原图下载同权，缩略图沿用列表读取权限。
  const canDownload = !!access.canGetFileDownloadInfo;

  const {
    previewSrc,
    closePreview,
    handleDownload,
    handlePreview,
    handleRename,
    handleDelete,
    handleMove,
    buildBeforeUpload,
  } = useFileActions(canDownload);

  // 加载容器配置（上传预校验用）
  useEffect(() => {
    if (!canCreate) return;
    getFileConfiguration()
      .then(setConfig)
      .catch(() => undefined);
  }, [canCreate]);

  const reload = useCallback(() => {
    grid.refresh();
  }, []);

  const columns = useFileColumns({
    canDownload,
    canUpdate: !!access.canUpdateFile,
    canMove: !!access.canMoveFile,
    canDelete: !!access.canDeleteFile,
    onDownload: handleDownload,
    onPreview: handlePreview,
    onRename: async (record, name) => {
      await handleRename(record, name);
      if (record.fileType === FileType.Directory) setTreeVersion((v) => v + 1);
    },
    onDelete: async (record) => {
      await handleDelete(record);
      if (record.fileType === FileType.Directory) setTreeVersion((v) => v + 1);
    },
    onMove: setMoveTarget,
    onReload: reload,
    onEnterDirectory: (record) => {
      setCurrentDirId(record.id);
      setCurrentDirName(record.fileName || '');
    },
  });

  const grid = useSearchableRemoteGrid<FileRow>({
    columnDefs: toGridColumns<FileRow>(columns, {
      pro: true,
      kinds: {
        thumbnailUrl: 'display',
        fileType: 'number',
        subFilesQuantity: 'number',
        byteSize: 'number',
      },
    }),
    ariaLabel: '文件',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        queryFiles(FILE_CONTAINER_NAME, currentDirId, input, signal),
      [currentDirId],
    ),
  });

  return (
    <PageContainer>
      <ProCard gutter={12} split="vertical">
        {/* 左侧目录树 */}
        <ProCard
          colSpan="260px"
          title={intl.formatMessage({ id: 'pages.files.tree.title' })}
          headerBordered
        >
          <DirectoryTreePanel
            key={treeVersion}
            currentDirId={currentDirId}
            onSelect={(dirId, dirName) => {
              setCurrentDirId(dirId);
              setCurrentDirName(dirName);
            }}
          />
        </ProCard>

        {/* 右侧文件列表 */}
        <ProCard
          title={intl.formatMessage(
            { id: 'pages.files.currentLocation' },
            { name: currentDirName },
          )}
          headerBordered
        >
          <GridControls wrap style={{ marginBottom: 8 }}>
            {grid.searchBox}
            {grid.hasFilters && (
              <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
            )}{' '}
            {(() => {
              const tools: React.ReactNode[] = [];
              if (canCreate) {
                tools.push(
                  <ModalForm
                    key="mkdir"
                    title={intl.formatMessage({
                      id: 'pages.files.toolbar.newDirectory',
                    })}
                    trigger={
                      <Button>
                        {intl.formatMessage({
                          id: 'pages.files.toolbar.newDirectory',
                        })}
                      </Button>
                    }
                    modalProps={{ destroyOnHidden: true }}
                    width={420}
                    onFinish={async (values: { name?: string }) => {
                      const dirName = values.name?.trim();
                      if (!dirName) {
                        return false;
                      }
                      await createDirectory(dirName, currentDirId);
                      setTreeVersion((v) => v + 1);
                      message.success(
                        intl.formatMessage({
                          id: 'pages.files.toolbar.directoryCreated',
                        }),
                      );
                      reload();
                      return true;
                    }}
                  >
                    <ProFormText
                      name="name"
                      label={intl.formatMessage({
                        id: 'pages.files.toolbar.directoryName',
                      })}
                      rules={[
                        {
                          required: true,
                          message: intl.formatMessage({
                            id: 'pages.files.toolbar.directoryNameRequired',
                          }),
                        },
                      ]}
                    />
                  </ModalForm>,
                  <Upload
                    key="upload"
                    showUploadList={false}
                    beforeUpload={buildBeforeUpload(config)}
                    customRequest={async ({ file, onSuccess, onError }) => {
                      try {
                        await uploadFile(file as File, currentDirId);
                        message.success(
                          intl.formatMessage({
                            id: 'pages.files.upload.success',
                          }),
                        );
                        reload();
                        onSuccess?.({});
                      } catch (error) {
                        message.error(
                          intl.formatMessage({
                            id: 'pages.files.upload.failed',
                          }),
                        );
                        onError?.(error as Error);
                      }
                    }}
                  >
                    <Button type="primary" icon={<UploadOutlined />}>
                      {intl.formatMessage({ id: 'pages.files.toolbar.upload' })}
                    </Button>
                  </Upload>,
                );
              }
              return tools;
            })()}
          </GridControls>
          <AgGridReact<FileRow> {...grid.gridProps} />
        </ProCard>
      </ProCard>

      {/* 移动 Modal */}
      <MoveModal
        target={moveTarget}
        onClose={() => setMoveTarget(undefined)}
        onMove={async (record, parentId, fileName) => {
          await handleMove(record, parentId, fileName);
          if (record.fileType === FileType.Directory)
            setTreeVersion((v) => v + 1);
        }}
        onMoved={reload}
      />

      {/* 原图预览弹窗：点击缩略图打开，src 会从缩略图切换为原图下载地址 */}
      <Image
        style={{ display: 'none' }}
        preview={{
          open: previewSrc !== undefined,
          src: previewSrc,
          onOpenChange: (open) => {
            if (!open) closePreview();
          },
        }}
      />
    </PageContainer>
  );
};

export default FilesPage;
