import { Alert, App, Form, Modal, Select, Spin, Switch } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useEffect, useState } from 'react';
import TreePanel from '@/components/TreePanel';
import {
  DataScopeType,
  DataScopeTypeLabels,
  getRoleDataScopeByRoleName,
  saveRoleDataScope,
} from '@/abp/dataScope';
import { getOrganizationUnits } from '@/abp/identityAdmin';
import { isNotFound } from '@/requestErrorConfig';

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
    if (!item.id) continue;
    nodes.set(item.id, {
      key: item.id,
      title: item.displayName,
      children: [],
    });
  }

  const roots: DataNode[] = [];
  for (const item of items) {
    if (!item.id) continue;
    const node = nodes.get(item.id);
    if (!node) continue;
    const parent = item.parentId ? nodes.get(item.parentId) : undefined;
    if (parent) {
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
  const [loadedRoleName, setLoadedRoleName] = useState<string>();
  const [unconfigured, setUnconfigured] = useState(false);
  const [ouTree, setOuTree] = useState<DataNode[]>([]);
  // OU 树父子联动开关（芋道同款）：默认不联动=勾哪个是哪个（精确集，与后端
  // Custom 范围的逐 OU 语义一致）；打开后勾父自动勾子，方便整棵部门一次选入
  const [ouParentLinked, setOuParentLinked] = useState(false);
  const [scopeType, setScopeType] = useState<DataScopeType>();
  const [checkedOuIds, setCheckedOuIds] = useState<string[]>([]);

  const isCustom = scopeType === DataScopeType.Custom;
  const canSave =
    open && !!roleName && loadedRoleName === roleName && !loading && !saving;

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
    form.resetFields();
    setLoadedRoleName(undefined);
    setUnconfigured(false);
    setScopeType(undefined);
    setCheckedOuIds([]);
    if (!open || !roleName) {
      return;
    }
    let cancelled = false;
    setLoading(true);
    getRoleDataScopeByRoleName(roleName)
      .then((data) => {
        if (cancelled) return;
        const st = data.scopeType;
        setUnconfigured(false);
        setScopeType(st);
        setCheckedOuIds(data.customOrganizationUnitIds || []);
        form.setFieldsValue({ scopeType: st });
        setLoadedRoleName(roleName);
      })
      .catch((err) => {
        if (cancelled) return;
        // 仅 404（未配置）允许选择新范围；其它错误关闭弹窗——保存是按 roleName 全量覆盖，
        // 读取失败时让管理员保存会拿新值盖掉未读到的真实配置。
        if (isNotFound(err)) {
          setUnconfigured(true);
          setLoadedRoleName(roleName);
          return;
        }
        message.error('读取数据范围失败，请重试');
        onClose();
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open, roleName, form]);

  const save = async () => {
    if (!canSave || !roleName) {
      return;
    }
    const values = await form.validateFields().catch(() => undefined);
    if (!values) return;
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
      okButtonProps={{ disabled: !canSave }}
      width={560}
      forceRender
    >
      <Spin spinning={loading}>
        {unconfigured && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            title="该角色尚未配置数据范围"
            description="仅影响已接入数据范围的业务数据。未配置且没有其他角色授予范围时，普通成员看不到这些数据；请选择范围后保存。"
          />
        )}
        <Form form={form} layout="vertical">
          <Form.Item
            name="scopeType"
            label="数据范围类型"
            rules={[{ required: true, message: '请选择数据范围类型' }]}
          >
            <Select
              placeholder="请选择数据范围类型"
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
              <TreePanel
                checkable
                checkStrictly={!ouParentLinked}
                expandAllOnDataReady
                height={300}
                treeData={ouTree}
                checkedKeys={checkedOuIds}
                onCheck={(keys) => setCheckedOuIds(keys.map(String))}
                onCheckAll={(allKeys) =>
                  setCheckedOuIds(allKeys.map(String))
                }
                onClearAll={() => setCheckedOuIds([])}
                toolbarExtra={
                  <span style={{ fontSize: 12, whiteSpace: 'nowrap' }}>
                    父子联动{' '}
                    <Switch
                      size="small"
                      checked={ouParentLinked}
                      onChange={setOuParentLinked}
                    />
                  </span>
                }
              />
            </Form.Item>
          )}
        </Form>
      </Spin>
    </Modal>
  );
};

export default DataScopeModal;
