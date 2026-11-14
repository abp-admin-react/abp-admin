import { Link, useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Form,
  InputNumber,
  Select,
  Space,
  Spin,
  Switch,
  Tag,
} from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  type ChainRuntimeSettingsDto,
  type ChainRuntimeSettingsInput,
  getChainRuntimeSettings,
  initializeChainCredentialEncryption,
  updateChainRuntimeSettings,
} from '@/abp/chainAdmin';
import { getUsers } from '@/abp/identity';
import { isValidRecordId } from './record-focus';

const maxRecipients = 48;
const roundBudgetRule = {
  required: true,
  type: 'integer' as const,
  min: 60,
  max: 3600,
  message: '请输入 60 至 3600 之间的整数秒数',
};

function normalizeRecipients(ids: string[] = []) {
  return [...new Set(ids.map((id) => id.trim().toLowerCase()).filter(Boolean))];
}

export default function ChainRuntimeSettings() {
  const { message } = App.useApp();
  const { canManageUsers } = useAccess();
  const [form] = Form.useForm<ChainRuntimeSettingsInput>();
  const [initialSettings, setInitialSettings] =
    useState<ChainRuntimeSettingsDto>();
  const [loadFailed, setLoadFailed] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const [initializingEncryption, setInitializingEncryption] = useState(false);
  const [encryptionError, setEncryptionError] = useState<string>();
  const [hasEncryptionKey, setHasEncryptionKey] = useState(false);
  const [revision, setRevision] = useState(0);
  const requestInProgress = useRef(false);
  const [userQuery, setUserQuery] = useState('');
  const [users, setUsers] = useState<Record<string, string>>({});
  const [matches, setMatches] = useState<string[]>([]);

  useEffect(() => {
    let active = true;
    setLoadFailed(false);
    getChainRuntimeSettings()
      .then((settings) => {
        if (active) {
          setInitialSettings(settings);
          setHasEncryptionKey(settings.hasEncryptionKey);
        }
      })
      .catch(() => {
        if (active) setLoadFailed(true);
      });
    return () => {
      active = false;
    };
  }, [revision]);

  useEffect(() => {
    const filter = userQuery.trim();
    setMatches([]);
    if (!canManageUsers || filter.length < 2 || isValidRecordId(filter)) return;

    let active = true;
    const timer = setTimeout(() => {
      getUsers({ filter, pageSize: 20 })
        .then(({ items }) => {
          if (!active) return;
          const options = items.filter((user) => user.isActive);
          setUsers((current) => ({
            ...current,
            ...Object.fromEntries(
              options.map((user) => [user.id, user.userName]),
            ),
          }));
          setMatches(options.map((user) => user.id));
        })
        .catch(() => {
          if (active) setMatches([]);
        });
    }, 300);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [canManageUsers, userQuery]);

  const recipientIds = Form.useWatch('alertRecipientUserIds', form) ?? [];
  const options = [...new Set([...recipientIds, ...matches])].map((id) => ({
    value: id,
    label: users[id] ?? id,
  }));

  const save = async (settings: ChainRuntimeSettingsInput) => {
    if (requestInProgress.current) return;
    requestInProgress.current = true;
    setSaving(true);
    setSaveFailed(false);
    try {
      const saved = await updateChainRuntimeSettings({
        ...settings,
        alertRecipientUserIds: normalizeRecipients(
          settings.alertRecipientUserIds,
        ),
      });
      form.setFieldsValue(saved);
      setHasEncryptionKey(saved.hasEncryptionKey);
      message.success('运行设置已保存');
    } catch {
      setSaveFailed(true);
    } finally {
      requestInProgress.current = false;
      setSaving(false);
    }
  };

  const initializeEncryption = async () => {
    if (requestInProgress.current || hasEncryptionKey) return;
    requestInProgress.current = true;
    setInitializingEncryption(true);
    setEncryptionError(undefined);
    try {
      await initializeChainCredentialEncryption();
      const settings = await getChainRuntimeSettings();
      setHasEncryptionKey(settings.hasEncryptionKey);
      if (settings.hasEncryptionKey) {
        message.success('凭据加密已初始化');
      } else {
        setEncryptionError('暂未确认凭据加密状态，请重试');
      }
    } catch {
      setEncryptionError('未能确认凭据加密状态，请重试');
    } finally {
      requestInProgress.current = false;
      setInitializingEncryption(false);
    }
  };

  if (loadFailed) {
    return (
      <Alert
        type="error"
        showIcon
        title="加载运行设置失败"
        action={
          <Button onClick={() => setRevision((value) => value + 1)}>
            重试
          </Button>
        }
      />
    );
  }
  if (!initialSettings) return <Spin aria-label="加载运行设置" />;

  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Card title="凭据加密" size="small">
        <Space orientation="vertical">
          <Space>
            <Tag color={hasEncryptionKey ? 'success' : 'warning'}>
              {hasEncryptionKey ? '已配置' : '未配置'}
            </Tag>
            {!hasEncryptionKey && (
              <Button
                onClick={initializeEncryption}
                loading={initializingEncryption}
                disabled={saving}
              >
                初始化加密
              </Button>
            )}
          </Space>
          {encryptionError && (
            <Alert type="error" showIcon title={encryptionError} />
          )}
        </Space>
      </Card>
      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={initialSettings}
          disabled={saving || initializingEncryption}
          onFinish={save}
        >
          <Form.Item
            label="充值检测、归集和钱包监控的单轮时间上限"
            name="chainRoundBudgetSeconds"
            rules={[roundBudgetRule]}
          >
            <InputNumber min={60} max={3600} step={1} suffix="秒" />
          </Form.Item>
          <Form.Item
            label="提现广播的单轮时间上限"
            name="withdrawRoundBudgetSeconds"
            rules={[roundBudgetRule]}
          >
            <InputNumber min={60} max={3600} step={1} suffix="秒" />
          </Form.Item>
          <Form.Item
            label="链上告警收件人"
            name="alertRecipientUserIds"
            extra={
              canManageUsers
                ? '搜索用户名，或粘贴用户 ID 后按回车。留空时只记录日志。'
                : '粘贴宿主管理端的用户 ID 后按回车。留空时只记录日志。'
            }
            rules={[
              {
                validator: async (_, values: string[] = []) => {
                  const ids = normalizeRecipients(values);
                  if (ids.length > maxRecipients)
                    throw new Error(`最多添加 ${maxRecipients} 位收件人`);
                  if (
                    ids.some(
                      (id) =>
                        !isValidRecordId(id) ||
                        id === '00000000-0000-0000-0000-000000000000',
                    )
                  ) {
                    throw new Error('请输入有效的用户 ID（UUID）');
                  }
                },
              },
            ]}
          >
            <Select
              mode="tags"
              allowClear
              showSearch={{ filterOption: false, onSearch: setUserQuery }}
              options={options}
              tokenSeparators={[',', ' ', '，']}
            />
          </Form.Item>
          <Form.Item
            label="Etherscan 密钥池为空时使用部署备用配置"
            name="useLegacyExplorerFallback"
            valuePropName="checked"
            extra={
              <>
                优先使用
                <Link to="/web3/config?view=network">
                  链网络中的 Etherscan 密钥
                </Link>
                。关闭后，密钥池为空时无法按地址查询链上转账。
              </>
            }
          >
            <Switch />
          </Form.Item>
          {saveFailed && (
            <Alert type="error" showIcon title="保存失败，请重试" />
          )}
          <Button type="primary" htmlType="submit" loading={saving}>
            保存运行设置
          </Button>
        </Form>
      </Card>
    </Space>
  );
}
