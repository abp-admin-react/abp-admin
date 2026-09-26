import { UploadOutlined } from '@ant-design/icons';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProCard,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess, useIntl } from '@umijs/max';
import { App, Button, Image, Upload } from 'antd';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { getFileListWithThumbnails } from '@/abp/imaging';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  createDirectory,
  FILE_CONTAINER_NAME,
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
  const actionRef = useRef<ActionType>(undefined);

  // 当前选中目录（undefined = 根目录）
  const [currentDirId, setCurrentDirId] = useState<string | undefined>();
  const [currentDirName, setCurrentDirName] = useState<string>(
    intl.formatMessage({ id: 'pages.files.rootDir' }),
  );
  const [config, setConfig] = useState<API.PublicFileContainerConfiguration>();
  // 移动目标
  const [moveTarget, setMoveTarget] = useState<API.FileInfoDto>();

  const {
    previewSrc,
    setPreviewSrc,
    handleDownload,
    handlePreview,
    handleRename,
    handleDelete,
    handleMove,
    buildBeforeUpload,
  } = useFileActions();

  const canCreate = !!access.canCreateFile;
  // 分享 = 把下载权外包给匿名 token，入口与下载同权限（后端同样要求 GetDownloadInfo）
  const canDownload = !!access.canGetFileDownloadInfo;

  // 加载容器配置（上传预校验用）
  useEffect(() => {
    getFileConfiguration()
      .then(setConfig)
      .catch(() => undefined);
  }, []);

  const reload = useCallback(() => {
    actionRef.current?.reload();
  }, []);

  const columns = useFileColumns({
    canDownload,
    canUpdate: !!access.canUpdateFile,
    canMove: !!access.canMoveFile,
    canDelete: !!access.canDeleteFile,
    onDownload: handleDownload,
    onPreview: handlePreview,
    onRename: handleRename,
    onDelete: handleDelete,
    onMove: setMoveTarget,
    onReload: reload,
    onEnterDirectory: (record) => {
      setCurrentDirId(record.id);
      setCurrentDirName(record.fileName || '');
    },
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
          <AutoHeightProTable<FileRow>
            rowKey="id"
            actionRef={actionRef}
            search={false}
            /* 目录树 + 列表的多面板嵌套布局，不撑满视口，保留拖拽列宽与防撑破 */
            fillViewport={false}
            params={{ parentId: currentDirId }}
            columns={columns}
            request={async (params) => {
              const result = await getFileListWithThumbnails(
                FILE_CONTAINER_NAME,
                {
                  parentId: params.parentId,
                  sorting: 'FileName',
                  skipCount:
                    ((params.current || 1) - 1) * (params.pageSize || 10),
                  maxResultCount: params.pageSize || 10,
                },
              );
              const thumbnailUrls = result.thumbnailUrls || {};
              return {
                data: (result.items || []).map((item) => ({
                  ...item,
                  thumbnailUrl: item.id ? thumbnailUrls[item.id] : undefined,
                })),
                total: result.totalCount || 0,
                success: true,
              };
            }}
            toolBarRender={() => {
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
            }}
          />
        </ProCard>
      </ProCard>

      {/* 移动 Modal */}
      <MoveModal
        target={moveTarget}
        onClose={() => setMoveTarget(undefined)}
        onMove={handleMove}
        onMoved={reload}
      />

      {/* 原图预览弹窗：点击缩略图打开，src 会从缩略图切换为原图下载地址 */}
      <Image
        style={{ display: 'none' }}
        preview={{
          open: previewSrc !== undefined,
          src: previewSrc,
          onOpenChange: (open) => {
            if (!open) setPreviewSrc(undefined);
          },
        }}
      />
    </PageContainer>
  );
};

export default FilesPage;
