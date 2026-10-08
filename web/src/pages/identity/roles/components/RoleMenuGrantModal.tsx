import { ModalForm } from '@ant-design/pro-components';
import { App, Spin, Tag } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useState } from 'react';
import type { IdentityRoleDto } from '@/abp/identity';
import {
  getRoleMenuGrantView,
  type RoleMenuGrantItemDto,
  updateRoleMenuGrantSet,
} from '@/abp/menus';
import { isNotFound } from '@/requestErrorConfig';
import TreePanel from '@/components/TreePanel';

type RoleMenuGrantModalProps = {
  role: IdentityRoleDto | undefined;
  onClose: () => void;
};

type TreeNodeMeta = {
  isControlled: boolean;
  isEnabled: boolean;
  isHide: boolean;
};

/** 平铺视图组树：返回树节点与元数据（公开/受限、启停、隐藏）映射，key=菜单 id。 */
function buildTree(
  items: RoleMenuGrantItemDto[],
): [DataNode[], Map<React.Key, TreeNodeMeta>] {
  const meta = new Map<React.Key, TreeNodeMeta>();
  const nodes = new Map<string, DataNode>();
  for (const item of items) {
    nodes.set(item.id, { key: item.id, title: item.title, children: [] });
    meta.set(item.id, {
      isControlled: item.isControlled,
      isEnabled: item.isEnabled,
      isHide: item.isHide,
    });
  }

  const roots: DataNode[] = [];
  for (const item of items) {
    const node = nodes.get(item.id)!;
    const parent = item.parentId ? nodes.get(item.parentId) : undefined;
    if (parent) {
      parent.children = parent.children || [];
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return [roots, meta];
}

/** 节点标题：显示名 + 受控徽标；停用/隐藏节点弱化——授权仍可配置（停用菜单重新启用后生效），但当前对用户不可见。 */
function renderTitle(
  title: DataNode['title'],
  key: React.Key,
  meta: Map<React.Key, TreeNodeMeta>,
): React.ReactNode {
  const label =
    typeof title === 'function' ? title({ key } as DataNode) : title;
  const m = meta.get(key);
  if (!m) return label;
  const invisible = !m.isEnabled || m.isHide;
  return (
    <span
      style={{
        opacity: invisible ? 0.45 : 1,
        textDecoration: m.isEnabled ? 'none' : 'line-through',
      }}
    >
      {label}
      {m.isHide ? (
        <Tag style={{ marginLeft: 8 }}>隐藏</Tag>
      ) : null}
      {m.isControlled ? (
        <Tag color="geekblue" style={{ marginLeft: 8 }}>
          受限
        </Tag>
      ) : (
        <Tag style={{ marginLeft: 8 }}>公开</Tag>
      )}
    </span>
  );
}

/**
 * 角色侧「菜单权限」弹窗（角色→菜单树勾选，Admin.NET/芋道同款交互方向）。
 * 语义适配本系统的混合授权模型（与纯授权模型的根本差异，见说明文案）：
 * - 勾选=为该角色显式授权；勾掉=撤销该角色授权——差集只动这一个角色；
 * - 「公开」节点未受任何角色控制，对所有用户可见（满足权限时），勾不勾都不影响其可见性；
 * - 「受限」节点仅授权角色可见——要让某菜单开始受限，去菜单页「分配角色」或在此勾选任意角色。
 * 勾选为逐节点精确集合（父子独立，与菜单可见性算法同语义）。
 */
const RoleMenuGrantModal: React.FC<RoleMenuGrantModalProps> = ({
  role,
  onClose,
}) => {
  const { message } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [items, setItems] = useState<RoleMenuGrantItemDto[]>([]);
  const [checkedKeys, setCheckedKeys] = useState<React.Key[]>([]);

  useEffect(() => {
    if (!role) return;
    let cancelled = false;
    setLoading(true);
    setCheckedKeys([]);
    getRoleMenuGrantView(role.id)
      .then((res) => {
        if (cancelled) return;
        setItems(res.items);
        setCheckedKeys(
          res.items.filter((x) => x.isGranted).map((x) => x.id),
        );
      })
      .catch(() => {
        if (cancelled) return;
        // 读取失败关闭弹窗（防"空勾选 + 全量覆盖保存"静默清空该角色全部授权）
        setItems([]);
        onClose();
        message.error('读取角色菜单授权失败，请重试');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [role?.id]);

  const [treeData, nodeMeta] = React.useMemo(
    () => buildTree(items),
    [items],
  );

  const decoratedTree = React.useMemo(
    () =>
      (function decorate(nodes: DataNode[]): DataNode[] {
        return nodes.map((n) => ({
          ...n,
          title: renderTitle(n.title, n.key, nodeMeta),
          children: n.children?.length ? decorate(n.children) : [],
        }));
      })(treeData),
    [treeData, nodeMeta],
  );

  const save = async () => {
    if (!role) return;
    setSaving(true);
    try {
      await updateRoleMenuGrantSet(
        role.id,
        checkedKeys.map((k) => String(k)),
      );
      message.success('角色菜单授权已保存');
      onClose();
    } catch (err) {
      // 404 = 角色刚在别处被删除（全局层对 404 静默，页面层按契约自行提示）——关弹窗防继续编辑幽灵角色
      if (isNotFound(err)) {
        message.error('该角色已不存在，弹窗已关闭');
        onClose();
        return;
      }
      message.error('保存角色菜单授权失败');
    } finally {
      setSaving(false);
    }
  };

  return (
    <ModalForm
      title={`菜单权限 - ${role?.name ?? ''}`}
      width={560}
      open={!!role}
      key={role?.id}
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      onFinish={save}
      submitter={{ searchConfig: { submitText: '保存' }, submitButtonProps: { loading: saving } }}
    >
      <div style={{ marginBottom: 8, color: 'rgba(0,0,0,0.45)' }}>
        勾选=为该角色显式授权可见，勾掉=撤销该角色的授权（只影响这一个角色）。标注「公开」的菜单未受任何角色控制、对所有用户可见，不受本次保存影响；「受限」菜单仅授权角色可见；停用/隐藏的菜单当前对用户不可见，但授权会保留、恢复后生效。
      </div>
      <Spin spinning={loading}>
        <TreePanel
          checkable
          checkStrictly
          expandAllOnDataReady
          treeData={decoratedTree}
          checkedKeys={checkedKeys}
          onCheck={setCheckedKeys}
          onCheckAll={setCheckedKeys}
          onClearAll={() => setCheckedKeys([])}
        />
      </Spin>
    </ModalForm>
  );
};

export default RoleMenuGrantModal;
