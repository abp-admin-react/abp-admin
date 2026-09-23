import { ModalForm } from '@ant-design/pro-components';
import { App, TreeSelect } from 'antd';
import React, { useEffect, useState } from 'react';
import type { IdentityRoleDto } from '@/abp/identity';
import {
  getMenuRoleGrants,
  type MenuTreeDto,
  updateMenuRoleGrants,
} from '@/abp/menus';

type RoleGrantModalProps = {
  target: MenuTreeDto | undefined;
  roles: IdentityRoleDto[];
  onClose: () => void;
  onSaved: () => void;
};

/**
 * 菜单的角色分配弹窗，从 index.tsx 抽离。
 * 内聚一条值得测试保护的隐性约束：读取当前授权失败时必须关闭弹窗——
 * 保存是全量覆盖语义，带着"空选中"让管理员顺手保存会静默清空该菜单的全部角色可见性。
 */
const RoleGrantModal: React.FC<RoleGrantModalProps> = ({
  target,
  roles,
  onClose,
  onSaved,
}) => {
  const { message } = App.useApp();
  const [grantedRoles, setGrantedRoles] = useState<string[]>([]);
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);

  useEffect(() => {
    if (!target) {
      return;
    }

    let cancelled = false;
    setSelectedRoles([]);
    (async () => {
      try {
        const res = await getMenuRoleGrants(target.id);
        if (cancelled) return;
        setGrantedRoles(res.items);
        setSelectedRoles(res.items);
      } catch {
        if (cancelled) return;
        // 读取当前授权失败：关闭弹窗（防"空选中 + 全量覆盖保存"静默清空）
        setGrantedRoles([]);
        onClose();
        message.error('读取当前角色分配失败，请重试');
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target?.id]);

  const saveRoles = async () => {
    if (!target) return;
    await updateMenuRoleGrants(target.id, selectedRoles);
    message.success('角色分配已保存');
    onClose();
    onSaved();
  };

  return (
    <ModalForm
      title={`分配角色 - ${target?.title ?? ''}`}
      width={480}
      open={!!target}
      key={target?.id}
      modalProps={{
        destroyOnHidden: true,
        onCancel: onClose,
      }}
      onFinish={saveRoles}
      submitter={{ searchConfig: { submitText: '保存' } }}
    >
      <div style={{ marginBottom: 8, color: 'rgba(0,0,0,0.45)' }}>
        勾选的角色可见该菜单；一个角色都不勾时，该菜单不受角色控制（按权限/公开规则显示）。
        当前已分配：{grantedRoles.length > 0 ? grantedRoles.join('、') : '无'}
      </div>
      <TreeSelect
        treeData={roles.map((x) => ({
          title: x.name,
          value: x.name,
          key: x.id,
        }))}
        value={selectedRoles}
        onChange={setSelectedRoles}
        treeCheckable
        showCheckedStrategy={TreeSelect.SHOW_ALL}
        treeDefaultExpandAll
        placeholder="选择可见该菜单的角色"
        style={{ width: '100%' }}
      />
    </ModalForm>
  );
};

export default RoleGrantModal;
