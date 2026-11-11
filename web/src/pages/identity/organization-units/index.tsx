import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProCard,
  type ProColumns,
  ProFormSelect,
  ProFormText,
  ProFormTreeSelect,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  App,
  Button,
  Collapse,
  Dropdown,
  Empty,
  type MenuProps,
  Popconfirm,
  Space,
  Splitter,
  Tree,
} from 'antd';
import React, { useEffect, useMemo, useRef, useState } from 'react';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import { getAllRoles, getUsers } from '@/abp/identity';
import {
  addOrganizationUnitMembers,
  addOrganizationUnitRoles,
  createOrganizationUnit,
  deleteOrganizationUnit,
  getOrganizationUnitMembers,
  getOrganizationUnitRoles,
  getOrganizationUnits,
  moveOrganizationUnit,
  type OrganizationUnitDto,
  removeOrganizationUnitMember,
  removeOrganizationUnitRole,
  updateOrganizationUnit,
} from '@/abp/identityAdmin';

const OrganizationUnitsPage: React.FC = () => {
  const access = useAccess();
  const { message, modal } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);
  const roleActionRef = useRef<ActionType>(undefined);
  const [units, setUnits] = useState<OrganizationUnitDto[]>([]);
  const [selected, setSelected] = useState<OrganizationUnitDto>();
  const [renameModalOpen, setRenameModalOpen] = useState(false);
  const [moveModalOpen, setMoveModalOpen] = useState(false);

  const load = async () => {
    const result = (await getOrganizationUnits()) || [];
    setUnits(result);
    setSelected(
      (current) => current && result.find((item) => item.id === current.id),
    );
  };

  useEffect(() => {
    load().catch(() => undefined);
  }, []);

  const treeData = useMemo(
    () =>
      units.filter((item) => !item.parentId).map((root) => toNode(root, units)),
    [units],
  );

  const movableTreeData = useMemo(() => {
    if (!selected) return [];
    const excluded = new Set([
      selected.id,
      ...collectDescendantIds(selected, units),
    ]);
    return units
      .filter((item) => !item.parentId)
      .map((root) => toNode(root, units, excluded))
      .filter(Boolean);
  }, [selected, units]);

  const departmentActions: MenuProps['items'] = [];
  if (selected && access.canUpdateOrganizationUnits) {
    departmentActions.push(
      {
        key: 'rename',
        label: '重命名',
        onClick: () => setRenameModalOpen(true),
      },
      { key: 'move', label: '移动部门', onClick: () => setMoveModalOpen(true) },
    );
  }
  if (selected && access.canDeleteOrganizationUnits) {
    departmentActions.push({
      key: 'delete',
      label: '删除部门',
      danger: true,
      onClick: () =>
        modal.confirm({
          title: `确认删除部门「${selected.displayName}」？`,
          okText: '删除',
          cancelText: '取消',
          okButtonProps: { danger: true },
          onOk: async () => {
            await deleteOrganizationUnit(selected.id);
            message.success('已删除');
            setSelected((current) =>
              current?.id === selected.id ? undefined : current,
            );
            await load();
          },
        }),
    });
  }

  return (
    <PageContainer {...identityOrgHelpHeader('org', '部门')}>
      <Splitter>
        <Splitter.Panel defaultSize={320} min={240}>
          <ProCard
            title="部门"
            extra={
              access.canCreateOrganizationUnits && (
                <ModalForm
                  title="新建根部门"
                  trigger={<Button>新建根部门</Button>}
                  modalProps={{ destroyOnHidden: true }}
                  onFinish={async (values) => {
                    await createOrganizationUnit({
                      displayName: values.displayName,
                    });
                    message.success('已创建根部门');
                    await load();
                    return true;
                  }}
                >
                  <DepartmentNameField />
                </ModalForm>
              )
            }
          >
            {units.length ? (
              <Tree
                blockNode
                treeData={treeData}
                defaultExpandAll
                selectedKeys={selected ? [selected.id] : []}
                onSelect={(_, info) => {
                  setSelected(
                    units.find((item) => item.id === String(info.node.key)),
                  );
                  setRenameModalOpen(false);
                  setMoveModalOpen(false);
                }}
              />
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="暂无部门"
              />
            )}
          </ProCard>
        </Splitter.Panel>
        <Splitter.Panel>
          {selected ? (
            <ProCard
              title={selected.displayName}
              extra={
                <Space wrap>
                  {access.canCreateOrganizationUnits && (
                    <ModalForm
                      key={`child-${selected.id}`}
                      title={`添加子部门 - ${selected.displayName}`}
                      trigger={<Button>添加子部门</Button>}
                      modalProps={{ destroyOnHidden: true }}
                      onFinish={async (values) => {
                        await createOrganizationUnit({
                          displayName: values.displayName,
                          parentId: selected.id,
                        });
                        message.success('已添加子部门');
                        await load();
                        return true;
                      }}
                    >
                      <DepartmentNameField />
                    </ModalForm>
                  )}
                  {departmentActions.length > 0 && (
                    <Dropdown
                      menu={{ items: departmentActions }}
                      trigger={['click']}
                    >
                      <Button>更多</Button>
                    </Dropdown>
                  )}
                </Space>
              }
            >
              <ProTable
                key={`members-${selected.id}`}
                rowKey="id"
                headerTitle="部门成员"
                actionRef={actionRef}
                search={false}
                options={false}
                columns={[
                  { title: '用户名', dataIndex: 'userName' },
                  { title: '邮箱', dataIndex: 'email' },
                  ...(access.canManageOrganizationUnitMembers
                    ? [
                        {
                          title: '操作',
                          valueType: 'option',
                          render: (_, record) => (
                            <Popconfirm
                              title="确认将该成员移出本部门？"
                              onConfirm={async () => {
                                await removeOrganizationUnitMember(
                                  selected.id,
                                  record.id,
                                );
                                message.success('已移除');
                                actionRef.current?.reload();
                              }}
                            >
                              <a>移除</a>
                            </Popconfirm>
                          ),
                        } as ProColumns,
                      ]
                    : []),
                ]}
                request={async (params) => {
                  const pageSize = params.pageSize ?? 10;
                  const result = await getOrganizationUnitMembers(selected.id, {
                    skipCount: ((params.current ?? 1) - 1) * pageSize,
                    maxResultCount: pageSize,
                  });
                  return {
                    data: result.items,
                    total: result.totalCount,
                    success: true,
                  };
                }}
                toolBarRender={() =>
                  access.canManageOrganizationUnitMembers
                    ? [
                        <ModalForm
                          key="add-members"
                          title={`添加成员 - ${selected.displayName}`}
                          trigger={<Button type="primary">添加成员</Button>}
                          modalProps={{ destroyOnHidden: true }}
                          onFinish={async (values) => {
                            await addOrganizationUnitMembers(
                              selected.id,
                              values.userIds || [],
                            );
                            message.success('已添加');
                            actionRef.current?.reload();
                            return true;
                          }}
                        >
                          <ProFormSelect
                            name="userIds"
                            label="用户"
                            mode="multiple"
                            rules={[{ required: true, message: '请选择用户' }]}
                            request={async () => {
                              const result = await getUsers({
                                current: 1,
                                pageSize: 100,
                              });
                              return (result.items || []).map((item) => ({
                                label: item.userName,
                                value: item.id,
                              }));
                            }}
                          />
                        </ModalForm>,
                      ]
                    : []
                }
              />
              <Collapse
                key={`roles-${selected.id}`}
                items={[
                  {
                    key: 'roles',
                    label: '统一角色（可选）',
                    children: (
                      <>
                        <p>本部门所有成员自动获得这些角色。</p>
                        <ProTable
                          rowKey="id"
                          headerTitle="已配置角色"
                          actionRef={roleActionRef}
                          search={false}
                          options={false}
                          pagination={false}
                          columns={[
                            { title: '角色名称', dataIndex: 'name' },
                            ...(access.canManageOrganizationUnitRoles
                              ? [
                                  {
                                    title: '操作',
                                    valueType: 'option',
                                    render: (_, record) => (
                                      <Popconfirm
                                        title="确认移除该部门统一角色？"
                                        description="移除后，成员不再通过本部门获得该角色。"
                                        onConfirm={async () => {
                                          await removeOrganizationUnitRole(
                                            selected.id,
                                            record.id,
                                          );
                                          message.success('已移除');
                                          roleActionRef.current?.reload();
                                        }}
                                      >
                                        <a>移除</a>
                                      </Popconfirm>
                                    ),
                                  } as ProColumns,
                                ]
                              : []),
                          ]}
                          request={async () => {
                            const result = await getOrganizationUnitRoles(
                              selected.id,
                            );
                            return {
                              data: result.items,
                              total: result.items?.length || 0,
                              success: true,
                            };
                          }}
                          toolBarRender={() =>
                            access.canManageOrganizationUnitRoles
                              ? [
                                  <ModalForm
                                    key="add-roles"
                                    title={`配置部门统一角色 - ${selected.displayName}`}
                                    trigger={<Button>添加角色</Button>}
                                    modalProps={{ destroyOnHidden: true }}
                                    onFinish={async (values) => {
                                      await addOrganizationUnitRoles(
                                        selected.id,
                                        values.roleIds || [],
                                      );
                                      message.success('已添加');
                                      roleActionRef.current?.reload();
                                      return true;
                                    }}
                                  >
                                    <p>角色将授予本部门所有成员。</p>
                                    <ProFormSelect
                                      name="roleIds"
                                      label="角色"
                                      mode="multiple"
                                      rules={[
                                        {
                                          required: true,
                                          message: '请选择角色',
                                        },
                                      ]}
                                      request={async () => {
                                        const result = await getAllRoles();
                                        return (result.items || []).map(
                                          (item) => ({
                                            label: item.name,
                                            value: item.id,
                                          }),
                                        );
                                      }}
                                    />
                                  </ModalForm>,
                                ]
                              : []
                          }
                        />
                      </>
                    ),
                  },
                ]}
              />
            </ProCard>
          ) : (
            <ProCard>
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="请选择左侧部门，查看和维护成员"
              />
            </ProCard>
          )}
        </Splitter.Panel>
      </Splitter>
      {selected && access.canUpdateOrganizationUnits ? (
        <ModalForm
          key={`rename-${selected.id}-${selected.displayName}`}
          title="重命名部门"
          open={renameModalOpen}
          onOpenChange={setRenameModalOpen}
          initialValues={{ displayName: selected.displayName }}
          modalProps={{ destroyOnHidden: true }}
          onFinish={async (values) => {
            await updateOrganizationUnit(selected.id, {
              displayName: values.displayName,
            });
            message.success('已更新');
            setRenameModalOpen(false);
            await load();
            return true;
          }}
        >
          <DepartmentNameField />
        </ModalForm>
      ) : null}
      {selected && access.canUpdateOrganizationUnits ? (
        <ModalForm
          key={`move-${selected.id}`}
          title={`移动部门 - ${selected.displayName}`}
          open={moveModalOpen}
          onOpenChange={setMoveModalOpen}
          modalProps={{ destroyOnHidden: true }}
          onFinish={async (values) => {
            await moveOrganizationUnit(
              selected.id,
              values.targetParentId || null,
            );
            message.success('已移动');
            setMoveModalOpen(false);
            await load();
            return true;
          }}
        >
          <MoveTargetSelect
            treeData={movableTreeData}
            currentParentId={selected.parentId ?? undefined}
          />
        </ModalForm>
      ) : null}
    </PageContainer>
  );
};

