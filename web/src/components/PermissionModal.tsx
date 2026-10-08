import { App, Modal, Spin } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useState } from 'react';
import TreePanel from '@/components/TreePanel';
import {
  getPermissions,
  type PermissionGrantInfo,
  type PermissionGroup,
  updatePermissions,
} from '@/abp/permissions';

type PermissionModalProps = {
  open: boolean;
  title: string;
  providerName: string;
  providerKey?: string;
  onClose: () => void;
};

function buildPermissionTree(permissions: PermissionGrantInfo[]): DataNode[] {
  const nodes = new Map<string, DataNode>();
  for (const item of permissions) {
    nodes.set(item.name, {
      key: item.name,
      title: item.displayName,
      children: [],
      disableCheckbox: item.isEditable === false,
    });
  }

  const roots: DataNode[] = [];
  for (const item of permissions) {
    const node = nodes.get(item.name)!;
    const parent = item.parentName ? nodes.get(item.parentName) : undefined;
    if (parent) {
      parent.children = parent.children || [];
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

const PermissionModal: React.FC<PermissionModalProps> = ({
  open,
  title,
  providerName,
  providerKey,
  onClose,
}) => {
  const { message } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [groups, setGroups] = useState<PermissionGroup[]>([]);
  const [checkedKeys, setCheckedKeys] = useState<string[]>([]);

  useEffect(() => {
    if (!open || !providerKey) {
      return;
    }
    setLoading(true);
    getPermissions(providerName, providerKey)
      .then((result) => {
        setGroups(result.groups || []);
        const granted = (result.groups || []).flatMap((group) =>
          (group.permissions || [])
            .filter((item) => item.isGranted)
            .map((item) => item.name),
        );
        setCheckedKeys(granted);
      })
      .catch(() => message.error('加载权限失败'))
      .finally(() => setLoading(false));
  }, [open, providerName, providerKey, message]);

  const treeData = useMemo<DataNode[]>(
    () =>
      groups.map((group) => ({
        key: `group:${group.name}`,
        title: group.displayName,
        children: buildPermissionTree(group.permissions || []),
      })),
    [groups],
  );

  const allPermissionNames = useMemo(
    () =>
      groups.flatMap((group) =>
        (group.permissions || []).map((item) => item.name),
      ),
    [groups],
  );

  // isEditable=false 的项（disableCheckbox）不可交互修改：全选/清空必须保持其加载时的
  // 授予状态，否则工具条按钮会绕过复选框守卫、把不可编辑项的授权一并改掉
  const editableNames = useMemo(
    () =>
      new Set(
        groups.flatMap((group) =>
          (group.permissions || [])
            .filter((item) => item.isEditable !== false)
            .map((item) => item.name),
        ),
      ),
    [groups],
  );

  const lockedGrantedNames = useMemo(
    () =>
      new Set(
        groups.flatMap((group) =>
          (group.permissions || [])
            .filter((item) => item.isEditable === false && item.isGranted)
            .map((item) => item.name),
        ),
      ),
    [groups],
  );

  const save = async () => {
    if (!providerKey) {
      return;
    }
    setSaving(true);
    try {
      const granted = new Set(checkedKeys);
      await updatePermissions(
        providerName,
        providerKey,
        allPermissionNames.map((name) => ({
          name,
          isGranted: granted.has(name),
        })),
      );
      message.success('权限已保存');
      onClose();
    } catch {
      message.error('保存权限失败');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onClose}
      onOk={save}
      confirmLoading={saving}
      width={640}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        <TreePanel
          checkable
          expandAllOnDataReady
          checkedKeys={checkedKeys}
          treeData={treeData}
          onCheck={(keys) => {
            setCheckedKeys(
              keys.map(String).filter((key) => !key.startsWith('group:')),
            );
          }}
          onCheckAll={(allKeys) => {
            // 全选只覆盖可编辑项；不可编辑项保持加载时的授予状态
            setCheckedKeys((prev) => [
              ...prev.filter((name) => !editableNames.has(name)),
              ...allKeys
                .map(String)
                .filter(
                  (key) =>
                    !key.startsWith('group:') && editableNames.has(key),
                ),
            ]);
          }}
          onClearAll={() => setCheckedKeys([...lockedGrantedNames])}
        />
      </Spin>
    </Modal>
  );
};

export default PermissionModal;
