import { FolderOpenOutlined } from '@ant-design/icons';
import { useIntl } from '@umijs/max';
import { Button, Tree } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { getFileList } from '@/services/fileManagement';
import type { DirNode } from './utils';
import { toDirNode, updateTreeData } from './utils';

/**
 * 左侧目录树面板：根目录一次加载 + 逐节点懒加载，选中/回根交给父级。
 * （files 页拆分自 index.tsx，见 docs/refactor/advisor/files.md 问题 7）
 */
const DirectoryTreePanel: React.FC<{
  currentDirId: string | undefined;
  onSelect: (dirId: string | undefined, dirName: string) => void;
}> = ({ currentDirId, onSelect }) => {
  const intl = useIntl();
  const [treeData, setTreeData] = useState<DirNode[]>([]);
  const rootLabel = intl.formatMessage({ id: 'pages.files.rootDir' });

  // 加载根目录
  useEffect(() => {
    getFileList({ directoryOnly: true, maxResultCount: 1000 })
      .then((res) => setTreeData((res.items || []).map(toDirNode)))
      .catch(() => undefined);
  }, []);

  // 目录树懒加载
  const onLoadData = useCallback((node: DirNode): Promise<void> => {
    if (node.children && node.children.length > 0) {
      return Promise.resolve();
    }
    return getFileList({
      parentId: node.key,
      directoryOnly: true,
      maxResultCount: 1000,
    })
      .then((res) => {
        const children = (res.items || []).map(toDirNode);
        setTreeData((origin) => updateTreeData(origin, node.key, children));
      })
      .catch(() => undefined);
  }, []);

  return (
    <>
      <Tree
        showIcon
        blockNode
        treeData={treeData}
        loadData={onLoadData}
        selectedKeys={currentDirId ? [currentDirId] : []}
        icon={<FolderOpenOutlined />}
        titleRender={(node) => <span>{node.title as React.ReactNode}</span>}
        onSelect={(keys, info) => {
          if (keys.length === 0) {
            // 点空白处回根目录
            onSelect(undefined, rootLabel);
          } else {
            onSelect(String(keys[0]), String(info.node.title || ''));
          }
        }}
      />
      <Button
        type="link"
        size="small"
        onClick={() => onSelect(undefined, rootLabel)}
      >
        {intl.formatMessage({ id: 'pages.files.tree.backToRoot' })}
      </Button>
    </>
  );
};

export default DirectoryTreePanel;
