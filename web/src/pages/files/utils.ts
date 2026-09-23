import type { TreeDataNode } from 'antd';

/** 目录树节点（files 页专用）：Tree 节点 + 懒加载标记 */
export type DirNode = TreeDataNode & {
  key: string;
  children?: DirNode[];
  isLeaf?: boolean;
};

/** 把目录 FileInfoDto 转成 Tree 节点 */
export function toDirNode(item: API.FileInfoDto): DirNode {
  return {
    key: item.id || '',
    title: item.fileName,
    isLeaf: !item.hasSubdirectories,
    children: item.hasSubdirectories ? [] : undefined,
  };
}

/** 从容器配置里解析允许的扩展名（key 形如 ".txt"） */
export function parseAllowedExtensions(
  config?: API.PublicFileContainerConfiguration,
): string[] {
  if (!config?.fileExtensionsConfiguration) return [];
  return Object.keys(config.fileExtensionsConfiguration).map((e) =>
    e.toLowerCase(),
  );
}

/** 更新树中某个节点的 children（不可变） */
export function updateTreeData(
  list: DirNode[],
  key: string,
  children: DirNode[],
): DirNode[] {
  return list.map((node) => {
    if (node.key === key) {
      return { ...node, children };
    }
    if (node.children) {
      return {
        ...node,
        children: updateTreeData(node.children, key, children),
      };
    }
    return node;
  });
}

/** 字节数人性化展示 */
export function formatByteSize(size?: number): string {
  if (size == null) return '-';
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  if (size < 1024 * 1024 * 1024) return `${(size / 1024 / 1024).toFixed(1)} MB`;
  return `${(size / 1024 / 1024 / 1024).toFixed(2)} GB`;
}
