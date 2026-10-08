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
  const [unconfigured, setUnconfigured] = useState(false);
  const [ouTree, setOuTree] = useState<DataNode[]>([]);
  // OU 树父子联动开关（芋道同款）：默认不联动=勾哪个是哪个（精确集，与后端
  // Custom 范围的逐 OU 语义一致）；打开后勾父自动勾子，方便整棵部门一次选入
  const [ouParentLinked, setOuParentLinked] = useState(false);
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
      setUnconfigured(false);
      setScopeType(DataScopeType.All);
      setCheckedOuIds([]);
      return;
    }
    setLoading(true);
    getRoleDataScopeByRoleName(roleName)
      .then((data) => {
        const st = data.scopeType ?? DataScopeType.All;
        setUnconfigured(false);
        setScopeType(st);
        setCheckedOuIds(data.customOrganizationUnitIds || []);
        form.setFieldsValue({ scopeType: st });
      })
      .catch((err) => {
        // 仅 404（未配置）走默认表单；其它错误关闭弹窗——保存是按 roleName 全量覆盖，
        // 读取失败时让管理员顺手保存会拿默认值盖掉真实配置（RoleGrantModal 同款防线）
        if (isNotFound(err)) {
          setUnconfigured(true);
          setScopeType(DataScopeType.All);
          setCheckedOuIds([]);
          form.setFieldsValue({ scopeType: DataScopeType.All });
          return;
        }
        message.error('读取数据范围失败，请重试');
        onClose();
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
        {unconfigured && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message="该角色尚未配置数据范围"
            description="未配置时其成员（admin 除外）在启用数据范围的业务数据上默认零行可见（fail-closed），保存后按所选范围生效。"
          />
        )}
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
              <TreePanel
                checkable
                checkStrictly={!ouParentLinked}
                defaultExpandAll
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
