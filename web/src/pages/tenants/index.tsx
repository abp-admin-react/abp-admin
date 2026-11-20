import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Popconfirm,
  Tag,
} from 'antd';
import React, { useState } from 'react';
import { getAllEditions } from '@/abp/proModules';
import { applyTenantPackage } from '@/abp/tenantPackages';
import { createTenant, deleteTenant, queryTenants } from '@/abp/tenants';
import type { CreateTenantInput, TenantDto } from '@/abp/types';
import FeatureModal from '@/components/FeatureModal';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useDictionary } from '@/hooks/useDictionary';
import FrameworkConfiguration from '@/pages/settings/FrameworkConfiguration';
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

const CreateTenantModal: React.FC<{
  onSuccess: () => void;
  canApplyPackage: boolean;
}> = ({ onSuccess, canApplyPackage }) => {
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const { options, loading } = useTenantPackageOptions(open && canApplyPackage);

  return (
    <ModalForm<CreateTenantFormInput>
      title="新建租户"
      trigger={<Button type="primary">新建租户</Button>}
      open={open}
      onOpenChange={setOpen}
      // R3 测试 D7：弹窗默认保留上次提交值，连续新建时表单残留旧租户数据；
      // destroyOnHidden 关闭即卸载，重开回到空表单（与 menus/OU 弹窗语义一致）
      modalProps={{ destroyOnHidden: true }}
      onFinish={async (values) => {
        const { packageId, ...input } = values;
        const tenant = await createTenant(input);
        if (canApplyPackage && packageId) {
          // 按套餐初始化租户菜单树；失败不影响租户创建结果
          try {
            await applyTenantPackage(tenant.id, packageId);
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
      {canApplyPackage && (
        <ProFormSelect
          name="packageId"
          label="租户套餐（可选）"
          placeholder="不选则下发全量菜单"
          options={options}
          fieldProps={{ showSearch: true, optionFilterProp: 'label', loading }}
        />
      )}
    </ModalForm>
  );
};

const TenantsPage: React.FC<{ configurationEntry?: React.ReactNode }> = ({
  configurationEntry,
}) => {
  const access = useAccess();
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const [featureTarget, setFeatureTarget] = useState<TenantDto>();
  // T3.4：激活状态的文案与颜色来自数据字典（字典项 Code 是枚举数值字符串）
  const activationDict = useDictionary('TenantActivationState');
  const activationByCode = new Map(
    activationDict.items.map((it) => [it.code, it]),
  );

  const reload = () => grid.refresh();

  // 版本列的 Id→名称映射（lookup 需要 AbpAdmin.Editions 权限，无权限时回退显示原始 Id）
  const [editions, setEditions] = useState<
    { id: string; displayName: string }[]
  >([]);
  React.useEffect(() => {
    if (!access.canManageEditions) return;
    let active = true;
    getAllEditions()
      .then((res) => {
        if (active) setEditions(res.items ?? []);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [access.canManageEditions]);
  const editionNames = new Map(
    editions.map((edition) => [edition.id, edition.displayName]),
  );
  const canApplyPackage =
    access.canUpdateTenants && access.canManageTenantPackages;

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
    },
    {
      title: 'Id',
      dataIndex: 'id',
      search: false,
      copyable: true,
    },
    {
      title: '版本',
      key: 'edition',
      search: false,
      render: (_, record) => {
        const editionId = record.extraProperties?.[TENANT_PROP.EditionId];
        if (!editionId) return '-';
        return editionNames.get(String(editionId)) ?? String(editionId);
      },
    },
    {
      title: '激活状态',
      key: 'activationState',
      search: false,
      render: (_, record) =>
        activationTag(
          Number(record.extraProperties?.[TENANT_PROP.ActivationState] ?? 0),
        ),
    },
    {
      title: '版本到期时间',
      key: 'editionEnd',
      search: false,
      render: (_, record) =>
        record.extraProperties?.[TENANT_PROP.EditionEndDateUtc] || '-',
    },
    {
      title: '操作',
      valueType: 'option',
      width: 360,
      render: (_, record) => [
        access.canUpdateTenants ? (
          <EditTenantModal key="edit" record={record} onSuccess={reload} />
        ) : null,
        access.canUpdateEditions ? (
          <TenantEditionForm
            key="edition"
            record={record}
            editions={editions}
            onSuccess={reload}
          />
        ) : null,
        access.canManageTenantFeatures ? (
          <a key="features" onClick={() => setFeatureTarget(record)}>
            功能
          </a>
        ) : null,
        canApplyPackage ? (
          <ApplyPackageModal
            key="apply-package"
            record={record}
            onSuccess={reload}
          />
        ) : null,
        access.canImpersonateTenants ? (
          <ImpersonateAction key="impersonate" record={record} />
        ) : null,
        access.canManageTenantConnectionStrings && connectionStringEnabled ? (
          <ConnectionStringDrawer
            key="cs"
            tenantId={record.id}
            tenantName={record.name}
          />
        ) : null,
        access.canDeleteTenants ? (
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
          </Popconfirm>
        ) : null,
      ],
    },
  ];

  const grid = useSearchableRemoteGrid<TenantDto>({
    columnDefs: toGridColumns<TenantDto>(columns, {
      pro: true,
      kinds: {
        id: 'uuid',
        edition: 'text',
        activationState: 'number',
        editionEnd: 'date',
      },
    }),
    ariaLabel: '租户',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryTenants,
  });

  return (
    <PageContainer title="租户" extra={configurationEntry}>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {access.canCreateTenants
          ? [
              <CreateTenantModal
                key="create"
                canApplyPackage={canApplyPackage}
                onSuccess={reload}
              />,
            ]
          : undefined}
      </GridControls>
      <AgGridReact<TenantDto> {...grid.gridProps} />

      <FeatureModal
        open={!!featureTarget}
        title={`功能 - ${featureTarget?.name || ''}`}
        providerName="T"
        providerKey={featureTarget?.id}
        onClose={() => setFeatureTarget(undefined)}
      />
    </PageContainer>
  );
};

export default function Page() {
  return (
    <FrameworkConfiguration owner="tenant">
      {(configurationEntry) => (
        <TenantsPage configurationEntry={configurationEntry} />
      )}
    </FrameworkConfiguration>
  );
}
