import {
  type ActionType,
  PageContainer,
  type ProColumns,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Input, Modal, Popconfirm, Space, Switch, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import {
  createPermissionDefinition,
  createPermissionGroup,
  deletePermissionDefinition,
  deletePermissionGroup,
  getPermissionDefinitions,
  getPermissionGroups,
  type PermissionDefinitionRecord,
  type PermissionGroupRecord,
  updatePermissionDefinition,
  updatePermissionGroup,
} from '@/abp/permissionDefinitions';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  firstFilterValue,
  textFilter,
} from '@/components/tableColumnFilters';

/**
 * 权限定义管理（运行时动态权限定义）。
 * 左组右定义双表联动：选中组过滤右侧定义。静态（代码内）定义不在此显示，
 * 页面只运营 ABP 定义记录表里的动态部分；与静态同名的定义在创建时被后端拒绝（防遮蔽）。
 */
const PermissionDefinitionsPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const groupActionRef = useRef<ActionType>(undefined);
  const definitionActionRef = useRef<ActionType>(undefined);

  const [selectedGroup, setSelectedGroup] = useState<PermissionGroupRecord | null>(null);
  const [groupModalOpen, setGroupModalOpen] = useState(false);
  const [editingGroup, setEditingGroup] = useState<PermissionGroupRecord | null>(null);
  const [definitionModalOpen, setDefinitionModalOpen] = useState(false);
  const [editingDefinition, setEditingDefinition] = useState<PermissionDefinitionRecord | null>(null);

  const [groupName, setGroupName] = useState('');
  const [groupDisplayName, setGroupDisplayName] = useState('');
  const [definitionForm, setDefinitionForm] = useState({
    name: '',
    parentName: '',
    displayName: '',
    isEnabled: true,
  });

  const reloadGroups = () => groupActionRef.current?.reload();
  const reloadDefinitions = () => definitionActionRef.current?.reload();

  const openCreateGroup = () => {
    setEditingGroup(null);
    setGroupName('');
    setGroupDisplayName('');
    setGroupModalOpen(true);
  };

  const openEditGroup = (record: PermissionGroupRecord) => {
    setEditingGroup(record);
    setGroupName(record.name);
    // 预填原始串（可能是 "L:资源:键"）：保存原样写回，避免把本地化串覆盖成当前文化明文
    setGroupDisplayName(record.displayName);
    setGroupModalOpen(true);
  };

  const submitGroup = async () => {
    // 前置必填校验（裸 Modal 无 ProForm rules）；服务端拒绝时全局 errorHandler 已 toast 真实原因，
    // 这里仅保持弹窗打开（不关窗、不二次 toast，与仓库「错误必须可见」契约一致）
    if (!groupName.trim() || !groupDisplayName.trim()) {
      message.warning('组名与显示名均为必填');
      return;
    }
    try {
      if (editingGroup) {
        await updatePermissionGroup(editingGroup.id, { displayName: groupDisplayName });
        message.success('权限组已更新');
      } else {
        await createPermissionGroup({ name: groupName, displayName: groupDisplayName });
        message.success('权限组已创建');
      }
      setGroupModalOpen(false);
      reloadGroups();
    } catch {
      // 保持弹窗打开，错误呈现交给全局 errorHandler（避免双重提示）
    }
  };

  const openCreateDefinition = () => {
    setEditingDefinition(null);
    setDefinitionForm({ name: '', parentName: '', displayName: '', isEnabled: true });
    setDefinitionModalOpen(true);
  };

  const openEditDefinition = (record: PermissionDefinitionRecord) => {
    setEditingDefinition(record);
    setDefinitionForm({
      name: record.name,
      parentName: record.parentName || '',
      // 同上：编辑回填原始 displayName，回滚原样写回
      displayName: record.displayName,
      isEnabled: record.isEnabled,
    });
    setDefinitionModalOpen(true);
  };

  const submitDefinition = async () => {
    if (!definitionForm.name.trim() || !definitionForm.displayName.trim()) {
      message.warning('权限全名与显示名均为必填');
      return;
    }
    try {
      if (editingDefinition) {
        await updatePermissionDefinition(editingDefinition.id, {
          displayName: definitionForm.displayName,
          isEnabled: definitionForm.isEnabled,
        });
        message.success('权限定义已更新');
      } else {
        await createPermissionDefinition({
          groupName: selectedGroup?.name || '',
          name: definitionForm.name,
          parentName: definitionForm.parentName || undefined,
          displayName: definitionForm.displayName,
          isEnabled: definitionForm.isEnabled,
        });
        message.success('权限定义已创建');
      }
      setDefinitionModalOpen(false);
      reloadDefinitions();
    } catch {
      // 保持弹窗打开，错误呈现交给全局 errorHandler（避免双重提示）
    }
  };

  const groupColumns: ProColumns<PermissionGroupRecord>[] = [
    {
      title: '组名',
      dataIndex: 'name',
      width: 180,
      render: (_, record) => (
        <a onClick={() => setSelectedGroup(record)}>{record.name}</a>
      ),
      ...textFilter('组名'),
    },
    {
      title: '显示名',
      dataIndex: 'displayNameLocalized',
      ellipsis: true,
    },
    {
      title: '操作',
      width: 120,
      render: (_, record) => (
        <Space>
          {access.canUpdatePermissionDefinitions ? (
            <Button size="small" type="link" onClick={() => openEditGroup(record)}>
              编辑
            </Button>
          ) : null}
          {access.canDeletePermissionDefinitions ? (
            <Popconfirm
              title="删除此权限组？"
              description="组下仍有权限定义时将被拒绝。"
              onConfirm={async () => {
                // 失败（如组下仍有定义）由全局 errorHandler 呈现原因
                await deletePermissionGroup(record.id);
                message.success('已删除');
                if (selectedGroup?.id === record.id) {
                  setSelectedGroup(null);
                }
                reloadGroups();
              }}
            >
              <Button size="small" type="link" danger>
                删除
              </Button>
            </Popconfirm>
          ) : null}
        </Space>
      ),
    },
  ];

  const definitionColumns: ProColumns<PermissionDefinitionRecord>[] = [
    {
      title: '权限名',
      dataIndex: 'name',
      width: 260,
      ...textFilter('权限名'),
    },
    {
      title: '父权限',
      dataIndex: 'parentName',
      width: 200,
      render: (_, record) => record.parentName || '-',
    },
    {
      title: '显示名',
      dataIndex: 'displayNameLocalized',
      ellipsis: true,
    },
    {
      title: '状态',
      dataIndex: 'isEnabled',
      width: 90,
      render: (_, record) =>
        record.isEnabled ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>,
    },
    {
      title: '操作',
      width: 120,
      render: (_, record) => (
        <Space>
          {access.canUpdatePermissionDefinitions ? (
            <Button size="small" type="link" onClick={() => openEditDefinition(record)}>
              编辑
            </Button>
          ) : null}
          {access.canDeletePermissionDefinitions ? (
            <Popconfirm
              title="删除此权限定义？"
              description="存在子权限时将被拒绝；删除后已授予该权限的策略不再解析。"
              onConfirm={async () => {
                // 失败（如有子权限）由全局 errorHandler 呈现原因
                await deletePermissionDefinition(record.id);
                message.success('已删除');
                reloadDefinitions();
              }}
            >
              <Button size="small" type="link" danger>
                删除
              </Button>
            </Popconfirm>
          ) : null}
        </Space>
      ),
    },
  ];

  return (
    <PageContainer title="权限定义管理">
      <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
        <div style={{ flex: '0 0 380px' }}>
          <AutoHeightProTable<PermissionGroupRecord>
            headerTitle="权限组（动态）"
            rowKey="id"
            search={false}
            options={false}
            actionRef={groupActionRef}
            toolBarRender={() => [
              access.canCreatePermissionDefinitions ? (
                <Button key="create" type="primary" onClick={openCreateGroup}>
                  新建组
                </Button>
              ) : null,
            ]}
            request={async (params, _sorter, filters) => {
              const result = await getPermissionGroups({
                current: params.current,
                pageSize: params.pageSize,
                filter: firstFilterValue(filters, 'name'),
              });
              return { data: result.items, total: result.totalCount, success: true };
            }}
            columns={groupColumns}
            pagination={{ pageSize: 10 }}
          />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <AutoHeightProTable<PermissionDefinitionRecord>
            headerTitle={
              selectedGroup
                ? `权限定义 - ${selectedGroup.name}`
                : '权限定义（全部动态定义）'
            }
            rowKey="id"
            search={false}
            options={false}
            actionRef={definitionActionRef}
            params={{ groupName: selectedGroup?.name }}
            toolBarRender={() => [
              selectedGroup ? (
                <Button key="clear" onClick={() => setSelectedGroup(null)}>
                  查看全部
                </Button>
              ) : null,
              access.canCreatePermissionDefinitions && selectedGroup ? (
                <Button key="create" type="primary" onClick={openCreateDefinition}>
                  新建定义
                </Button>
              ) : null,
            ]}
            request={async (params, _sorter, filters) => {
              const result = await getPermissionDefinitions({
                current: params.current,
                pageSize: params.pageSize,
                groupName: params.groupName as string | undefined,
                filter: firstFilterValue(filters, 'name'),
              });
              return { data: result.items, total: result.totalCount, success: true };
            }}
            columns={definitionColumns}
            pagination={{ pageSize: 20 }}
          />
        </div>
      </div>

      <Modal
        title={editingGroup ? '编辑权限组' : '新建权限组'}
        open={groupModalOpen}
        onOk={submitGroup}
        onCancel={() => setGroupModalOpen(false)}
        destroyOnHidden
      >
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          <div>
            <div style={{ marginBottom: 4 }}>组名{editingGroup ? '（不可修改）' : ''}</div>
            <Input
              value={groupName}
              disabled={!!editingGroup}
              onChange={(e) => setGroupName(e.target.value)}
              placeholder="如 MyBiz"
            />
          </div>
          <div>
            <div style={{ marginBottom: 4 }}>显示名</div>
            <Input
              value={groupDisplayName}
              onChange={(e) => setGroupDisplayName(e.target.value)}
              placeholder="纯文本或 L:资源:键"
            />
          </div>
        </Space>
      </Modal>

      <Modal
        title={editingDefinition ? '编辑权限定义' : `新建权限定义（${selectedGroup?.name ?? ''}）`}
        open={definitionModalOpen}
        onOk={submitDefinition}
        onCancel={() => setDefinitionModalOpen(false)}
        destroyOnHidden
      >
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          <div>
            <div style={{ marginBottom: 4 }}>权限全名{editingDefinition ? '（不可修改）' : ''}</div>
            <Input
              value={definitionForm.name}
              disabled={!!editingDefinition}
              onChange={(e) => setDefinitionForm((f) => ({ ...f, name: e.target.value }))}
              placeholder="如 MyBiz.Orders.Approve（与代码内静态定义同名会被拒绝）"
            />
          </div>
          {!editingDefinition ? (
            <div>
              <div style={{ marginBottom: 4 }}>父权限（可选，须已存在）</div>
              <Input
                value={definitionForm.parentName}
                onChange={(e) => setDefinitionForm((f) => ({ ...f, parentName: e.target.value }))}
                placeholder="如 MyBiz.Orders"
              />
            </div>
          ) : null}
          <div>
            <div style={{ marginBottom: 4 }}>显示名</div>
            <Input
              value={definitionForm.displayName}
              onChange={(e) => setDefinitionForm((f) => ({ ...f, displayName: e.target.value }))}
              placeholder="纯文本或 L:资源:键"
            />
          </div>
          <div>
            <span style={{ marginRight: 8 }}>启用</span>
            <Switch
              checked={definitionForm.isEnabled}
              onChange={(checked) => setDefinitionForm((f) => ({ ...f, isEnabled: checked }))}
            />
          </div>
        </Space>
      </Modal>
    </PageContainer>
  );
};

export default PermissionDefinitionsPage;
