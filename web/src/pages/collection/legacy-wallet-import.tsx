import { Alert, Button, Form, Input, Modal, Space, Typography } from 'antd';
import { type ChangeEvent, useRef, useState } from 'react';
import {
  type ImportLegacyWalletInput,
  type ImportLegacyWalletResultDto,
  importLegacyWallet,
} from '@/abp/chainAdmin';

type Manifest = Omit<ImportLegacyWalletInput, 'mnemonic'>;

type ImportFields = { manifest: File; mnemonic: string };

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseManifest(json: string): Manifest {
  const manifest: unknown = JSON.parse(json);
  if (!isObject(manifest)) throw new Error();
  const { addressIndexHighWaterMark, addresses } = manifest;
  if (
    typeof addressIndexHighWaterMark !== 'number' ||
    !Number.isSafeInteger(addressIndexHighWaterMark) ||
    !Array.isArray(addresses) ||
    addresses.length === 0
  ) {
    throw new Error();
  }
  return {
    addressIndexHighWaterMark,
    addresses: addresses.map((row: unknown) => {
      if (!isObject(row)) throw new Error();
      const { address, addressIndex, isActive } = row;
      const userId = row.userId ?? null;
      if (
        typeof address !== 'string' ||
        !/^0x[0-9a-fA-F]{40}$/.test(address) ||
        typeof addressIndex !== 'number' ||
        !Number.isSafeInteger(addressIndex) ||
        typeof isActive !== 'boolean' ||
        (userId !== null && typeof userId !== 'string') ||
        (isActive && !userId)
      ) {
        throw new Error();
      }
      return { userId, address, addressIndex, isActive };
    }),
  };
}

const errorMessages: Record<string, string> = {
  LegacyWalletInvalidManifest: '地址清单有误，请核对索引、地址和用户对应关系。',
  LegacyWalletMnemonicInvalid: '原助记词校验失败，请核对备份。',
  LegacyWalletAddressMismatch: '清单中存在与原助记词和索引不匹配的地址。',
  LegacyWalletUnknownUser: '清单中有活跃地址尚未对应到新系统的用户。',
  LegacyWalletWorkersRunning: '请关闭 BSC 扫链和归集，再迁移旧钱包。',
  LegacyWalletTargetOccupied: '当前网络已有钱包、地址或扫链进度，无法迁入。',
  MasterKeyMissing: '请先在「运行设置」初始化加密。',
  TryAgain: '钱包正在被其他操作使用，请稍后重试。',
};

function importErrorMessage(error: unknown) {
  const { status, data } =
    (
      error as {
        response?: { status?: number; data?: { error?: { code?: string } } };
      } | null
    )?.response ?? {};
  const code = data?.error?.code;
  if (code?.startsWith('AbpAdmin.Collection:')) {
    const message = errorMessages[code.slice('AbpAdmin.Collection:'.length)];
    if (message) return message;
  }
  if (status === 401) return '登录已过期，请重新登录。';
  if (status === 403) return '当前账号没有迁移旧钱包的权限。';
  if (status === 404) return '迁移接口或目标 BSC 网络不可用。';
  return '钱包迁移请求未成功，请刷新配置后核对迁移状态。';
}

const importTitle = '迁移旧充值钱包';
const formId = 'legacy-wallet-import';

