import {
  ModalForm,
  ProFormCheckbox,
  ProFormDependency,
  type ProFormInstance,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { App, Tabs, Tag } from 'antd';
import type { CheckboxChangeEvent } from 'antd/es/checkbox';
import React, { useRef } from 'react';
import {
  type CreateOpenIddictApplicationInput,
  createOpenIddictApplication,
  type OpenIddictApplicationDto,
  type OpenIddictScopeLookupDto,
  updateOpenIddictApplication,
} from '@/abp/openIddictApplications';

const APPLICATION_TYPES = [
  { label: 'web', value: 'web' },
  { label: 'native', value: 'native' },
];

const CLIENT_TYPES = [
  { label: 'public', value: 'public' },
  { label: 'confidential', value: 'confidential' },
];

const CONSENT_TYPES = [
  { label: 'explicit', value: 'explicit' },
  { label: 'external', value: 'external' },
  { label: 'implicit', value: 'implicit' },
  { label: 'systematic', value: 'systematic' },
];

/**
 * 绝对 URI 校验器工厂：multi=多行文本（按 \r\n,; 分割，与后端 OpenIddictTextUtils.SplitList 同一契约），
 * 否则单值。前端预校验；服务端同样校验并抛中文业务异常。
 */
const makeAbsoluteUriValidator = (multi: boolean) => {
  return (_: unknown, value?: string) => {
    const lines = multi
      ? (value || '')
          .split(/[\r\n,;]+/)
          .map((x) => x.trim())
          .filter(Boolean)
      : [(value || '').trim()].filter(Boolean);
    for (const line of lines) {
      try {
        new URL(line);
      } catch {
        return Promise.reject(new Error(`必须是绝对 URI：${line}`));
      }
    }
    return Promise.resolve();
  };
};

const absoluteUrisValidator = makeAbsoluteUriValidator(true);
const absoluteUriValidator = makeAbsoluteUriValidator(false);

export type AppFormModalProps = {
  title: string;
  trigger: React.ReactElement;
  isEdit: boolean;
  record?: OpenIddictApplicationDto;
  scopeOptions: OpenIddictScopeLookupDto[];
  onSuccess: () => void;
};

// Tabs 默认懒渲染：未访问过的 Tab 字段不会注册到 form 实例，
// preserve={false} 下这些字段既不进 onFinish 提交值，还会在下次保存时被静默清空。
// forceRender 让四个 Tab 的字段全部随弹窗挂载，与「重开重置」语义不冲突
// （modalProps.destroyOnHidden 卸载 DOM 时字段随之注销丢值，重开仍回到 initialValues）。
const TAB_ITEMS_FORCE_RENDER = { forceRender: true } as const;

/** 编辑弹窗：general / URIs / flows / scopes 四个 Tab */
const AppFormModal: React.FC<AppFormModalProps> = ({
  title,
  trigger,
  isEdit,
  record,
  scopeOptions,
  onSuccess,
}) => {
  // 静态 message 不消费 App 上下文（主题/ConfigProvider），与全仓约定统一走 App.useApp()
  const { message } = App.useApp();
  const formRef = useRef<ProFormInstance>(undefined);

  const scopeSelectOptions = scopeOptions.map((scope) => ({
    label: (
      <>
        {scope.name}
        {scope.isBuiltIn && (
          <Tag style={{ marginInlineStart: 4 }} color="blue">
            内置
          </Tag>
        )}
      </>
    ),
    value: scope.name,
  }));

  const onHybridChange = (event: CheckboxChangeEvent) => {
    // 与服务端派生规则一致的 UI 联动：勾 Hybrid 自动勾上 AuthCode+Implicit（并置灰）
    if (event.target.checked) {
      formRef.current?.setFieldsValue({
        allowAuthorizationCodeFlow: true,
        allowImplicitFlow: true,
      });
    }
  };

  const onRequireParChange = (event: CheckboxChangeEvent) => {
    // 联动：勾「强制 PAR」自动启用 Pushed Authorization 端点（并置灰）
    if (event.target.checked) {
      formRef.current?.setFieldsValue({
        enablePushedAuthorizationEndpoint: true,
      });
    }
  };

  return (
    <ModalForm
      title={title}
      trigger={trigger}
      width={720}
      formRef={formRef}
      modalProps={{ destroyOnHidden: true }}
      // 受控 formRef 实例跨开关存活：destroyOnHidden 只销毁 DOM，值（含 secret）会带到下次打开。
      // preserve={false} 让字段卸载即丢值，重开时回到 initialValues（新建=默认值，编辑=当前记录）；
      // 懒渲染 Tab 的字段补偿见 TAB_ITEMS_FORCE_RENDER。
      preserve={false}
      initialValues={
        isEdit
          ? record
          : {
              clientType: 'public',
              applicationType: 'web',
              consentType: 'implicit',
            }
      }
      onFinish={async (values) => {
        if (isEdit && record) {
          await updateOpenIddictApplication(
            record.id,
            values as CreateOpenIddictApplicationInput,
          );
          message.success('已更新');
        } else {
          await createOpenIddictApplication(
            values as CreateOpenIddictApplicationInput,
          );
          message.success('已创建');
        }
        onSuccess();
        return true;
      }}
    >
      <Tabs
        items={[
          {
            ...TAB_ITEMS_FORCE_RENDER,
            key: 'general',
            label: '常规',
            children: (
              <>
                <ProFormText
                  name="clientId"
                  label="Client Id"
                  disabled={isEdit}
                  rules={[{ required: true }]}
                />
                <ProFormText
                  name="displayName"
                  label="显示名"
                  rules={[{ required: true }]}
                />
                <ProFormSelect
                  name="applicationType"
                  label="应用类型"
                  options={APPLICATION_TYPES}
                  rules={[{ required: true }]}
                />
                <ProFormSelect
                  name="clientType"
                  label="客户端类型"
                  options={CLIENT_TYPES}
                  rules={[{ required: true }]}
                />
                <ProFormSelect
                  name="consentType"
                  label="同意类型"
                  options={CONSENT_TYPES}
                  rules={[{ required: true }]}
                />
                <ProFormText.Password
                  name="clientSecret"
                  label="Client Secret（机密客户端）"
                  placeholder={
                    isEdit ? '留空表示不修改' : 'Public 客户端无需填写'
                  }
                  fieldProps={{ autoComplete: 'new-password' }}
                />
                <ProFormText name="clientUri" label="Client URI" />
                <ProFormText name="logoUri" label="Logo URI" />
              </>
            ),
          },
          {
            ...TAB_ITEMS_FORCE_RENDER,
            key: 'uris',
            label: 'URIs',
            children: (
              <>
                <ProFormTextArea
                  name="redirectUris"
                  label="Redirect URIs（每行一个）"
                  fieldProps={{ rows: 4 }}
                  rules={[{ validator: absoluteUrisValidator }]}
                />
                <ProFormTextArea
                  name="postLogoutRedirectUris"
                  label="Post-logout Redirect URIs（每行一个）"
                  fieldProps={{ rows: 4 }}
                  rules={[{ validator: absoluteUrisValidator }]}
                />
                <ProFormText
                  name="frontChannelLogoutUri"
                  label="Front-channel Logout URI"
                  rules={[{ validator: absoluteUriValidator }]}
                />
              </>
            ),
          },
          {
            ...TAB_ITEMS_FORCE_RENDER,
            key: 'flows',
            label: 'Flows',
            children: (
              <>
                <ProFormDependency name={['allowHybridFlow']}>
                  {({ allowHybridFlow }) => (
                    <>
                      <ProFormCheckbox
                        name="allowAuthorizationCodeFlow"
                        disabled={!!allowHybridFlow}
                      >
                        Authorization Code
                      </ProFormCheckbox>
                      <ProFormCheckbox
                        name="allowImplicitFlow"
                        disabled={!!allowHybridFlow}
                      >
                        Implicit
                      </ProFormCheckbox>
                    </>
                  )}
                </ProFormDependency>
                <ProFormCheckbox
                  name="allowHybridFlow"
                  fieldProps={{ onChange: onHybridChange }}
                >
                  Hybrid（自动启用 Authorization Code 与 Implicit）
                </ProFormCheckbox>
                <ProFormCheckbox name="allowPasswordFlow">
                  Password
                </ProFormCheckbox>
                <ProFormCheckbox name="allowClientCredentialsFlow">
                  Client Credentials
                </ProFormCheckbox>
                <ProFormCheckbox name="allowRefreshTokenFlow">
                  Refresh Token
                </ProFormCheckbox>
                <ProFormCheckbox name="allowTokenExchangeFlow">
                  Token Exchange
                </ProFormCheckbox>
                <ProFormCheckbox name="allowDeviceAuthorizationFlow">
                  Device Authorization
                </ProFormCheckbox>
                <ProFormCheckbox name="enableEndSessionEndpoint">
                  启用 End Session 端点
                </ProFormCheckbox>
                <ProFormDependency name={['requirePushedAuthorization']}>
                  {({ requirePushedAuthorization }) => (
                    <ProFormCheckbox
                      name="enablePushedAuthorizationEndpoint"
                      disabled={!!requirePushedAuthorization}
                    >
                      启用 Pushed Authorization 端点
                    </ProFormCheckbox>
                  )}
                </ProFormDependency>
                <ProFormCheckbox name="requirePkce">要求 PKCE</ProFormCheckbox>
                <ProFormCheckbox
                  name="requirePushedAuthorization"
                  fieldProps={{ onChange: onRequireParChange }}
                >
                  强制 PAR（自动启用 Pushed Authorization 端点）
                </ProFormCheckbox>
              </>
            ),
          },
          {
            ...TAB_ITEMS_FORCE_RENDER,
            key: 'scopes',
            label: 'Scopes',
            children: (
              <>
                <ProFormSelect
                  name="scopes"
                  label="允许的 scopes"
                  mode="multiple"
                  options={scopeSelectOptions}
                  fieldProps={{ showSearch: true, optionFilterProp: 'value' }}
                />
                <ProFormSelect
                  name="extensionGrantTypes"
                  label="Extension grant types"
                  mode="tags"
                  placeholder="输入后回车添加，例如 urn:ietf:params:oauth:grant-type:token-exchange 之外的自定义 grant"
                />
              </>
            ),
          },
        ]}
      />
    </ModalForm>
  );
};

export default AppFormModal;
