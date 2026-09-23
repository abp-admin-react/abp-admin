import {
  ModalForm,
  ProFormDigit,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App } from 'antd';
import React, { useMemo } from 'react';
import { menuIconNames } from '@/abp/menuIcons';
import {
  createMenu,
  MENU_TYPE,
  type MenuCreateDto,
  updateMenu,
} from '@/abp/menus';
import { routeRegistry } from '../routeRegistry';
import type {
  MenuEditTarget,
  MenuFormValues,
  TreeSelectNode,
} from './menuTypes';

type MenuFormModalProps = {
  target: MenuEditTarget | undefined;
  menuTreeData: TreeSelectNode[];
  permissionTreeData: TreeSelectNode[];
  onClose: () => void;
  onSaved: () => void;
};

/** 菜单新增/编辑弹窗（12 个表单项），从 index.tsx 抽离。
 * key/destroyOnClose 语义保持原样：不同编辑目标重挂载，避免表单残留旧值。 */
const MenuFormModal: React.FC<MenuFormModalProps> = ({
  target,
  menuTreeData,
  permissionTreeData,
  onClose,
  onSaved,
}) => {
  const { message } = App.useApp();

  const initialValues = useMemo<MenuFormValues | undefined>(() => {
    if (!target) return undefined;
    if (target.mode === 'create') {
      return {
        parentId: target.parent?.id ?? null,
        type: MENU_TYPE.Menu,
        title: '',
        orderNo: 100,
        isHide: false,
        isEnabled: true,
      };
    }
    const node = target.node;
    return {
      parentId: node.parentId ?? null,
      type: node.type,
      title: node.title,
      name: node.name ?? undefined,
      path: node.path ?? undefined,
      icon: node.icon ?? undefined,
      orderNo: node.orderNo,
      permissionName: node.permissionName ?? undefined,
      isHide: node.isHide,
      isEnabled: node.isEnabled,
      remark: node.remark ?? undefined,
    };
  }, [target]);

  const submitForm = async (values: MenuFormValues) => {
    const payload: MenuCreateDto = {
      parentId: values.parentId ?? null,
      type: values.type,
      title: values.title,
      name: values.name ?? null,
      path: values.path ?? null,
      icon: values.icon ?? null,
      orderNo: values.orderNo,
      permissionName: values.permissionName ?? null,
      isHide: values.isHide,
      isEnabled: values.isEnabled,
      remark: values.remark ?? null,
    };
    if (target?.mode === 'edit') {
      // 乐观并发：携带加载时的并发戳，服务端检测到他人修改会返回 409
      await updateMenu(target.node.id, {
        ...payload,
        concurrencyStamp: target.node.concurrencyStamp ?? null,
      });
      message.success('菜单已更新');
    } else {
      await createMenu(payload);
      message.success('菜单已创建');
    }
    onClose();
    onSaved();
    return true;
  };

  return (
    <ModalForm<MenuFormValues>
      title={
        target?.mode === 'edit'
          ? `编辑菜单 - ${target.node.title}`
          : `新增菜单${target?.parent ? `（上级：${target.parent.title}）` : ''}`
      }
      width={560}
      open={!!target}
      initialValues={initialValues}
      key={target?.mode === 'edit' ? `edit-${target.node.id}` : 'create'}
      modalProps={{
        destroyOnHidden: true,
        onCancel: onClose,
      }}
      onFinish={submitForm}
    >
      <ProFormSelect
        name="parentId"
        label="上级菜单"
        placeholder="顶级"
        allowClear
        request={async () => menuTreeData}
        fieldProps={{ treeDefaultExpandAll: true, treeLine: true }}
      />
      <ProFormSelect
        name="type"
        label="类型"
        options={[
          { value: MENU_TYPE.Catalog, label: '目录（分组）' },
          { value: MENU_TYPE.Menu, label: '菜单（页面）' },
        ]}
        rules={[{ required: true }]}
      />
      <ProFormText
        name="title"
        label="显示名"
        placeholder="如：用户"
        rules={[{ required: true, message: '请输入显示名' }]}
      />
      <ProFormText
        name="name"
        label="国际化 key（可选）"
        placeholder="如 users，前端按 menu.{上级}.{name} 查语言包"
      />
      <ProFormSelect
        name="path"
        label="路由地址"
        placeholder="菜单类型必填，从注册表选择"
        fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
        options={routeRegistry.map((x) => ({
          value: x.path,
          label: `${x.label} (${x.path})`,
        }))}
      />
      <ProFormSelect
        name="icon"
        label="图标"
        placeholder="选择图标"
        fieldProps={{ showSearch: true }}
        options={menuIconNames.map((x) => ({ value: x, label: x }))}
      />
      <ProFormDigit
        name="orderNo"
        label="排序"
        min={0}
        max={9999}
        fieldProps={{ precision: 0 }}
      />
      <ProFormSelect
        name="permissionName"
        label="绑定权限（可选）"
        placeholder="绑定后需该权限授予才可见"
        fieldProps={{
          showSearch: true,
          treeData: permissionTreeData,
          treeDefaultExpandAll: true,
          treeLine: true,
          treeNodeFilterProp: 'title',
        }}
      />
      <ProFormSwitch
        name="isHide"
        label="隐藏"
        extra="不出现在侧边菜单，但页面路由可达"
      />
      <ProFormSwitch
        name="isEnabled"
        label="启用"
        extra="停用后整棵子树不可见"
      />
      <ProFormText name="remark" label="备注" placeholder="可选" />
    </ModalForm>
  );
};

export default MenuFormModal;
