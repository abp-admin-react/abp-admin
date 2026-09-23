import { FileOutlined } from '@ant-design/icons';
import { Image } from 'antd';
import type React from 'react';

/**
 * 文件列表「预览」列单元格（T3.1）。
 * 有缩略图 URL 显示 40×40 小图，点击交给父级打开原图预览；非图片/缩略图未就绪显示通用图标。
 */
const FileThumbnailCell: React.FC<{
  thumbnailUrl?: string;
  fileName?: string;
  onPreview?: () => void;
}> = ({ thumbnailUrl, fileName, onPreview }) => {
  if (!thumbnailUrl) {
    return <FileOutlined style={{ fontSize: 20, color: '#bfbfbf' }} />;
  }

  return (
    <Image
      src={thumbnailUrl}
      alt={fileName}
      width={40}
      height={40}
      style={{ objectFit: 'cover', borderRadius: 4, cursor: 'pointer' }}
      preview={false}
      onClick={onPreview}
      placeholder
    />
  );
};

export default FileThumbnailCell;
