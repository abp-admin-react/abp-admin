import { App, Modal, Spin, Tree } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useState } from 'react';
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
        <Tree
          checkable
          defaultExpandAll
          checkedKeys={checkedKeys}
          treeData={treeData}
          onCheck={(keys) => {
            const next = Array.isArray(keys) ? keys : keys.checked;
            setCheckedKeys(
              (next as string[]).filter((key) => !key.startsWith('group:')),
            );
          }}
        />
      </Spin>
    </Modal>
  );
};

export default PermissionModal;
