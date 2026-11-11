import { Alert, App, Empty, Modal, Spin, Tabs } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useRef, useState } from 'react';
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
    const node = nodes.get(item.name);
    if (!node) continue;
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
  const [readFailed, setReadFailed] = useState(false);
  const [loadedTarget, setLoadedTarget] = useState<{
    providerName: string;
    providerKey: string;
  }>();
  const targetVersion = useRef(0);
  const [groups, setGroups] = useState<PermissionGroup[]>([]);
  const [checkedKeys, setCheckedKeys] = useState<string[]>([]);

  useEffect(() => {
    const version = ++targetVersion.current;
    let cancelled = false;
    setGroups([]);
    setCheckedKeys([]);
    setLoadedTarget(undefined);
    setReadFailed(false);
    setSaving(false);
    setLoading(false);
    if (!open || !providerKey) {
      return;
    }
    setLoading(true);
    getPermissions(providerName, providerKey)
      .then((result) => {
        if (cancelled) return;
        const nextGroups = (result.groups || []).filter(
          (group) => group.permissions?.length,
        );
        setGroups(nextGroups);
        const granted = nextGroups.flatMap((group) =>
          (group.permissions || [])
            .filter((item) => item.isGranted)
            .map((item) => item.name),
        );
        setCheckedKeys(granted);
        setLoadedTarget({ providerName, providerKey });
      })
      .catch(() => {
        if (!cancelled) setReadFailed(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
      if (targetVersion.current === version) ++targetVersion.current;
    };
  }, [open, providerName, providerKey, message]);

  const permissions = useMemo(
    () => groups.flatMap((group) => group.permissions),
    [groups],
  );
  const groupsWithTrees = useMemo(
    () =>
      groups.map((group) => ({
        group,
        treeData: buildPermissionTree(group.permissions),
      })),
    [groups],
  );
  const grantedNames = useMemo(() => new Set(checkedKeys), [checkedKeys]);
  const loaded =
    loadedTarget?.providerName === providerName &&
    loadedTarget?.providerKey === providerKey;
  const canSave =
    open &&
    !!providerKey &&
    loaded &&
    !loading &&
    !saving &&
    permissions.length > 0;

  const setGroupChecks = (group: PermissionGroup, keys: React.Key[]) => {
    if (!canSave) return;
    const editable = new Set(
      group.permissions
        .filter((permission) => permission.isEditable !== false)
        .map((permission) => permission.name),
    );
    // 只替换当前组可编辑的勾选，其他组与锁定项始终保留。
    setCheckedKeys((previous) => [
      ...previous.filter((name) => !editable.has(name)),
      ...keys.map(String).filter((name) => editable.has(name)),
    ]);
  };

  const save = async () => {
    if (!canSave || !providerKey) {
      return;
    }
    const version = targetVersion.current;
    setSaving(true);
    try {
      const granted = new Set(checkedKeys);
      await updatePermissions(
        providerName,
        providerKey,
        permissions.map((permission) => ({
          name: permission.name,
          isGranted:
            permission.isEditable === false
              ? permission.isGranted
              : granted.has(permission.name),
        })),
      );
      if (targetVersion.current === version) {
        message.success('权限已保存');
        onClose();
      }
    } catch {
      if (targetVersion.current === version) message.error('保存权限失败');
    } finally {
      if (targetVersion.current === version) setSaving(false);
    }
  };

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onClose}
      onOk={save}
      confirmLoading={saving}
      okButtonProps={{ disabled: !canSave }}
      width={840}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        <div style={{ minHeight: 420 }}>
          {readFailed && (
            <Alert type="error" title="加载权限失败，请关闭后重试" showIcon />
          )}
          {loaded &&
            (groups.length ? (
              <Tabs
                tabPlacement="start"
                destroyOnHidden
                style={{ height: 420 }}
                items={groupsWithTrees.map(({ group, treeData }) => ({
                  key: group.name,
                  label: group.displayName,
                  children: (
                    <TreePanel
                      checkable
                      disabled={saving}
                      expandAllOnDataReady
                      height={360}
                      checkedKeys={group.permissions
                        .filter((permission) =>
                          grantedNames.has(permission.name),
                        )
                        .map((permission) => permission.name)}
                      treeData={treeData}
                      toolbarExtra="仅当前组"
                      onCheck={(keys) => setGroupChecks(group, keys)}
                      onCheckAll={(keys) => setGroupChecks(group, keys)}
                      onClearAll={() => setGroupChecks(group, [])}
                    />
                  ),
                }))}
              />
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="没有可配置的权限"
              />
            ))}
        </div>
      </Spin>
    </Modal>
  );
};

export default PermissionModal;
