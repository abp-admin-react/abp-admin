import { ModalForm, ProFormSelect } from '@ant-design/pro-components';
import { App } from 'antd';
import React from 'react';
import { applyTenantPackage } from '@/abp/tenantPackages';
import type { TenantDto } from '@/abp/types';
import { useTenantPackageOptions } from '../hooks/useTenantPackageOptions';

type ApplyPackageModalProps = {
  record: TenantDto;
  onSuccess: () => void;
};

/** 应用套餐（破坏性：租户菜单树重置为套餐内容），从首页操作列抽离。 */
const ApplyPackageModal: React.FC<ApplyPackageModalProps> = ({
  record,
  onSuccess,
}) => {
  const { message } = App.useApp();
  const { options } = useTenantPackageOptions();

  return (
    <ModalForm<{ packageId?: string | null }>
      title={`应用套餐 - ${record.name}`}
      trigger={<a>应用套餐</a>}
      initialValues={{ packageId: null }}
      onFinish={async (values) => {
        await applyTenantPackage(record.id, values.packageId ?? null);
        message.success('套餐已应用，租户菜单树已重置');
        onSuccess();
        return true;
      }}
    >
      <div style={{ marginBottom: 12, color: '#faad14' }}>
        破坏性操作：将清空该租户现有菜单与菜单角色勾选，重置为套餐内容。
      </div>
      <ProFormSelect
        name="packageId"
        label="套餐"
        placeholder="不选 = 重置为全量菜单"
        allowClear
        options={options}
        fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
      />
    </ModalForm>
  );
};

export default ApplyPackageModal;
