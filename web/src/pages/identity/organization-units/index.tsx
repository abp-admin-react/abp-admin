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
import { App, Button, Popconfirm, Splitter, Tree } from 'antd';
import React, { useEffect, useMemo, useRef, useState } from 'react';
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
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);
  const roleActionRef = useRef<ActionType>(undefined);
  const [units, setUnits] = useState<OrganizationUnitDto[]>([]);
  const [selected, setSelected] = useState<OrganizationUnitDto>();
  const [moveModalOpen, setMoveModalOpen] = useState(false);

  const load = async () => {
    const result = await getOrganizationUnits();
    setUnits(result || []);
  };

  useEffect(() => {
    load().catch(() => undefined);
  }, []);

  const treeData = useMemo(
    () =>
      units.filter((item) => !item.parentId).map((root) => toNode(root, units)),
    [units],
  );

  // 移动目标树：防环排除必须作用于树构建的每一层——只过滤根层数组时，
  // 选中非根节点后其自身与子树仍会以祖先的 children 出现、可选为目标父节点
  //（后端同样有防环校验兜底，UI 不是控制）
  const movableTreeData = useMemo(() => {
    if (!selected) {
      return [];
    }
    const excluded = new Set<string>([
      selected.id,
      ...collectDescendantIds(selected, units),
    ]);
    return (
      units
        .filter((item) => !item.parentId)
        .map((root) => toNode(root, units, excluded))
        // 选中根节点自身时 toNode 返回 null（排除自己）——根层不滤掉会让
        // TreeSelect 拿到 [null, ...] 渲染崩溃（六透镜第二轮 function-High）
        .filter(Boolean)
    );
  }, [selected, units]);

  return (
    <PageContainer>
      <Splitter>
        <Splitter.Panel defaultSize={320} min={240}>
          <ProCard
            title="组织单元"
            extra={
              <ModalForm
                title="新建组织单元"
                trigger={<Button type="primary">新建</Button>}
                onFinish={async (values) => {
                  await createOrganizationUnit({
                    displayName: values.displayName,
                    parentId: selected?.id,
                  });
                  message.success('已创建');
                  await load();
                  return true;
                }}
              >
                <ProFormText
                  name="displayName"
                  label="名称"
                  rules={[{ required: true }]}
                />
              </ModalForm>
            }
          >
            <Tree
              treeData={treeData}
              defaultExpandAll
              selectedKeys={selected ? [selected.id] : []}
              onSelect={(_, info) => {
                const id = String(info.node.key);
                setSelected(units.find((item) => item.id === id));
                actionRef.current?.reload();
                roleActionRef.current?.reload();
              }}
              titleRender={(node) => (
                <span>
                  {node.title as string}{' '}
                  {selected?.id === node.key ? (
                    <>
                      <a
                        onClick={() => setMoveModalOpen(true)}
                        style={{ marginRight: 4 }}
                      >
                        移动
                      </a>
                      <Popconfirm
                        title="删除该组织单元？"
                        onConfirm={async () => {
                          await deleteOrganizationUnit(String(node.key));
                          message.success('已删除');
                          setSelected(undefined);
                          await load();
                        }}
                      >
                        <a>删除</a>
                      </Popconfirm>
                    </>
                  ) : null}
                </span>
              )}
            />
          </ProCard>
        </Splitter.Panel>
        <Splitter.Panel>
          <ProCard
            tabs={{
              items: [
                {
                  key: 'members',
                  label: '成员',
                  children: (
                    <ProTable
                      rowKey="id"
                      headerTitle={
                        selected
                          ? `成员 - ${selected.displayName}`
                          : '请选择组织单元'
                      }
                      actionRef={actionRef}
                      search={false}
                      columns={
                        [
                          { title: '用户名', dataIndex: 'userName' },
                          { title: '邮箱', dataIndex: 'email' },
                          {
                            title: '操作',
                            valueType: 'option',
                            render: (_, record) =>
                              selected ? (
                                <Popconfirm
                                  title="移除该成员？"
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
                              ) : null,
                          },
                        ] as ProColumns[]
                      }
                      request={async (params) => {
                        if (!selected) {
                          return { data: [], success: true, total: 0 };
                        }
                        const pageSize = params.pageSize ?? 10;
                        // 后端 GetMembersAsync 本身分页：原先固定取前 100 条，
                        // 超过 100 人的组织单元看不到其余成员
                        const result = await getOrganizationUnitMembers(
                          selected.id,
                          {
                            skipCount: ((params.current ?? 1) - 1) * pageSize,
                            maxResultCount: pageSize,
                          },
                        );
                        return {
                          data: result.items,
                          total: result.totalCount,
                          success: true,
                        };
                      }}
                      toolBarRender={() => [
                        selected ? (
                          <ModalForm
                            key="add"
                            title="添加成员"
                            trigger={<Button>添加成员</Button>}
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
                              rules={[
                                { required: true, message: '请选择用户' },
                              ]}
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
                          </ModalForm>
                        ) : null,
                        selected ? (
                          <ModalForm
                            key="rename"
                            title="重命名"
                            trigger={<Button>重命名</Button>}
                            initialValues={{
                              displayName: selected.displayName,
                            }}
                            onFinish={async (values) => {
                              await updateOrganizationUnit(selected.id, {
                                displayName: values.displayName,
                              });
                              message.success('已更新');
                              await load();
                              return true;
                            }}
                          >
                            <ProFormText
                              name="displayName"
                              label="名称"
                              rules={[{ required: true }]}
                            />
                          </ModalForm>
                        ) : null,
                      ]}
                    />
                  ),
                },
                {
                  key: 'roles',
                  label: '角色',
                  children: (
                    <ProTable
                      rowKey="id"
                      headerTitle={
                        selected
                          ? `角色 - ${selected.displayName}`
                          : '请选择组织单元'
                      }
                      actionRef={roleActionRef}
                      search={false}
                      columns={
                        [
                          { title: '角色', dataIndex: 'name' },
                          {
                            title: '操作',
                            valueType: 'option',
                            render: (_, record) =>
                              selected ? (
                                <Popconfirm
                                  title="移除该角色？"
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
                              ) : null,
                          },
                        ] as ProColumns[]
                      }
                      request={async () => {
                        if (!selected) {
                          return { data: [], success: true, total: 0 };
                        }
                        const result = await getOrganizationUnitRoles(
                          selected.id,
                        );
                        return {
                          data: result.items,
                          total: result.items?.length || 0,
                          success: true,
                        };
                      }}
                      toolBarRender={() => [
                        selected ? (
                          <ModalForm
                            key="add-roles"
                            title="添加角色"
                            trigger={<Button>添加角色</Button>}
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
                            <ProFormSelect
                              name="roleIds"
                              label="角色"
                              mode="multiple"
                              rules={[
                                { required: true, message: '请选择角色' },
                              ]}
                              request={async () => {
                                const result = await getAllRoles();
                                return (result.items || []).map((item) => ({
                                  label: item.name,
                                  value: item.id,
                                }));
                              }}
                            />
                          </ModalForm>
                        ) : null,
                      ]}
                    />
                  ),
                },
              ],
            }}
          />
        </Splitter.Panel>
      </Splitter>
      {/* 移动是树操作：弹窗挂在页面级（树上「移动」链接触发），
          不寄生在成员 Tab 工具栏——避免跨面板隐式耦合（六透镜第二轮 design-Low） */}
      {selected ? (
        <ModalForm
          key={`move-${selected.id}`}
          title={`移动 - ${selected.displayName}`}
          open={moveModalOpen}
          onOpenChange={setMoveModalOpen}
          modalProps={{ destroyOnHidden: true }}
          onFinish={async (values) => {
            // 留空＝移到根级（后端 ParentId 可空）
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

/** 移动目标选择：ProFormTreeSelect 直接绑定表单值；留空＝移到根级，标注当前父节点 */
const MoveTargetSelect: React.FC<{
  treeData: any[];
  currentParentId?: string;
}> = ({ treeData, currentParentId }) => {
  return (
    <>
      <div style={{ marginBottom: 8, color: 'rgba(0,0,0,0.45)' }}>
        选择新的父组织单元（已排除自身及其子树）；留空＝移到根级。
      </div>
      <ProFormTreeSelect
        name="targetParentId"
        label="目标父组织单元"
        allowClear
        placeholder="留空＝移到根级"
        fieldProps={{
          treeData,
          treeDefaultExpandAll: true,
          style: { width: '100%' },
          titleRender: (node: any) => (
            <span>
              {node.title}
              {node.key === currentParentId ? (
                <span style={{ color: 'rgba(0,0,0,0.45)' }}>（当前）</span>
              ) : null}
            </span>
          ),
        }}
      />
    </>
  );
};

function toNode(
  unit: OrganizationUnitDto,
  all: OrganizationUnitDto[],
  excluded?: Set<string>,
): any {
  if (excluded?.has(unit.id)) {
    return null;
  }
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
  // parentId→children 一次构建后栈式遍历 O(N)；逐节点 filter 全表是 O(N²)
  const childrenByParent = new Map<string, string[]>();
  for (const item of all) {
    if (!item.parentId) {
      continue;
    }
    const siblings = childrenByParent.get(item.parentId);
    if (siblings) {
      siblings.push(item.id);
    } else {
      childrenByParent.set(item.parentId, [item.id]);
    }
  }

  const descendants: string[] = [];
  const stack = childrenByParent.get(unit.id) ?? [];
  while (stack.length > 0) {
    const id = stack.pop()!;
    descendants.push(id);
    const grandchildren = childrenByParent.get(id);
    if (grandchildren) {
      stack.push(...grandchildren);
    }
  }
  return descendants;
}

export default OrganizationUnitsPage;
