import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Popconfirm,
  Space,
  Spin,
  Switch,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { financeErrorText } from '@/abp/finance';
import {
  backfillOpening,
  getWithdrawStatus,
  setWithdrawEnabled,
  setWithdrawHalt,
  type WithdrawStatusDto,
} from '@/abp/withdrawal';

type Action = 'enabled' | 'halt' | 'opening';

export default function WithdrawalControls() {
  const { canAdminWithdrawals } = useAccess();
  const { message } = App.useApp();
  const [status, setStatus] = useState<WithdrawStatusDto>();
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [pending, setPending] = useState<Action>();
  const writing = useRef(false);

  const load = useCallback(async () => {
    setLoading(true);
    setFailed(false);
    try {
      setStatus(await getWithdrawStatus());
    } catch {
      setStatus(undefined);
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (canAdminWithdrawals) void load();
    else setStatus(undefined);
  }, [canAdminWithdrawals, load]);

  if (!canAdminWithdrawals) return null;

  const run = async (action: Action, write: () => Promise<string>) => {
    if (writing.current) return;
    writing.current = true;
    setPending(action);
    try {
      message.success(await write());
    } catch (error) {
      message.error(financeErrorText(error));
    } finally {
      await load();
      writing.current = false;
      setPending(undefined);
    }
  };

  const disabled = loading || !!pending;
  return (
    <Card title="提现启停与开账" size="small">
      <Space orientation="vertical" size={16} style={{ width: '100%' }}>
        {failed ? (
          <Alert
            showIcon
            type="error"
            title="加载提现状态失败"
            action={<Button onClick={load}>重试</Button>}
          />
        ) : status ? (
          <Space size={32} wrap>
            <Space>
              提现功能
              <Popconfirm
                title={status.enabled ? '关闭提现功能？' : '开启提现功能？'}
                description={
                  status.enabled
                    ? '关闭后将拒绝新的提现申请，并暂停链上打款。'
                    : '开启后将接收提现申请，并在下一轮恢复链上打款。紧急停机开启时仍会拦截。'
                }
                disabled={disabled}
                okText="确认"
                cancelText="取消"
                onConfirm={() =>
                  run('enabled', async () => {
                    const enabled = !status.enabled;
                    await setWithdrawEnabled(enabled);
                    return enabled ? '提现已开启' : '提现已关闭';
                  })
                }
              >
                <Switch
                  aria-label="提现功能"
                  checked={status.enabled}
                  disabled={disabled}
                  loading={pending === 'enabled'}
                />
              </Popconfirm>
            </Space>
            <Space>
              紧急停机
              <Popconfirm
                title={status.halt ? '解除提现紧急停机？' : '紧急停止提现？'}
                description={
                  status.halt
                    ? '解除后，提现功能开启时将重新接收申请及审批。'
                    : '停机后将拒绝新申请、暂停所有审批。已广播的订单仍会检查链上结果。'
                }
                disabled={disabled}
                okText="确认"
                cancelText="取消"
                okButtonProps={{ danger: !status.halt }}
                onConfirm={() =>
                  run('halt', async () => {
                    const halt = !status.halt;
                    await setWithdrawHalt(halt);
                    return halt ? '提现已紧急停机' : '已解除提现紧急停机';
                  })
                }
              >
                <Switch
                  aria-label="紧急停机"
                  checked={status.halt}
                  checkedChildren="停机中"
                  unCheckedChildren="正常"
                  disabled={disabled}
                  loading={pending === 'halt'}
                />
              </Popconfirm>
            </Space>
          </Space>
        ) : (
          <Spin aria-label="加载提现状态" size="small" />
        )}
        <Popconfirm
          title="执行存量余额开账？"
          description="请先处理所有未完成提现单。开账会将已有用户余额与总账的差额补记入账。"
          disabled={disabled}
          okText="确认开账"
          cancelText="取消"
          onConfirm={() =>
            run('opening', async () => {
              const { users, posted, skipped } = await backfillOpening();
              return `开账完成：用户 ${users}，过账 ${posted}，跳过 ${skipped}`;
            })
          }
        >
          <Button
            size="small"
            disabled={disabled}
            loading={pending === 'opening'}
          >
            存量余额开账
          </Button>
        </Popconfirm>
      </Space>
    </Card>
  );
}
