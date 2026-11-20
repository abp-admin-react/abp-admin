import {
  ModalForm,
  PageContainer,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Popconfirm,
} from 'antd';
import React from 'react';
import {
  createOpenIddictScope,
  deleteOpenIddictScope,
  type OpenIddictScopeDto,
  queryOpenIddictScopes,
  updateOpenIddictScope,
} from '@/abp/openIddictApplications';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

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
  const grid = useSearchableRemoteGrid<OpenIddictScopeDto>({
    columnDefs: toGridColumns<OpenIddictScopeDto>(
      [
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
                      grid.refresh();
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
                      grid.refresh();
                    }}
                  >
                    <a>删除</a>
                  </Popconfirm>,
                ]
              : []),
          ],
        },
      ],
      { pro: true, kinds: {} },
    ),
    ariaLabel: '作用域',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryOpenIddictScopes,
  });

  return (
    <PageContainer>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {access.canCreateOpenIddictScope
          ? [
              <ModalForm<ScopeFormValues>
                key="create"
                title="新建范围"
                trigger={<Button type="primary">新建</Button>}
                onFinish={async (values) => {
                  await createOpenIddictScope(values);
                  message.success('已创建');
                  grid.refresh();
                  return true;
                }}
              >
                <ScopeFormFields />
              </ModalForm>,
            ]
          : []}
      </GridControls>
      <AgGridReact<OpenIddictScopeDto> {...grid.gridProps} />
    </PageContainer>
  );
};

export default ScopesPage;
