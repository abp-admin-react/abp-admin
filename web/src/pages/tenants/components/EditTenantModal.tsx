import {
  ModalForm,
  ProFormDateTimePicker,
  ProFormDependency,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { App } from 'antd';
import dayjs from 'dayjs';
import React from 'react';
import { updateTenant } from '@/abp/tenants';
import type { TenantDto } from '@/abp/types';
import { useDictionary } from '@/hooks/useDictionary';
import { TENANT_PROP } from '../constants';

/** T2.8：租户激活状态三态的颜色/文案兜底（字典 TenantActivationState 未种子时使用）。
 * 正常路径下颜色与文案以后端字典为准（T3.4：规则只在后端定义一处）。 */
const ACTIVATION_STATES: Record<number, { color: string; text: string }> = {
  0: { color: 'green', text: '激活' },
  1: { color: 'orange', text: '限时激活' },
  2: { color: 'red', text: '已停用' },
};

const toUtcIso = (value?: string) =>
  value ? dayjs(value).toISOString() : null;

type EditTenantModalProps = {
  record: TenantDto;
  onSuccess: () => void;
};

/** 编辑租户（名称 + 激活状态 + 到期时间），从首页操作列抽离。 */
const EditTenantModal: React.FC<EditTenantModalProps> = ({
  record,
  onSuccess,
}) => {
  const { message } = App.useApp();
  // T3.4：激活状态的文案与颜色来自数据字典（字典项 Code 是枚举数值字符串）
  const activationDict = useDictionary('TenantActivationState');

  return (
    <ModalForm
      title="编辑租户"
      trigger={<a>编辑</a>}
      initialValues={{
        name: record.name,
        activationState: Number(
          record.extraProperties?.[TENANT_PROP.ActivationState] ?? 0,
        ),
        activationEndDate:
          record.extraProperties?.[TENANT_PROP.ActivationEndDate] || undefined,
        editionEndDateUtc:
          record.extraProperties?.[TENANT_PROP.EditionEndDateUtc] || undefined,
      }}
      onFinish={async (values) => {
        await updateTenant(record.id, {
          name: values.name,
          concurrencyStamp: record.concurrencyStamp,
          extraProperties: {
            [TENANT_PROP.ActivationState]: values.activationState ?? 0,
            [TENANT_PROP.ActivationEndDate]:
              values.activationState === 1
                ? toUtcIso(values.activationEndDate)
                : null,
            [TENANT_PROP.EditionEndDateUtc]: toUtcIso(values.editionEndDateUtc),
          },
        });
        message.success('已更新');
        onSuccess();
        return true;
      }}
    >
      <ProFormText
        name="name"
        label="名称"
        rules={[{ required: true, message: '请输入租户名称' }]}
      />
      <ProFormSelect
        name="activationState"
        label="激活状态"
        allowClear={false}
        // T3.4：选项来自字典；字典未种子时回落硬编码三态。表单值保持数值（与 extraProperties 契约一致）
        options={
          activationDict.items.length > 0
            ? activationDict.items.map((it) => ({
                label: it.displayText,
                value: Number(it.code),
              }))
            : Object.entries(ACTIVATION_STATES).map(([value, config]) => ({
                label: config.text,
                value: Number(value),
              }))
        }
      />
      <ProFormDependency name={['activationState']}>
        {({ activationState }) =>
          activationState === 1 ? (
            <ProFormDateTimePicker
              name="activationEndDate"
              label="激活到期时间"
              rules={[{ required: true, message: '请选择激活到期时间' }]}
            />
          ) : null
        }
      </ProFormDependency>
      <ProFormDateTimePicker
        name="editionEndDateUtc"
        label="版本到期时间"
        tooltip="到期后该租户的有效版本失效，功能回落；版本关联仍保留，便于续费恢复"
      />
    </ModalForm>
  );
};

export { ACTIVATION_STATES };
export default EditTenantModal;
