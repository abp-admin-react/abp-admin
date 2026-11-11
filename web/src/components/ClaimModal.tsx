import {
  ModalForm,
  ProFormList,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { App } from 'antd';
import React, { useState } from 'react';
import {
  type ClaimValueDto,
  getAllClaimTypes,
  getRoleClaims,
  getUserClaims,
  updateRoleClaims,
  updateUserClaims,
} from '@/abp/identityAdmin';

const ClaimModal: React.FC<{
  title: string;
  userId?: string;
  roleId?: string;
  onClose: () => void;
}> = ({ title, userId, roleId, onClose }) => {
  const { message } = App.useApp();
  const [claimTypeOptions, setClaimTypeOptions] = useState<
    { label: string; value: string }[]
  >([]);

  return (
    <ModalForm
      title={title}
      open
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      request={async () => {
        const [types, claims] = await Promise.all([
          getAllClaimTypes(),
          userId
            ? getUserClaims(userId)
            : roleId
              ? getRoleClaims(roleId)
              : Promise.resolve([]),
        ]);
        setClaimTypeOptions(
          types.map(({ name }) => ({ label: name, value: name })),
        );
        return { claims };
      }}
      onFinish={async (values) => {
        const claims = ((values.claims || []) as ClaimValueDto[]).filter(
          (item) => item.claimType,
        );
        if (userId) {
          await updateUserClaims(userId, claims);
        } else if (roleId) {
          await updateRoleClaims(roleId, claims);
        }
        message.success('声明已保存');
        onClose();
        return true;
      }}
    >
      <ProFormList
        name="claims"
        creatorButtonProps={{ creatorButtonText: '添加声明' }}
        copyIconProps={false}
        deleteIconProps={{ tooltipText: '删除' }}
      >
        <ProFormSelect
          name="claimType"
          label="类型"
          width="sm"
          options={claimTypeOptions}
          rules={[{ required: true, message: '请选择声明类型' }]}
        />
        <ProFormText name="claimValue" label="值" width="md" />
      </ProFormList>
    </ModalForm>
  );
};

export default ClaimModal;
