import {
  DrawerForm,
  ProFormDependency,
  type ProFormInstance,
  ProFormList,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App } from 'antd';
import React, { useRef, useState } from 'react';
import {
  checkTenantConnectionString,
  getAvailableTenantDatabases,
  getTenantConnectionStringManagement,
  updateTenantConnectionStrings,
} from '@/abp/tenants';

const MASK = '********';

type ConnectionStringDrawerProps = {
  tenantId: string;
  tenantName?: string;
};

/**
 * T2.8 SaaS Pro 缺口第 4 项：租户连接字符串管理。
 * 使用共享数据库开关 / 默认连接串 / 模块特定连接串动态列表 / 每行测试连接。
 * 值语义：留空 = 保持原值（服务端把空/纯掩码都解释为保持原值）。
 * 掩码不回显进输入框：已保存时显示空值 + placeholder 提示——
 * 若回显 "********"，用户在其后追加输入会提交出「掩码+新值」的非法值
 * （服务端已加守卫拒绝，但 UI 层就不该制造这种输入）。
 */
const ConnectionStringDrawer: React.FC<ConnectionStringDrawerProps> = ({
  tenantId,
  tenantName,
}) => {
  const { message } = App.useApp();
  const formRef = useRef<ProFormInstance>(undefined);
  const [databases, setDatabases] = useState<string[]>([]);

  // 掩码 → 空值：空值提交时服务端解释为「保持原值」
  const maskToEmpty = (value?: string | null) => (value === MASK ? '' : value ?? '');

  // 测试连接只做连通性探测（服务端开一次连接立即关闭，不写库），不入库。
  // 掩码分支现为纯防御：掩码已不回显进输入框（见 request/maskToEmpty），
  // 此处拦截的是用户手工敲入 '********' 的场景——掩码不是可连接的串，直接提示。
  const testConnection = async (value?: string) => {
    const trimmed = (value || '').trim();
    if (!trimmed || trimmed === MASK) {
      message.warning('连接串为空或为已保存的掩码值，请输入新连接串后再测试');
      return;
    }
    const hide = message.loading('正在测试连接…', 0);
    try {
      const result = await checkTenantConnectionString(tenantId, trimmed);
      if (result.isValid) {
        message.success('连接成功');
      } else {
        message.error(`连接失败：${result.errorMessage || '未知错误'}`);
      }
    } finally {
      hide();
    }
  };

  return (
    <DrawerForm
      title={`连接字符串 - ${tenantName || ''}`}
      trigger={<a>连接字符串</a>}
      width={640}
      formRef={formRef}
      drawerProps={{ destroyOnHidden: true }}
      request={async () => {
        const [management, availableDatabases] = await Promise.all([
          getTenantConnectionStringManagement(tenantId),
          getAvailableTenantDatabases(),
        ]);
        setDatabases(availableDatabases);
        return {
          useSharedDatabase: management.useSharedDatabase,
          defaultValue: maskToEmpty(
            management.items.find((item) => item.name === 'Default')?.value,
          ),
          items: management.items
            .filter((item) => item.name !== 'Default')
            .map((item) => ({ name: item.name, value: maskToEmpty(item.value) })),
        };
      }}
      onFinish={async (values) => {
        if (values.useSharedDatabase) {
          await updateTenantConnectionStrings(tenantId, {
            useSharedDatabase: true,
            items: [],
          });
        } else {
          const items: { name: string; value?: string }[] = [
            // Default 始终提交：留空或掩码由服务端解释为「保持原值」
            { name: 'Default', value: (values.defaultValue || '').trim() },
          ];
          for (const item of values.items ?? []) {
            if (item?.name) {
              items.push({
                name: item.name,
                value: (item.value || '').trim(),
              });
            }
          }
          await updateTenantConnectionStrings(tenantId, {
            useSharedDatabase: false,
            items,
          });
        }
        message.success('连接字符串已保存');
        return true;
      }}
    >
      <ProFormSwitch
        name="useSharedDatabase"
        label="使用共享数据库"
        tooltip="勾选后移除该租户的所有连接串记录，回退到 Host 数据库"
      />
      <ProFormDependency name={['useSharedDatabase']}>
        {({ useSharedDatabase }) =>
          useSharedDatabase ? null : (
            <>
              <ProFormText
                name="defaultValue"
                label="默认连接字符串"
                placeholder="留空表示保持原值"
                fieldProps={{
                  addonAfter: (
                    <a
                      onClick={() =>
                        testConnection(
                          formRef.current?.getFieldValue('defaultValue'),
                        )
                      }
                    >
                      测试连接
                    </a>
                  ),
                }}
              />
              <ProFormList
                name="items"
                label="模块特定连接字符串"
                copyIconProps={false}
                creatorButtonProps={{ creatorButtonText: '添加连接字符串' }}
                creatorRecord={() => ({ name: undefined, value: '' })}
                actionRender={(field, _action, defaultActionDom) => [
                  <a
                    key="test"
                    onClick={() =>
                      testConnection(
                        formRef.current?.getFieldValue([
                          'items',
                          field.name,
                          'value',
                        ]),
                      )
                    }
                  >
                    测试连接
                  </a>,
                  ...defaultActionDom,
                ]}
              >
                <ProFormSelect
                  name="name"
                  label="数据库"
                  rules={[{ required: true, message: '请选择数据库' }]}
                  options={databases.map((name) => ({
                    label: name,
                    value: name,
                  }))}
                />
                <ProFormText
                  name="value"
                  label="连接字符串"
                  placeholder="留空表示保持原值"
                />
              </ProFormList>
            </>
          )
        }
      </ProFormDependency>
    </DrawerForm>
  );
};

export default ConnectionStringDrawer;
