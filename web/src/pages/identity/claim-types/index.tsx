import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
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
  type ClaimTypeDto,
  createClaimType,
  deleteClaimType,
  queryClaimTypes,
  updateClaimType,
} from '@/abp/identityAdmin';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const ClaimTypesPage: React.FC = () => {
  // App.useApp() 消费 ConfigProvider 主题上下文，替代不推荐的静态 message（重构报告问题 20）
  const { message } = App.useApp();
  const access = useAccess();
  const columns: ProColumns<ClaimTypeDto>[] = [
    { title: '名称', dataIndex: 'name' },
    { title: '说明', dataIndex: 'description', ellipsis: true, search: false },
    {
      title: '必填',
      dataIndex: 'required',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    {
      title: '静态',
      dataIndex: 'isStatic',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    { title: '正则', dataIndex: 'regex', search: false },
    {
      title: '操作',
      valueType: 'option',
      render: (_, record) => [
        !record.isStatic && access.canUpdateClaimTypes && (
          <ModalForm
            key="edit"
            title="编辑声明类型"
            trigger={<a>编辑</a>}
            initialValues={record}
            onFinish={async (values) => {
              await updateClaimType(record.id, {
                ...values,
                name: record.name,
                valueType: record.valueType,
              });
              message.success('已更新');
              grid.refresh();
              return true;
            }}
          >
            <ProFormText name="name" label="名称" disabled />
            <ProFormSwitch name="required" label="必填" />
            <ProFormText name="regex" label="正则" />
            <ProFormText name="description" label="说明" />
          </ModalForm>
        ),
        !record.isStatic && access.canDeleteClaimTypes && (
          <Popconfirm
            key="delete"
            title="确认删除？"
            onConfirm={async () => {
              await deleteClaimType(record.id);
              message.success('已删除');
              grid.refresh();
            }}
          >
            <a>删除</a>
          </Popconfirm>
        ),
      ],
    },
  ];

  const grid = useSearchableRemoteGrid<ClaimTypeDto>({
    columnDefs: toGridColumns<ClaimTypeDto>(columns, {
      pro: true,
      kinds: { required: 'boolean', isStatic: 'boolean' },
    }),
    ariaLabel: '声明类型',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryClaimTypes,
  });

  return (
    <PageContainer title="用户身份属性">
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {[
          access.canCreateClaimTypes ? (
            <ModalForm
              key="create"
              title="新建声明类型"
              trigger={<Button type="primary">新建</Button>}
              onFinish={async (values) => {
                await createClaimType(values as { name: string });
                message.success('已创建');
                grid.refresh();
                return true;
              }}
            >
              <ProFormText
                name="name"
                label="名称"
                rules={[{ required: true }]}
              />
              <ProFormSelect
                name="valueType"
                label="值类型"
                initialValue={0}
                options={[
                  { label: 'String', value: 0 },
                  { label: 'Int', value: 1 },
                  { label: 'Boolean', value: 2 },
                  { label: 'DateTime', value: 3 },
                ]}
              />
              <ProFormSwitch name="required" label="必填" />
              <ProFormText name="regex" label="正则" />
              <ProFormText name="description" label="说明" />
            </ModalForm>
          ) : (
            <React.Fragment key="create-placeholder" />
          ),
        ]}
      </GridControls>
      <AgGridReact<ClaimTypeDto> {...grid.gridProps} />
    </PageContainer>
  );
};

export default ClaimTypesPage;
