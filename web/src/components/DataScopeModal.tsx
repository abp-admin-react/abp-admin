import { App, Form, Modal, Select, Spin, Tree } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useMemo, useState } from 'react';
import {
  DataScopeType,
  DataScopeTypeLabels,
  getRoleDataScopeByRoleName,
  saveRoleDataScope,
} from '@/abp/dataScope';
import { getOrganizationUnits } from '@/abp/identityAdmin';

type OrganizationUnitDto = {
  id?: string;
  parentId?: string | null;
  displayName?: string;
};

type DataScopeModalProps = {
  open: boolean;
  title: string;
  roleName?: string;
  onClose: () => void;
};

function buildOuTree(items: OrganizationUnitDto[]): DataNode[] {
  const nodes = new Map<string, DataNode>();
  for (const item of items) {
    nodes.set(item.id!, {
      key: item.id!,
      title: item.displayName,
      children: [],
    });
  }

  const roots: DataNode[] = [];
  for (const item of items) {
    const node = nodes.get(item.id!)!;
    if (item.parentId && nodes.has(item.parentId)) {
      const parent = nodes.get(item.parentId)!;
      parent.children = parent.children || [];
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

const DataScopeModal: React.FC<DataScopeModalProps> = ({
  open,
  title,
  roleName,
  onClose,
}) => {
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [ouTree, setOuTree] = useState<DataNode[]>([]);
  const [scopeType, setScopeType] = useState<DataScopeType>(DataScopeType.All);
  const [checkedOuIds, setCheckedOuIds] = useState<string[]>([]);

  const isCustom = scopeType === DataScopeType.Custom;

  useEffect(() => {
    if (!open) {
      return;
    }
    // 加载组织单元树
    getOrganizationUnits()
      .then((items) => setOuTree(buildOuTree(items || [])))
      .catch(() => message.error('加载组织单元失败'));
  }, [open, message]);

  useEffect(() => {
    if (!open || !roleName) {
      form.resetFields();
      setScopeType(DataScopeType.All);
      setCheckedOuIds([]);
      return;
    }
    setLoading(true);
    getRoleDataScopeByRoleName(roleName)
      .then((data) => {
        const st = data.scopeType ?? DataScopeType.All;
        setScopeType(st);
        setCheckedOuIds(data.customOrganizationUnitIds || []);
        form.setFieldsValue({ scopeType: st });
      })
      .catch(() => {
        // 未配置过，使用默认值
        setScopeType(DataScopeType.All);
        setCheckedOuIds([]);
        form.setFieldsValue({ scopeType: DataScopeType.All });
      })
      .finally(() => setLoading(false));
  }, [open, roleName, form]);

  const save = async () => {
    if (!roleName) {
      return;
    }
    const values = await form.validateFields();
    if (
      values.scopeType === DataScopeType.Custom &&
      checkedOuIds.length === 0
    ) {
      message.error('请选择至少一个组织单元');
      return;
    }
    setSaving(true);
    try {
      await saveRoleDataScope({
        roleName,
        scopeType: values.scopeType,
        customOrganizationUnitIds:
          values.scopeType === DataScopeType.Custom ? checkedOuIds : [],
      });
      message.success('数据范围已保存');
      onClose();
    } catch {
      message.error('保存数据范围失败');
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
      width={560}
      forceRender
    >
      <Spin spinning={loading}>
        <Form form={form} layout="vertical">
          <Form.Item
            name="scopeType"
            label="数据范围类型"
            rules={[{ required: true, message: '请选择数据范围类型' }]}
          >
            <Select
              options={Object.entries(DataScopeTypeLabels).map(
                ([value, label]) => ({
                  value: Number(value) as DataScopeType,
                  label,
                }),
              )}
              onChange={(v) => setScopeType(v as DataScopeType)}
            />
          </Form.Item>
          {isCustom && (
            <Form.Item
              label="自定义组织单元"
              required
              validateStatus={checkedOuIds.length === 0 ? 'error' : undefined}
              help={
                checkedOuIds.length === 0 ? '请选择至少一个组织单元' : undefined
              }
            >
              <Tree
                checkable
                checkStrictly
                defaultExpandAll
                treeData={ouTree}
                checkedKeys={checkedOuIds}
                onCheck={(keys) => {
                  const next = Array.isArray(keys) ? keys : keys.checked;
                  setCheckedOuIds(next as string[]);
                }}
              />
            </Form.Item>
          )}
        </Form>
      </Spin>
    </Modal>
  );
};

export default DataScopeModal;
