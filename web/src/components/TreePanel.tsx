import { Button, Space } from 'antd';
import type { TreeProps } from 'antd';
import { Tree } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useState } from 'react';

export type TreePanelProps = Omit<
  TreeProps,
  'height' | 'expandedKeys' | 'onExpand' | 'onCheck'
> & {
  /** 树区高度（px），默认 420——树在框内滚动，弹窗整体高度稳定（Admin.NET/芋道同款布局） */
  height?: number;
  /** 每次 treeData 到达/变化后全展开（受控重置；antd 的 defaultExpandAll 对异步数据不生效，
   *  刻意不复用同名 prop——语义是「数据就绪即全展开」而非「初始展开一次」） */
  expandAllOnDataReady?: boolean;
  /** 工具条右侧附加区（如数据范围 OU 树的「父子联动」开关） */
  toolbarExtra?: React.ReactNode;
  /** 全选：TreePanel 把内部已算好的全量 key 递给调用方（调用方按需过滤，如权限弹窗剔除 group 前缀/不可编辑项） */
  onCheckAll?: (allKeys: React.Key[]) => void;
  /** 清空语义由调用方定义（非可编辑项保留等），不传则不显示该按钮 */
  onClearAll?: () => void;
  /** 勾选变化。TreePanel 统一拆包 antd 的联合返回（checkStrictly 时为 {checked}，否则为数组），调用方只收扁平 key 数组 */
  onCheck?: (checkedKeys: React.Key[]) => void;
};

/** 深度优先收集整棵树全部节点 key（全选/展开折叠共用）。全选的自定义过滤由调用方在 onCheckAll 回调里做。 */
function collectTreeKeys(
  nodes: DataNode[],
  acc: React.Key[] = [],
): React.Key[] {
  for (const node of nodes) {
    acc.push(node.key);
    if (node.children?.length) collectTreeKeys(node.children, acc);
  }
  return acc;
}

/**
 * 带工具条的勾选树面板（授权类弹窗共用基建，借鉴 Admin.NET/芋道的树工具条）：
 * 全选/清空 + 展开/折叠 + 定高滚动。勾选业务语义（过滤/级联策略）由调用方通过
 * onCheck/onCheckAll 声明；展开状态内部管理（含节点级点击展开——expandedKeys 受控
 * 时必须内部回写 onExpand，否则节点箭头点击不生效）。
 */
const TreePanel: React.FC<TreePanelProps> = ({
  height = 420,
  expandAllOnDataReady,
  toolbarExtra,
  onCheckAll,
  onClearAll,
  onCheck,
  treeData,
  ...treeRest
}) => {
  const allKeys = useMemo(
    () => collectTreeKeys((treeData as DataNode[]) ?? []),
    [treeData],
  );
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>(
    () => (expandAllOnDataReady ? allKeys : []),
  );

  useEffect(() => {
    if (expandAllOnDataReady) {
      setExpandedKeys(allKeys);
    }
  }, [allKeys, expandAllOnDataReady]);

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
          <Button size="small" type="link" onClick={() => onCheckAll(allKeys)}>
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
        <Tree
          treeData={treeData}
          expandedKeys={expandedKeys}
          onExpand={(keys) => setExpandedKeys(keys)}
          onCheck={(keys) => {
            // antd 联合返回：checkStrictly=true 时为 {checked}，否则为数组——在此统一拆包，
            // 调用方只处理扁平 key 数组（此前三个消费方各自重复这段拆包）
            const next = Array.isArray(keys) ? keys : keys.checked;
            onCheck?.(next);
          }}
          {...treeRest}
        />
      </div>
    </div>
  );
};

export default TreePanel;
