import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
import { App, Button, Popconfirm, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import { applyTenantPackage } from '@/abp/tenantPackages';
import { createTenant, deleteTenant, getTenants } from '@/abp/tenants';
import type { CreateTenantInput, TenantDto } from '@/abp/types';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import FeatureModal from '@/components/FeatureModal';
import { firstFilterValue, textFilter } from '@/components/tableColumnFilters';
import { useDictionary } from '@/hooks/useDictionary';
import ConnectionStringDrawer from './ConnectionStringDrawer';
import ApplyPackageModal from './components/ApplyPackageModal';
import EditTenantModal, {
  ACTIVATION_STATES,
} from './components/EditTenantModal';
import ImpersonateAction from './components/ImpersonateAction';
import TenantEditionForm from './components/TenantEditionForm';
import { TENANT_PROP } from './constants';
import { useTenantPackageOptions } from './hooks/useTenantPackageOptions';

/** 新建租户表单：开源契约 + 套餐选择（仅前端用，套餐在创建成功后单独应用） */
type CreateTenantFormInput = CreateTenantInput & { packageId?: string };

const CONNECTION_STRING_MANAGEMENT_SETTING =
  'AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement';

const CreateTenantModal: React.FC<{ onSuccess: () => void }> = ({
  onSuccess,
}) => {
  const { message } = App.useApp();
  const { options } = useTenantPackageOptions();

  return (
    <ModalForm<CreateTenantFormInput>
      title="新建租户"
      trigger={<Button type="primary">新建租户</Button>}
      // R3 测试 D7：弹窗默认保留上次提交值，连续新建时表单残留旧租户数据；
      // destroyOnHidden 关闭即卸载，重开回到空表单（与 menus/OU 弹窗语义一致）
      modalProps={{ destroyOnHidden: true }}
      onFinish={async (values) => {
        const tenant = await createTenant(values);
        if (values.packageId) {
          // 按套餐初始化租户菜单树；失败不影响租户创建结果
          try {
            await applyTenantPackage(tenant.id, values.packageId);
          } catch {
            message.warning('租户已创建，但套餐应用失败，可稍后在租户列表重试');
          }
        }
        message.success('已创建');
        onSuccess();
        return true;
      }}
    >
      <ProFormText
        name="name"
        label="租户名称"
        rules={[{ required: true, message: '请输入租户名称' }]}
      />
      <ProFormText
        name="adminEmailAddress"
        label="管理员邮箱"
        rules={[
          { required: true, message: '请输入管理员邮箱' },
          { type: 'email', message: '邮箱格式不正确' },
        ]}
      />
      <ProFormText.Password
        name="adminPassword"
        label="管理员密码"
        rules={[{ required: true, message: '请输入管理员密码' }]}
      />
      <ProFormSelect
        name="packageId"
        label="租户套餐（可选）"
        placeholder="不选则下发全量菜单"
        options={options}
        fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
      />
    </ModalForm>
  );
};

const TenantsPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  const access = useAccess();
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const [featureTarget, setFeatureTarget] = useState<TenantDto>();
  const [hostFeaturesOpen, setHostFeaturesOpen] = useState(false);
  // T3.4：激活状态的文案与颜色来自数据字典（字典项 Code 是枚举数值字符串）
  const activationDict = useDictionary('TenantActivationState');
  const activationByCode = new Map(
    activationDict.items.map((it) => [it.code, it]),
  );

  const reload = () => actionRef.current?.reload();

  // 设置项为 false 时整个连接字符串入口不渲染（默认 true）
  const connectionStringEnabled =
    initialState?.settingValues?.[CONNECTION_STRING_MANAGEMENT_SETTING] !==
    'false';

  const activationTag = (state: number) => {
    const item = activationByCode.get(String(state));
    const fallback = ACTIVATION_STATES[state] ?? ACTIVATION_STATES[0];
    return (
      <Tag color={item?.tagType ?? fallback.color}>
        {item?.displayText ?? fallback.text}
      </Tag>
    );
  };

  const columns: ProColumns<TenantDto>[] = [
    {
      title: '名称',
      dataIndex: 'name',
      ...textFilter('按租户名称筛选'),
    },
    {
      title: 'Id',
      dataIndex: 'id',
      search: false,
      copyable: true,
    },
    {
      title: '版本',
      search: false,
      render: (_, record) =>
        record.extraProperties?.[TENANT_PROP.EditionId] || '-',
    },
    {
      title: '激活状态',
      search: false,
      render: (_, record) =>
        activationTag(
          Number(record.extraProperties?.[TENANT_PROP.ActivationState] ?? 0),
        ),
    },
    {
      title: '版本到期时间',
      search: false,
      render: (_, record) =>
        record.extraProperties?.[TENANT_PROP.EditionEndDateUtc] || '-',
    },
    {
      title: '操作',
      valueType: 'option',
      width: 360,
      render: (_, record) => [
        <EditTenantModal key="edit" record={record} onSuccess={reload} />,
        access.canManageEditions ? (
          <TenantEditionForm key="edition" record={record} onSuccess={reload} />
        ) : null,
        access.canManageTenantFeatures ? (
          <a key="features" onClick={() => setFeatureTarget(record)}>
            功能
          </a>
        ) : null,
        access.canManageTenantPackages ? (
          <ApplyPackageModal
            key="apply-package"
            record={record}
            onSuccess={reload}
          />
        ) : null,
        access.canImpersonateTenants ? (
          <ImpersonateAction key="impersonate" record={record} />
        ) : null,
        connectionStringEnabled ? (
          <ConnectionStringDrawer
            key="cs"
            tenantId={record.id}
            tenantName={record.name}
          />
        ) : null,
        <Popconfirm
          key="delete"
          title="确认删除该租户？"
          onConfirm={async () => {
            await deleteTenant(record.id);
            message.success('已删除');
            reload();
          }}
        >
          <a>删除</a>
        </Popconfirm>,
      ],
    },
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<TenantDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={false}
        request={async (params, _sorter, filter) => {
          const result = await getTenants({
            current: params.current,
            pageSize: params.pageSize,
            filter: firstFilterValue(filter, 'name'),
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() => [
          access.canManageHostFeatures ? (
            <Button
              key="host-features"
              onClick={() => setHostFeaturesOpen(true)}
            >
              管理 Host 功能
            </Button>
          ) : null,
          <CreateTenantModal key="create" onSuccess={reload} />,
        ]}
      />
      <FeatureModal
        open={!!featureTarget}
        title={`功能 - ${featureTarget?.name || ''}`}
        providerName="T"
        providerKey={featureTarget?.id}
        onClose={() => setFeatureTarget(undefined)}
      />
      <FeatureModal
        open={hostFeaturesOpen}
        title="Host 功能"
        providerName="T"
        onClose={() => setHostFeaturesOpen(false)}
      />
    </PageContainer>
  );
};

export default TenantsPage;