export default function LegacyWalletImport({
  networkId,
  hasMnemonic,
  onImported,
}: {
  networkId: string;
  hasMnemonic: boolean;
  onImported: () => void;
}) {
  const [form] = Form.useForm<ImportFields>();
  const fileInput = useRef<HTMLInputElement>(null);
  const resultStatus = useRef<HTMLDivElement>(null);
  const pending = useRef(false);
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [result, setResult] = useState<ImportLegacyWalletResultDto>();

  function close() {
    if (pending.current) return;
    form.resetFields();
    setError(undefined);
    setOpen(false);
  }

  async function submit(values: ImportFields) {
    if (pending.current) return;
    pending.current = true;
    setSaving(true);
    setError(undefined);
    try {
      let manifest: Manifest;
      try {
        manifest = parseManifest(await values.manifest.text());
      } catch {
        form.setFields([
          { name: 'manifest', errors: ['请按说明检查地址清单的 JSON 格式。'] },
        ]);
        requestAnimationFrame(() => fileInput.current?.focus());
        return;
      }
      const mnemonic = values.mnemonic.trim();
      form.resetFields(['mnemonic']);
      try {
        const imported = await importLegacyWallet(networkId, {
          ...manifest,
          mnemonic,
        });
        setResult(imported);
        setOpen(false);
        onImported();
      } catch (e) {
        setError(importErrorMessage(e));
      }
    } finally {
      pending.current = false;
      setSaving(false);
    }
  }

  return (
    <>
      <Button disabled={hasMnemonic || !!result} onClick={() => setOpen(true)}>
        {importTitle}
      </Button>
      {result && (
        <div ref={resultStatus} role="status" tabIndex={-1}>
          已迁入 {result.importedAddressCount} 个活跃地址，保留{' '}
          {result.retiredAddressCount} 个已停用地址。
          {result.nextAddressIndex === null
            ? '派生索引已用完。'
            : `下一个派生索引为 ${result.nextAddressIndex}。`}
        </div>
      )}
      <Modal
        title={importTitle}
        open={open}
        destroyOnHidden
        afterClose={() => resultStatus.current?.focus()}
        onCancel={close}
        closable={!saving}
        keyboard={!saving}
        mask={{ closable: false }}
        cancelText="取消"
        cancelButtonProps={{ disabled: saving }}
        okText="校验并迁移"
        okButtonProps={{ htmlType: 'submit', form: formId }}
        confirmLoading={saving}
      >
        <Typography.Paragraph>
          迁入未配置的 BSC
          钱包，并保留旧地址和派生索引。请先初始化加密，关闭扫链和归集。
        </Typography.Paragraph>
        <Form
          id={formId}
          name={formId}
          form={form}
          layout="vertical"
          method="post"
          autoComplete="off"
          clearOnDestroy
          disabled={saving}
          scrollToFirstError={{ focus: true }}
          onFinish={submit}
        >
          <Form.Item
            name="manifest"
            label="旧地址清单"
            rules={[{ required: true, message: '请选择地址清单 JSON 文件。' }]}
            getValueFromEvent={(event: ChangeEvent<HTMLInputElement>) =>
              event.currentTarget.files?.[0]
            }
            getValueProps={() => ({})}
            extra={
              <details>
                <summary>查看 JSON 文件格式</summary>
                <Space orientation="vertical" size={4}>
                  <span>
                    <code>addressIndexHighWaterMark</code>{' '}
                    填历史已发放的最大索引。
                  </span>
                  <span>
                    <code>addresses</code> 包含每条地址的 <code>address</code>、
                    <code>addressIndex</code> 和 <code>isActive</code>。
                  </span>
                  <span>
                    活跃地址的 <code>userId</code> 填新系统用户
                    ID。已停用地址可填 <code>null</code>。
                  </span>
                </Space>
              </details>
            }
          >
            <input
              ref={fileInput}
              type="file"
              accept=".json,application/json"
              disabled={saving}
            />
          </Form.Item>
          <Form.Item
            name="mnemonic"
            label="原充值助记词"
            rules={[
              {
                required: true,
                whitespace: true,
                message: '请输入原充值助记词。',
              },
            ]}
            extra="单词之间用空格分隔。"
          >
            <Input
              type="password"
              autoComplete="off"
              spellCheck={false}
              autoCorrect="off"
              autoCapitalize="none"
            />
          </Form.Item>
        </Form>
        {error && <Alert type="error" showIcon title={error} />}
      </Modal>
    </>
  );
}
