import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm } from 'antd';
import React, { useRef } from 'react';
import {
  type ClaimTypeDto,
  createClaimType,
  deleteClaimType,
  getClaimTypes,
  updateClaimType,
} from '@/abp/identityAdmin';
import AutoHeightProTable from '@/components/AutoHeightProTable';

const ClaimTypesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  // App.useApp() 消费 ConfigProvider 主题上下文，替代不推荐的静态 message（重构报告问题 20）
  const { message } = App.useApp();
  const access = useAccess();
  const columns: ProColumns<ClaimTypeDto>[] = [
    { title: '名称', dataIndex: 'name' },
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
              await updateClaimType(record.id, values);
              message.success('已更新');
              actionRef.current?.reload();
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
              actionRef.current?.reload();
            }}
          >
            <a>删除</a>
          </Popconfirm>
        ),
      ],
    },
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<ClaimTypeDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={false}
        request={async (params) => {
          const result = await getClaimTypes(params);
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() => [
          access.canCreateClaimTypes ? (
            <ModalForm
              key="create"
              title="新建声明类型"
              trigger={<Button type="primary">新建</Button>}
              onFinish={async (values) => {
                await createClaimType(values as { name: string });
                message.success('已创建');
                actionRef.current?.reload();
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
      />
    </PageContainer>
  );
};

export default ClaimTypesPage;
