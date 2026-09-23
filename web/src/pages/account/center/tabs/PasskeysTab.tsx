import { App, Button, Space, Table } from 'antd';
import React from 'react';
import {
  deletePasskey,
  getPasskeyCreationOptions,
  getPasskeys,
  registerPasskey,
  type UserPasskeyDto,
} from '@/abp/accountSecurity';
import { useAsyncData } from '@/hooks/useAsyncData';

// base64url <-> ArrayBuffer（WebAuthn 凭据字段编解码，自 index.tsx 拆分）
function b64ToBuf(value: string) {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
  const pad =
    normalized.length % 4 === 0 ? '' : '='.repeat(4 - (normalized.length % 4));
  const binary = atob(normalized + pad);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function bufToB64(buffer: ArrayBuffer) {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  bytes.forEach((b) => {
    binary += String.fromCharCode(b);
  });
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');
}

/** Passkey Tab：注册 / 删除 Passkey 凭据（自 index.tsx 拆分，见重构报告问题 4） */
const PasskeysTab: React.FC = () => {
  const { message } = App.useApp();
  const {
    data: items,
    loading,
    refresh,
  } = useAsyncData(async () => {
    const result = await getPasskeys();
    return result.items ?? [];
  });

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Button
        type="primary"
        onClick={async () => {
          const options = await getPasskeyCreationOptions();
          const publicKey = JSON.parse(options.json);
          publicKey.challenge = b64ToBuf(publicKey.challenge);
          publicKey.user.id = b64ToBuf(publicKey.user.id);
          if (publicKey.excludeCredentials) {
            publicKey.excludeCredentials = publicKey.excludeCredentials.map(
              (c: { id: string }) => ({ ...c, id: b64ToBuf(c.id) }),
            );
          }
          const cred = (await navigator.credentials.create({
            publicKey,
          })) as PublicKeyCredential;
          const response = cred.response as AuthenticatorAttestationResponse;
          await registerPasskey({
            credentialJson: JSON.stringify({
              id: cred.id,
              rawId: bufToB64(cred.rawId),
              type: cred.type,
              response: {
                clientDataJSON: bufToB64(response.clientDataJSON),
                attestationObject: bufToB64(response.attestationObject),
              },
            }),
            name: 'browser',
          });
          message.success('Passkey 已注册');
          await refresh();
        }}
      >
        注册 Passkey
      </Button>
      <Table<UserPasskeyDto>
        rowKey="credentialId"
        size="small"
        loading={loading}
        dataSource={items ?? []}
        pagination={false}
        columns={[
          { title: '名称', dataIndex: 'name' },
          { title: '凭证', dataIndex: 'credentialId', ellipsis: true },
          {
            title: '操作',
            render: (_, record) => (
              <Button
                type="link"
                danger
                size="small"
                onClick={async () => {
                  await deletePasskey(record.credentialId);
                  message.success('已删除');
                  await refresh();
                }}
              >
                删除
              </Button>
            ),
          },
        ]}
      />
    </Space>
  );
};

export default PasskeysTab;
