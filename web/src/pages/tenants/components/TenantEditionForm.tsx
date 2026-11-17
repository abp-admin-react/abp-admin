import { ModalForm, ProFormSelect } from '@ant-design/pro-components';
import { App } from 'antd';
import type React from 'react';
import { setTenantEdition } from '@/abp/proModules';
import type { TenantDto } from '@/abp/types';
import { TENANT_PROP } from '../constants';

type TenantEditionFormProps = {
  record: TenantDto;
  editions: { id: string; displayName: string }[];
  onSuccess: () => void;
};

/** T2.8：租户的版本分配表单，从首页操作列抽离。 */
const TenantEditionForm: React.FC<TenantEditionFormProps> = ({
  record,
  editions,
  onSuccess,
}) => {
  const { message } = App.useApp();
  return (
    <ModalForm
      title={`版本 - ${record.name}`}
      trigger={<a>版本</a>}
      initialValues={{
        editionId: record.extraProperties?.[TENANT_PROP.EditionId] || undefined,
      }}
      onFinish={async (values) => {
        await setTenantEdition(record.id, values.editionId || undefined);
        message.success('版本已保存');
        onSuccess();
        return true;
      }}
    >
      <ProFormSelect
        name="editionId"
        label="版本"
        allowClear
        options={editions.map((item) => ({
          label: item.displayName,
          value: item.id,
        }))}
      />
    </ModalForm>
  );
};

export default TenantEditionForm;
