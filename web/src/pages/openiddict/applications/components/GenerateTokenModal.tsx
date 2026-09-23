import {
  ModalForm,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { Alert, Modal, Typography } from 'antd';
import React, { useState } from 'react';
import { generateOpenIddictAccessToken } from '@/abp/openIddictApplications';

export type GenerateTokenModalProps = {
  applicationId: string;
  clientId?: string | null;
  assignedScopes: string[];
};

/** 生成 Access Token：secret 由调用者输入（已存的是哈希读不回来），不写入任何本地存储 */
const GenerateTokenModal: React.FC<GenerateTokenModalProps> = ({
  applicationId,
  clientId,
  assignedScopes,
}) => {
  const [tokenResult, setTokenResult] = useState<{
    accessToken: string;
    tokenType: string;
    expiresInSeconds: number;
    grantedScopes: string[];
  }>();

  return (
    <>
      <ModalForm
        title={`生成 Access Token - ${clientId ?? ''}`}
        trigger={<a>生成 Access Token</a>}
        modalProps={{ destroyOnHidden: true }}
        onFinish={async (values) => {
          const result = await generateOpenIddictAccessToken(applicationId, {
            clientSecret: values.clientSecret,
            scopes: values.scopes,
          });
          setTokenResult(result);
          return true;
        }}
      >
        <ProFormText.Password
          name="clientSecret"
          label="Client Secret"
          placeholder="请输入该应用的 client secret（不会保存）"
          rules={[{ required: true, message: '请输入 client secret' }]}
          fieldProps={{ autoComplete: 'new-password' }}
        />
        <ProFormSelect
          name="scopes"
          label="Scopes"
          mode="multiple"
          placeholder="留空表示不请求 scope"
          options={assignedScopes.map((scope) => ({
            label: scope,
            value: scope,
          }))}
        />
      </ModalForm>
      <Modal
        title="Access Token"
        open={!!tokenResult}
        footer={null}
        onCancel={() => setTokenResult(undefined)}
      >
        {tokenResult && (
          <>
            <Alert
              type="warning"
              showIcon
              title="此 token 仅显示一次，请妥善保存"
              style={{ marginBottom: 16 }}
            />
            <Typography.Paragraph copyable={{ text: tokenResult.accessToken }}>
              <Typography.Text
                code
                ellipsis={{ tooltip: tokenResult.accessToken }}
              >
                {tokenResult.accessToken}
              </Typography.Text>
            </Typography.Paragraph>
            <Typography.Paragraph type="secondary">
              类型：{tokenResult.tokenType}；有效期：
              {tokenResult.expiresInSeconds} 秒
              {tokenResult.grantedScopes.length > 0 &&
                `；scopes：${tokenResult.grantedScopes.join(' ')}`}
            </Typography.Paragraph>
          </>
        )}
      </Modal>
    </>
  );
};

export default GenerateTokenModal;
