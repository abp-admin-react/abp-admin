import { useIntl } from '@umijs/max';
import { App } from 'antd';
import { useCallback, useState } from 'react';
import {
  deleteFile,
  getDownloadInfo,
  moveFile,
  renameFile,
} from '@/services/fileManagement';
import { parseAllowedExtensions } from './utils';

/** 文件行：FileInfoDto + 后端下发的缩略图 URL（仅图片且生成完成时存在） */
export type FileRow = API.FileInfoDto & { thumbnailUrl?: string };

/**
 * 文件操作集合（下载/预览/重命名/删除/移动/上传预校验）与对应的 message 提示。
 * 组件只管装配，文案与交互语义集中在这里，便于统一走 intl。
 */
export function useFileActions() {
  const { message } = App.useApp();
  const intl = useIntl();
  // 原图预览（点击缩略图打开；先用缩略图秒开，拿到下载凭证后替换成原图）
  const [previewSrc, setPreviewSrc] = useState<string | undefined>();

  // 两步下载：先拿 downloadUrl（EasyAbp 5.x 已把 token 拼进 URL），再开新窗口下载
  const handleDownload = useCallback(
    async (record: API.FileInfoDto) => {
      if (!record.id) return;
      try {
        const info = await getDownloadInfo(record.id);
        if (!info.downloadUrl) {
          message.error(
            intl.formatMessage({ id: 'pages.files.download.noUrl' }),
          );
          return;
        }
        window.open(info.downloadUrl, '_blank');
      } catch {
        message.error(
          intl.formatMessage({ id: 'pages.files.download.failed' }),
        );
      }
    },
    [intl, message],
  );

  // 点击缩略图：先以缩略图打开预览弹窗，同时取原图下载凭证，拿到后切换成原图
  const handlePreview = useCallback(async (record: FileRow) => {
    if (!record.id || !record.thumbnailUrl) return;
    setPreviewSrc(record.thumbnailUrl);
    try {
      const info = await getDownloadInfo(record.id);
      // EasyAbp 5.x 的 downloadUrl 自带 ?token=，不能再拼一次（重复参数会绑定成数组导致校验失败）
      if (info.downloadUrl) {
        setPreviewSrc(info.downloadUrl);
      }
    } catch {
      // 拿不到原图凭证时保留缩略图预览
    }
  }, []);

  const handleRename = useCallback(
    async (record: API.FileInfoDto, fileName: string) => {
      if (!record.id) return;
      await renameFile(record.id, fileName);
      message.success(intl.formatMessage({ id: 'pages.files.rename.success' }));
    },
    [intl, message],
  );

  const handleDelete = useCallback(
    async (record: API.FileInfoDto) => {
      if (!record.id) return;
      await deleteFile(record.id);
      message.success(intl.formatMessage({ id: 'pages.files.delete.success' }));
    },
    [intl, message],
  );

  const handleMove = useCallback(
    async (
      record: API.FileInfoDto,
      newParentId: string | undefined,
      newFileName: string,
    ) => {
      if (!record.id) return;
      // NewFileName 是 [Required]，不改名也必须传当前文件名
      await moveFile(record.id, newParentId, newFileName);
      message.success(intl.formatMessage({ id: 'pages.files.move.success' }));
    },
    [intl, message],
  );

  // 上传前预校验：大小 + 扩展名
  const buildBeforeUpload = useCallback(
    (config?: API.PublicFileContainerConfiguration) =>
      (file: File): boolean => {
        if (
          config?.maxByteSizeForEachFile &&
          file.size > config.maxByteSizeForEachFile
        ) {
          const mb = (config.maxByteSizeForEachFile / 1024 / 1024).toFixed(1);
          message.error(
            intl.formatMessage({ id: 'pages.files.upload.tooLarge' }, { mb }),
          );
          return false;
        }
        if (config?.allowOnlyConfiguredFileExtensions) {
          const allowed = parseAllowedExtensions(config);
          const dot = file.name.lastIndexOf('.');
          const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
          if (allowed.length > 0 && !allowed.includes(ext)) {
            message.error(
              intl.formatMessage(
                { id: 'pages.files.upload.extNotAllowed' },
                {
                  ext:
                    ext ||
                    intl.formatMessage({
                      id: 'pages.files.upload.noExtension',
                    }),
                  allowed: allowed.join(' '),
                },
              ),
            );
            return false;
          }
        }
        return true;
      },
    [intl, message],
  );

  return {
    previewSrc,
    setPreviewSrc,
    handleDownload,
    handlePreview,
    handleRename,
    handleDelete,
    handleMove,
    buildBeforeUpload,
  };
}
