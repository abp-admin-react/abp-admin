import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  Alert,
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
} from 'antd';
import React, { useState } from 'react';
import {
  createEdition,
  deleteEdition,
  getAllEditions,
  getEditionTenantCount,
  queryEditions,
  updateEdition,
} from '@/abp/proModules';
import FeatureModal from '@/components/FeatureModal';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

type EditionItem = { id: string; displayName: string };

const EditionsPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const [featureTarget, setFeatureTarget] = useState<EditionItem>();

  const columns: ProColumns<EditionItem>[] = [
    { title: '名称', dataIndex: 'displayName' },
    {
      title: '操作',
      valueType: 'option',
      render: (_, record) => [
        access.canUpdateEditions ? (
          <ModalForm
            key="edit"
            title="编辑版本"
            trigger={<a>编辑</a>}
            initialValues={record}
            onFinish={async (values) => {
              await updateEdition(record.id, {
                displayName: values.displayName,
              });
              message.success('已更新');
              grid.refresh();
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
        access.canManageEditionFeatures ? (
          <a key="features" onClick={() => setFeatureTarget(record)}>
            功能
          </a>
        ) : null,
        access.canDeleteEditions ? (
          <DeleteEditionForm
            key="delete"
            record={record}
            onSuccess={() => grid.refresh()}
          />
        ) : null,
      ],
    },
  ];

  const grid = useSearchableRemoteGrid<EditionItem>({
    columnDefs: toGridColumns<EditionItem>(columns, { pro: true, kinds: {} }),
    ariaLabel: '版本',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryEditions,
  });

  return (
    <PageContainer title="租户功能版本">
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {access.canCreateEditions
          ? [
              <ModalForm
                key="create"
                title="新建版本"
                trigger={<Button type="primary">新建</Button>}
                onFinish={async (values) => {
                  await createEdition({ displayName: values.displayName });
                  message.success('已创建');
                  grid.refresh();
                  return true;
                }}
              >
                <ProFormText
                  name="displayName"
                  label="名称"
                  rules={[{ required: true }]}
                />
              </ModalForm>,
            ]
          : undefined}
      </GridControls>
      <AgGridReact<EditionItem> {...grid.gridProps} />

      <FeatureModal
        open={!!featureTarget}
        title={`功能 - ${featureTarget?.displayName || ''}`}
        providerName="E"
        providerKey={featureTarget?.id}
        onClose={() => setFeatureTarget(undefined)}
      />
    </PageContainer>
  );
};

/** T2.8 SaaS Pro 缺口第 5 项：删除版本时迁移租户 */
const DeleteEditionForm: React.FC<{
  record: EditionItem;
  onSuccess: () => void;
}> = ({ record, onSuccess }) => {
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [tenantCount, setTenantCount] = useState<number>();
  const [editions, setEditions] = useState<EditionItem[]>([]);

  return (
    <ModalForm
      title={`删除版本 - ${record.displayName}`}
      trigger={<a>删除</a>}
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setTenantCount(undefined);
      }}
      modalProps={{ destroyOnHidden: true }}
      submitter={{
        searchConfig: { submitText: '确认删除' },
        submitButtonProps: { disabled: tenantCount === undefined },
      }}
      request={async () => {
        const [count, result] = await Promise.all([
          getEditionTenantCount(record.id),
          getAllEditions(),
        ]);
        setTenantCount(count);
        setEditions(
          (result.items ?? []).filter((item) => item.id !== record.id),
        );
        return { moveTenantsToEditionId: undefined };
      }}
      onFinish={async (values) => {
        if (tenantCount === undefined) return false;
        await deleteEdition(
          record.id,
          values.moveTenantsToEditionId || undefined,
        );
        message.success('已删除');
        onSuccess();
        return true;
      }}
    >
      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        title={
          tenantCount === undefined
            ? '等待加载该版本的租户和可迁移版本。'
            : tenantCount > 0
              ? `该版本下有 ${tenantCount} 个租户，删除前请确认它们的去向。`
              : '该版本下没有租户。'
        }
      />
      <ProFormSelect
        name="moveTenantsToEditionId"
        label="将其租户迁移至"
        allowClear
        placeholder="留空则清空这些租户的版本分配"
        options={editions.map((item) => ({
          label: item.displayName,
          value: item.id,
        }))}
      />
    </ModalForm>
  );
};

export default EditionsPage;
