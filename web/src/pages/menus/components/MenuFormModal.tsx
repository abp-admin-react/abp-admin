import {
  ModalForm,
  ProFormDependency,
  ProFormDigit,
  ProFormRadio,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProFormTreeSelect,
} from '@ant-design/pro-components';
import { App, Button, Col, Form, Popover, Tooltip } from 'antd';
import React, { useMemo, useState } from 'react';
import { menuIconNames, toMenuIcon } from '@/abp/menuIcons';
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

type IconPickerProps = {
  value?: string;
  onChange?: (value: string | null) => void;
};

/** 图标选择器（借鉴 Admin.NET IconSelector）：面板网格平铺纯图标（8 列 × 32px，
 * 22 个图标 3 行放完），悬停 tooltip 显示名称，点选选中、再点同一个取消。
 * 作为受控组件接入 Form.Item（value/onChange 约定），与 ProForm 字段同数据流。 */
function IconPicker({ value, onChange }: IconPickerProps) {
  const [open, setOpen] = useState(false);
  return (
    <Popover
      open={open}
      onOpenChange={setOpen}
      trigger="click"
      placement="bottomLeft"
      styles={{ content: { padding: 8 } }}
      content={
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(8, 32px)',
            gap: 4,
          }}
        >
          {menuIconNames.map((name) => (
            <Tooltip key={name} title={name}>
              <Button
                type={value === name ? 'primary' : 'text'}
                icon={toMenuIcon(name)}
                onClick={() => {
                  onChange?.(value === name ? null : name);
                  setOpen(false);
                }}
                style={{
                  width: 32,
                  height: 32,
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: 0,
                }}
              />
            </Tooltip>
          ))}
        </div>
      }
    >
      <Button
        style={{
          width: '100%',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-start',
        }}
      >
        {value ? (
          toMenuIcon(value)
        ) : (
          <span style={{ color: 'rgba(0,0,0,0.25)' }}>选择图标</span>
        )}
      </Button>
    </Popover>
  );
}

/** 菜单新增/编辑弹窗，从 index.tsx 抽离。
 * 布局借鉴 Admin.NET editMenu.vue：横向标签 + 双列网格（grid + colProps），
 * 短字段两两一行、开关说明收进 label tooltip，保证 720p 视口下整窗免滚动。
 * key/destroyOnHidden 语义保持原样：不同编辑目标重挂载，避免表单残留旧值。 */
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
      // 目录类型归一掉残留的路由地址（表单切类型时不清空旧值）
      title: values.title,
      name: values.name ?? null,
      path: values.type === MENU_TYPE.Menu ? values.path ?? null : null,
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
      width={640}
      open={!!target}
      initialValues={initialValues}
      key={target?.mode === 'edit' ? `edit-${target.node.id}` : 'create'}
      grid
      // 横向标签：labelCol 固定宽度对齐最宽标签「国际化 key」，行高较纵向减半，
      // 这是 720p 免滚动的关键；双列短字段由各字段的 colProps span=12 组成
      layout="horizontal"
      labelCol={{ flex: '0 0 108px' }}
      wrapperCol={{ flex: '1' }}
      modalProps={{
        destroyOnHidden: true,
        onCancel: onClose,
        centered: true,
      }}
      onFinish={submitForm}
    >
      {/* 上级菜单必须用 TreeSelect：ProFormSelect 的 request 走普通 options 协议，
          title 不被识别（渲染成裸 id）、children 被拍平（只能选到根节点） */}
      <ProFormTreeSelect
        name="parentId"
        label="上级菜单"
        placeholder="顶级"
        allowClear
        colProps={{ span: 24 }}
        fieldProps={{
          treeData: menuTreeData,
          treeDefaultExpandAll: true,
          treeLine: true,
        }}
      />
      {/* Admin.NET 同款：类型用 radio 平铺，比下拉少一次点击且始终可见 */}
      <ProFormRadio.Group
        name="type"
        label="类型"
        colProps={{ span: 24 }}
        rules={[{ required: true }]}
        options={[
          { value: MENU_TYPE.Catalog, label: '目录（分组）' },
          { value: MENU_TYPE.Menu, label: '菜单（页面）' },
        ]}
      />
      <ProFormText
        name="title"
        label="显示名"
        placeholder="如：用户"
        colProps={{ span: 12 }}
        rules={[{ required: true, message: '请输入显示名' }]}
      />
      <ProFormText
        name="name"
        label="国际化 key"
        placeholder="如 users（可选）"
        colProps={{ span: 12 }}
      />
      {/* 路由地址仅菜单（页面）类型展示（Admin.NET/芋道的类型驱动字段收敛同款）——
          目录隐藏防误填；校验口径与后端 MenuTypeMismatch 一致 */}
      <ProFormDependency name={['type']}>
        {({ type }) =>
          type === MENU_TYPE.Menu ? (
            <ProFormSelect
              name="path"
              label="路由地址"
              placeholder="从注册表选择"
              colProps={{ span: 12 }}
              rules={[
                ({ getFieldValue }) => ({
                  validator: (_, value) =>
                    getFieldValue('type') === MENU_TYPE.Menu && !value
                      ? Promise.reject(new Error('菜单类型必须选择路由地址'))
                      : Promise.resolve(),
                }),
              ]}
              fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
              options={routeRegistry.map((x) => ({
                value: x.path,
                label: `${x.label} (${x.path})`,
              }))}
            />
          ) : null
        }
      </ProFormDependency>
      {/* 自定义图标网格面板：Form.Item 直接绑定 value/onChange，与 ProForm 字段同数据流 */}
      <Col span={12}>
        <Form.Item name="icon" label="图标">
          <IconPicker />
        </Form.Item>
      </Col>
      <ProFormTreeSelect
        name="permissionName"
        label="绑定权限"
        placeholder="可选，绑定后需该权限授予才可见"
        allowClear
        colProps={{ span: 12 }}
        fieldProps={{
          treeData: permissionTreeData,
          showSearch: true,
          treeNodeFilterProp: 'title',
          treeDefaultExpandAll: true,
          treeLine: true,
        }}
      />
      <ProFormDigit
        name="orderNo"
        label="排序"
        min={0}
        max={9999}
        colProps={{ span: 12 }}
        fieldProps={{ precision: 0 }}
      />
      <ProFormSwitch
        name="isHide"
        label="隐藏"
        tooltip="不出现在侧边菜单，但页面路由可达"
        colProps={{ span: 12 }}
      />
      <ProFormSwitch
        name="isEnabled"
        label="启用"
        tooltip="停用后整棵子树不可见"
        colProps={{ span: 12 }}
      />
      <ProFormText name="remark" label="备注" placeholder="可选" colProps={{ span: 24 }} />
    </ModalForm>
  );
};

export default MenuFormModal;
