import {
  ModalForm,
  PageContainer,
  ProCard,
  type ProColumns,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
  ProFormTreeSelect,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Collapse,
  Dropdown,
  Empty,
  Button as GridButton,
  Space as GridControls,
  type MenuProps,
  Popconfirm,
  Space,
  Splitter,
  Tree,
} from 'antd';
import React, { useEffect, useMemo, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import { getAllRoles, getUsers } from '@/abp/identity';
import {
  addOrganizationUnitMembers,
  addOrganizationUnitRoles,
  createOrganizationUnit,
  deleteOrganizationUnit,
  getOrganizationUnits,
  moveOrganizationUnit,
  type OrganizationUnitDto,
  queryOrganizationMembers,
  queryOrganizationRoles,
  removeOrganizationUnitMember,
  removeOrganizationUnitRole,
  updateOrganizationUnit,
} from '@/abp/identityAdmin';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { isNotFound } from '@/requestErrorConfig';

type OrgMember = { id: string; userName: string; email?: string };
type OrgRole = { id: string; name: string };

const organizationIdentifierPattern =
  /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

function parseIdentifierList(value?: string): string[] {
  const ids = value?.split(/[\s,，]+/).filter(Boolean) ?? [];
  return [...new Set(ids.map((id) => id.toLowerCase()))];
}

function IdentifierListField({ name, label }: { name: string; label: string }) {
  return (
    <ProFormTextArea
      name={name}
      label={label}
      fieldProps={{
        rows: 3,
        autoCapitalize: 'none',
        autoCorrect: 'off',
        spellCheck: false,
      }}
      formItemProps={{
        extra: '输入带连字符的完整 ID。多个 ID 可用换行或逗号分隔。',
        validateTrigger: 'onBlur',
      }}
      rules={[
        { required: true, whitespace: true, message: `请输入${label}` },
        {
          validator: async (_: unknown, value?: string) => {
            const ids = parseIdentifierList(value);
            if (
              ids.length === 0 ||
              ids.some((id) => !organizationIdentifierPattern.test(id))
            ) {
              throw new Error(`请输入带连字符的完整${label}`);
            }
          },
        },
      ]}
    />
  );
}

const OrganizationUnitsPage: React.FC = () => {
  const access = useAccess();
  const { message, modal } = App.useApp();
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
      selected?.id,
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
          title: `确认删除部门「${selected?.displayName}」？`,
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

  const membersGrid = useSearchableRemoteGrid<OrgMember>({
    columnDefs: toGridColumns<OrgMember>(
      [
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
                      if (!selected) return;
                      await removeOrganizationUnitMember(
                        selected.id,
                        record.id,
                      );
                      message.success('已移除');
                      membersGrid.refresh();
                    }}
                  >
                    <a>移除</a>
                  </Popconfirm>
                ),
              } as ProColumns,
            ]
          : []),
      ],
      {
        pro: true,
        kinds: { email: access.canUpdateUsers ? 'text' : 'display' },
      },
    ),
    ariaLabel: '部门成员',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        selected
          ? queryOrganizationMembers(selected.id, input, signal)
          : Promise.resolve({ items: [], totalCount: 0 }),
      [selected?.id],
    ),
  });
  const rolesGrid = useSearchableRemoteGrid<OrgRole>({
    columnDefs: toGridColumns<OrgRole>(
      [
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
                      if (!selected) return;
                      await removeOrganizationUnitRole(selected.id, record.id);
                      message.success('已移除');
                      rolesGrid.refresh();
                    }}
                  >
                    <a>移除</a>
                  </Popconfirm>
                ),
              } as ProColumns,
            ]
          : []),
      ],
      { pro: true, kinds: {} },
    ),
    ariaLabel: '部门角色',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        selected
          ? queryOrganizationRoles(selected.id, input, signal)
          : Promise.resolve({ items: [], totalCount: 0 }),
      [selected?.id],
    ),
  });

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
              title={selected?.displayName}
              extra={
                <Space wrap>
                  {access.canCreateOrganizationUnits && (
                    <ModalForm
                      key={`child-${selected.id}`}
                      title={`添加子部门 - ${selected?.displayName}`}
                      trigger={<Button>添加子部门</Button>}
                      modalProps={{ destroyOnHidden: true }}
                      onFinish={async (values) => {
                        await createOrganizationUnit({
                          displayName: values.displayName,
                          parentId: selected?.id,
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
              <strong>{'部门成员'}</strong>
              <GridControls wrap style={{ marginBottom: 8 }}>
                {membersGrid.searchBox}
                {membersGrid.hasFilters && (
                  <GridButton onClick={membersGrid.clearFilters}>
                    清空筛选
                  </GridButton>
                )}{' '}
                {access.canManageOrganizationUnitMembers
                  ? [
                      <ModalForm<{ userIds?: string[]; userIdsText?: string }>
                        key="add-members"
                        title={`添加成员 - ${selected?.displayName}`}
                        trigger={<Button type="primary">添加成员</Button>}
                        modalProps={{ destroyOnHidden: true }}
                        scrollToFirstError={{ focus: true }}
                        onFinish={async (values) => {
                          const ids = access.canManageUsers
                            ? (values.userIds ?? [])
                            : parseIdentifierList(values.userIdsText);
                          try {
                            await addOrganizationUnitMembers(selected?.id, ids);
                          } catch (error) {
                            if (!isNotFound(error)) throw error;
                            message.error('找不到指定用户，请核对用户 ID。');
                            return false;
                          }
                          message.success('已添加');
                          membersGrid.refresh();
                          return true;
                        }}
                      >
                        {access.canManageUsers ? (
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
                        ) : (
                          <IdentifierListField
                            name="userIdsText"
                            label="用户 ID"
                          />
                        )}
                      </ModalForm>,
                    ]
                  : []}
              </GridControls>
              <AgGridReact<OrgMember> {...membersGrid.gridProps} />

              <Collapse
                key={`roles-${selected.id}`}
                destroyOnHidden
                items={[
                  {
                    key: 'roles',
                    label: '统一角色（可选）',
                    children: (
                      <>
                        <p>本部门所有成员自动获得这些角色。</p>

                        <strong>{'已配置角色'}</strong>
                        <GridControls wrap style={{ marginBottom: 8 }}>
                          {rolesGrid.searchBox}
                          {rolesGrid.hasFilters && (
                            <GridButton onClick={rolesGrid.clearFilters}>
                              清空筛选
                            </GridButton>
                          )}{' '}
                          {access.canManageOrganizationUnitRoles
                            ? [
                                <ModalForm<{
                                  roleIds?: string[];
                                  roleIdsText?: string;
                                }>
                                  key="add-roles"
                                  title={`配置部门统一角色 - ${selected?.displayName}`}
                                  trigger={<Button>添加角色</Button>}
                                  modalProps={{ destroyOnHidden: true }}
                                  scrollToFirstError={{ focus: true }}
                                  onFinish={async (values) => {
                                    const ids = access.canManageRoles
                                      ? (values.roleIds ?? [])
                                      : parseIdentifierList(values.roleIdsText);
                                    try {
                                      await addOrganizationUnitRoles(
                                        selected?.id,
                                        ids,
                                      );
                                    } catch (error) {
                                      if (!isNotFound(error)) throw error;
                                      message.error(
                                        '找不到指定角色，请核对角色 ID。',
                                      );
                                      return false;
                                    }
                                    message.success('已添加');
                                    rolesGrid.refresh();
                                    return true;
                                  }}
                                >
                                  <p>角色将授予本部门所有成员。</p>
                                  {access.canManageRoles ? (
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
                                  ) : (
                                    <IdentifierListField
                                      name="roleIdsText"
                                      label="角色 ID"
                                    />
                                  )}
                                </ModalForm>,
                              ]
                            : []}
                        </GridControls>
                        <AgGridReact<OrgRole> {...rolesGrid.gridProps} />
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
          key={`rename-${selected.id}-${selected?.displayName}`}
          title="重命名部门"
          open={renameModalOpen}
          onOpenChange={setRenameModalOpen}
          initialValues={{ displayName: selected?.displayName }}
          modalProps={{ destroyOnHidden: true }}
          onFinish={async (values) => {
            await updateOrganizationUnit(selected?.id, {
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
          title={`移动部门 - ${selected?.displayName}`}
          open={moveModalOpen}
          onOpenChange={setMoveModalOpen}
          modalProps={{ destroyOnHidden: true }}
          onFinish={async (values) => {
            await moveOrganizationUnit(
              selected?.id,
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
