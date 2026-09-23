import { PlusOutlined } from '@ant-design/icons';
import {
  ModalForm,
  PageContainer,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { Button, message, Popconfirm, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import {
  deleteApiAppLanguageId,
  getApiAppLanguage,
  postApiAppLanguage,
  postApiAppLanguageIdSetAsDefault,
  putApiAppLanguageId,
} from '@/services/abpadmin/language';

// .NET 标准 culture 清单
const cultureOptions = [
  { label: '中文（简体）', value: 'zh-Hans' },
  { label: '中文（繁体）', value: 'zh-Hant' },
  { label: 'English', value: 'en' },
  { label: 'English (United States)', value: 'en-US' },
  { label: 'English (United Kingdom)', value: 'en-GB' },
  { label: '日本語', value: 'ja' },
  { label: '한국어', value: 'ko' },
  { label: 'Français', value: 'fr' },
  { label: 'Deutsch', value: 'de' },
  { label: 'Español', value: 'es' },
  { label: 'Русский', value: 'ru' },
  { label: 'العربية', value: 'ar' },
  { label: 'Português', value: 'pt' },
  { label: 'Italiano', value: 'it' },
  { label: 'Nederlands', value: 'nl' },
  { label: 'Polski', value: 'pl' },
  { label: 'Türkçe', value: 'tr' },
  { label: 'Tiếng Việt', value: 'vi' },
  { label: 'ไทย', value: 'th' },
  { label: 'हिन्दी', value: 'hi' },
];

const LanguagesPage: React.FC = () => {
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [editModalOpen, setEditModalOpen] = useState(false);
  const [currentRecord, setCurrentRecord] = useState<API.LanguageDto>();
  const tableRef = useRef<any>(null);
  const access = useAccess();

  const handleCreate = async (values: API.CreateLanguageDto) => {
    try {
      await postApiAppLanguage(values);
      message.success('创建成功');
      setCreateModalOpen(false);
      tableRef.current?.reload();
      return true;
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '创建失败');
      return false;
    }
  };

  const handleUpdate = async (values: API.UpdateLanguageDto) => {
    if (!currentRecord?.id) return false;
    try {
      await putApiAppLanguageId({ id: currentRecord.id }, values);
      message.success('更新成功');
      setEditModalOpen(false);
      setCurrentRecord(undefined);
      tableRef.current?.reload();
      return true;
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '更新失败');
      return false;
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteApiAppLanguageId({ id });
      message.success('删除成功');
      tableRef.current?.reload();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '删除失败');
    }
  };

  const handleSetAsDefault = async (id: string) => {
    try {
      await postApiAppLanguageIdSetAsDefault({ id });
      message.success('设置默认语言成功');
      tableRef.current?.reload();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '设置失败');
    }
  };

  return (
    <PageContainer>
      <ProTable
        actionRef={tableRef}
        rowKey="id"
        columns={[
          { title: '显示名', dataIndex: 'displayName' },
          { title: 'Culture Name', dataIndex: 'cultureName' },
          { title: 'UI Culture Name', dataIndex: 'uiCultureName' },
          { title: '旗标', dataIndex: 'flagIcon' },
          {
            title: '状态',
            dataIndex: 'isEnabled',
            render: (_, record) => (
              <Tag color={record.isEnabled ? 'green' : 'red'}>
                {record.isEnabled ? '启用' : '禁用'}
              </Tag>
            ),
          },
          {
            title: '默认',
            dataIndex: 'isDefault',
            render: (_, record) =>
              record.isDefault ? <Tag color="blue">默认</Tag> : null,
          },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) =>
              [
                access.canManageLanguages && (
                  <a
                    key="edit"
                    onClick={() => {
                      setCurrentRecord(record);
                      setEditModalOpen(true);
                    }}
                  >
                    编辑
                  </a>
                ),
                access.canChangeDefaultLanguage &&
                  !record.isDefault &&
                  record.isEnabled && (
                    <a
                      key="default"
                      onClick={() => handleSetAsDefault(record.id!)}
                    >
                      设为默认
                    </a>
                  ),
                access.canManageLanguages && !record.isDefault && (
                  <Popconfirm
                    key="delete"
                    title="确定删除此语言？"
                    onConfirm={() => handleDelete(record.id!)}
                  >
                    <a style={{ color: 'red' }}>删除</a>
                  </Popconfirm>
                ),
              ].filter(Boolean),
          },
        ]}
        toolbar={{
          actions: access.canManageLanguages
            ? [
                <Button
                  key="create"
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => setCreateModalOpen(true)}
                >
                  新增语言
                </Button>,
              ]
            : [],
        }}
        request={async () => {
          const result = await getApiAppLanguage();
          return { data: result.items, success: true };
        }}
        search={false}
        pagination={false}
      />

      {/* 新增语言 Modal */}
      <ModalForm
        title="新增语言"
        open={createModalOpen}
        onOpenChange={setCreateModalOpen}
        onFinish={handleCreate}
        modalProps={{ destroyOnHidden: true }}
      >
        <ProFormSelect
          name="cultureName"
          label="Culture Name"
          options={cultureOptions}
          rules={[{ required: true, message: '请选择 Culture' }]}
          placeholder="选择 .NET 标准 Culture"
        />
        <ProFormSelect
          name="uiCultureName"
          label="UI Culture Name"
          options={cultureOptions}
          rules={[{ required: true, message: '请选择 UI Culture' }]}
          placeholder="选择 UI Culture"
        />
        <ProFormText
          name="displayName"
          label="显示名"
          rules={[{ required: true, message: '请输入显示名' }]}
          placeholder="如：中文（简体）"
        />
        <ProFormText name="flagIcon" label="旗标图标" placeholder="如：cn" />
        <ProFormSwitch name="isEnabled" label="启用" initialValue={true} />
      </ModalForm>

      {/* 编辑语言 Modal */}
      <ModalForm
        title="编辑语言"
        open={editModalOpen}
        onOpenChange={(open) => {
          setEditModalOpen(open);
          if (!open) setCurrentRecord(undefined);
        }}
        onFinish={handleUpdate}
        modalProps={{ destroyOnHidden: true }}
        initialValues={currentRecord}
      >
        <ProFormText name="cultureName" label="Culture Name" disabled />
        <ProFormText name="uiCultureName" label="UI Culture Name" disabled />
        <ProFormText
          name="displayName"
          label="显示名"
          rules={[{ required: true, message: '请输入显示名' }]}
        />
        <ProFormText name="flagIcon" label="旗标图标" />
        <ProFormSwitch
          name="isEnabled"
          label="启用"
          disabled={currentRecord?.isDefault}
        />
        {currentRecord?.isDefault && (
          <div
            style={{
              color: '#999',
              fontSize: 12,
              marginTop: -16,
              marginBottom: 16,
            }}
          >
            默认语言不能禁用
          </div>
        )}
      </ModalForm>
    </PageContainer>
  );
};

export default LanguagesPage;