const DepartmentNameField = () => (
  <ProFormText
    name="displayName"
    label="部门名称"
    rules={[{ required: true, message: '请输入部门名称' }]}
  />
);

const MoveTargetSelect: React.FC<{
  treeData: any[];
  currentParentId?: string;
}> = ({ treeData, currentParentId }) => (
  <>
    <p>选择新的上级部门；留空则作为根部门。</p>
    <ProFormTreeSelect
      name="targetParentId"
      label="上级部门"
      allowClear
      placeholder="留空则作为根部门"
      fieldProps={{
        treeData,
        treeDefaultExpandAll: true,
        style: { width: '100%' },
        titleRender: (node: any) => (
          <span>
            {node.title}
            {node.key === currentParentId ? '（当前）' : ''}
          </span>
        ),
      }}
    />
  </>
);

function toNode(
  unit: OrganizationUnitDto,
  all: OrganizationUnitDto[],
  excluded?: Set<string>,
): any {
  if (excluded?.has(unit.id)) return null;
  return {
    key: unit.id,
    title: unit.displayName,
    children: all
      .filter((item) => item.parentId === unit.id)
      .map((child) => toNode(child, all, excluded))
      .filter(Boolean),
  };
}

function collectDescendantIds(
  unit: OrganizationUnitDto,
  all: OrganizationUnitDto[],
): string[] {
  const childrenByParent = new Map<string, string[]>();
  for (const item of all) {
    if (!item.parentId) continue;
    const siblings = childrenByParent.get(item.parentId);
    if (siblings) siblings.push(item.id);
    else childrenByParent.set(item.parentId, [item.id]);
  }
  const descendants: string[] = [];
  const stack = childrenByParent.get(unit.id) ?? [];
  while (stack.length > 0) {
    const id = stack.pop();
    if (id === undefined) break;
    descendants.push(id);
    const grandchildren = childrenByParent.get(id);
    if (grandchildren) stack.push(...grandchildren);
  }
  return descendants;
}

export default OrganizationUnitsPage;
