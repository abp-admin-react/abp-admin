import { Button, Space } from 'antd';
import type { TreeProps } from 'antd';
import { Tree } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useState } from 'react';

export type TreePanelProps = Omit<TreeProps, 'height' | 'expandedKeys'> & {
  /** 树区高度（px），默认 420——树在框内滚动，弹窗整体高度稳定（Admin.NET/芋道同款布局） */
  height?: number;
  /** treeData 异步到达后默认全展开（antd defaultExpandAll 对异步数据不生效，须受控重置） */
  defaultExpandAll?: boolean;
  /** 工具条右侧附加区（如数据范围 OU 树的「父子联动」开关） */
  toolbarExtra?: React.ReactNode;
  /** 全选语义由调用方定义（可选叶子集/过滤前缀等），不传则不显示该按钮 */
  onCheckAll?: () => void;
  /** 清空语义同上 */
  onClearAll?: () => void;
};

function collectKeys(nodes: DataNode[], acc: React.Key[] = []): React.Key[] {
  for (const node of nodes) {
    acc.push(node.key);
    if (node.children?.length) collectKeys(node.children, acc);
  }
  return acc;
}

/**
 * 带工具条的勾选树面板（授权类弹窗共用基建，借鉴 Admin.NET/芋道的树工具条）：
 * 全选/清空 + 展开/折叠 + 定高滚动。勾选状态（checkedKeys/onCheck/checkStrictly）
 * 完全由调用方受控，展开状态内部管理——调用方只声明业务语义。
 */
const TreePanel: React.FC<TreePanelProps> = ({
  height = 420,
  defaultExpandAll,
  toolbarExtra,
  onCheckAll,
  onClearAll,
  treeData,
  ...treeRest
}) => {
  const allKeys = useMemo(
    () => collectKeys((treeData as DataNode[]) ?? []),
    [treeData],
  );
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>(
    () => (defaultExpandAll ? allKeys : []),
  );

  useEffect(() => {
    if (defaultExpandAll) {
      setExpandedKeys(allKeys);
    }
  }, [allKeys, defaultExpandAll]);

  const allExpanded =
    allKeys.length > 0 && expandedKeys.length >= allKeys.length;

  return (
    <div
      style={{
        border: '1px solid rgba(0,0,0,0.06)',
        borderRadius: 8,
        padding: 8,
      }}
    >
      <Space size={4} style={{ marginBottom: 6 }}>
        {onCheckAll && (
          <Button size="small" type="link" onClick={onCheckAll}>
            全选
          </Button>
        )}
        {onClearAll && (
          <Button size="small" type="link" onClick={onClearAll}>
            清空
          </Button>
        )}
        <Button
          size="small"
          type="link"
          onClick={() => setExpandedKeys(allExpanded ? [] : allKeys)}
        >
          {allExpanded ? '折叠全部' : '展开全部'}
        </Button>
        {toolbarExtra && <span style={{ marginLeft: 8 }}>{toolbarExtra}</span>}
      </Space>
      <div style={{ height, overflow: 'auto' }}>
        <Tree treeData={treeData} expandedKeys={expandedKeys} {...treeRest} />
      </div>
    </div>
  );
};

export default TreePanel;
