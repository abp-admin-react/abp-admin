import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProFormText,
  ProFormTextArea,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm } from 'antd';
import React, { useRef } from 'react';
import {
  createOpenIddictScope,
  deleteOpenIddictScope,
  getOpenIddictScopes,
  type OpenIddictScopeDto,
  updateOpenIddictScope,
} from '@/abp/openIddictApplications';

type ScopeFormValues = {
  name: string;
  displayName?: string;
  description?: string;
  resources?: string;
};

/** 新建/编辑共用的 4 个表单字段（name/displayName/description/resources），两处复用避免漂移 */
const ScopeFormFields: React.FC = () => (
  <>
    <ProFormText name="name" label="名称" rules={[{ required: true }]} />
    <ProFormText name="displayName" label="显示名" />
    <ProFormTextArea name="description" label="说明" />
    <ProFormTextArea name="resources" label="Resources" />
  </>
);

const ScopesPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  return (
    <PageContainer>
      <ProTable<OpenIddictScopeDto>
        rowKey="id"
        actionRef={actionRef}
        search={false}
        columns={[
          { title: '名称', dataIndex: 'name' },
          { title: '显示名', dataIndex: 'displayName' },
          { title: '说明', dataIndex: 'description' },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => [
              ...(access.canUpdateOpenIddictScope
                ? [
                    <ModalForm<ScopeFormValues>
                      key="edit"
                      title="编辑范围"
                      trigger={<a>编辑</a>}
                      initialValues={record}
                      onFinish={async (values) => {
                        await updateOpenIddictScope(record.id, values);
                        message.success('已更新');
                        actionRef.current?.reload();
                        return true;
                      }}
                    >
                      <ScopeFormFields />
                    </ModalForm>,
                  ]
                : []),
              ...(access.canDeleteOpenIddictScope
                ? [
                    <Popconfirm
                      key="delete"
                      title="确认删除？"
                      onConfirm={async () => {
                        await deleteOpenIddictScope(record.id);
                        message.success('已删除');
                        actionRef.current?.reload();
                      }}
                    >
                      <a>删除</a>
                    </Popconfirm>,
                  ]
                : []),
            ],
          },
        ]}
        request={async (params) => {
          const result = await getOpenIddictScopes(params);
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() =>
          access.canCreateOpenIddictScope
            ? [
                <ModalForm<ScopeFormValues>
                  key="create"
                  title="新建范围"
                  trigger={<Button type="primary">新建</Button>}
                  onFinish={async (values) => {
                    await createOpenIddictScope(values);
                    message.success('已创建');
                    actionRef.current?.reload();
                    return true;
                  }}
                >
                  <ScopeFormFields />
                </ModalForm>,
              ]
            : []
        }
      />
    </PageContainer>
  );
};

export default ScopesPage;
