import { useAccess } from '@umijs/max';
import { Alert, App, Button, Empty, Space, Spin, Switch } from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  getNetworkStatus,
  type NetworkStatusDto,
  setChainSwitches,
} from '@/abp/chainAdmin';
import { getSettingGroup2, getSettingGroups } from '@/abp/settingUi';
import { isNotFound } from '@/requestErrorConfig';
import SettingGroupPanel from '../settings/SettingGroupPanel';
import WithdrawalControls from './withdrawal-controls';

type RuleSection = 'Chain' | 'Withdraw';
type SwitchKey = keyof Parameters<typeof setChainSwitches>[0];

const walletWatchInterval = 'Collection.Chain.HotWalletWatchIntervalSeconds';
const operationalSettings = new Set([
  'Collection.Chain.ScanEnabled',
  'Collection.Chain.SweepEnabled',
  'Collection.Chain.HotWalletWatchEnabled',
  'Collection.Withdraw.Enabled',
  'Collection.Withdraw.Halt',
]);
const settingBlocks: Record<string, string> = {
  'Collection.Chain.ScanIntervalSeconds': '充值检测',
  'Collection.Chain.Confirmations': '充值检测',
  'Collection.Chain.BlockRange': '充值检测',
  'Collection.Chain.SweepIntervalSeconds': '自动归集',
  'Collection.Chain.SweepRoundLimit': '自动归集',
  'Collection.Chain.SweepTaskBatchSize': '自动归集',
  'Collection.Chain.FuelBnbLimit': '自动归集',
  'Collection.Chain.RetryCooldownMinutes': '自动归集',
  'Collection.Chain.RebroadcastGraceMinutes': '自动归集',
  'Collection.Chain.ScanBehindBlocks': '运行告警',
  'Collection.Chain.GasBnbAlertThreshold': '运行告警',
  'Collection.Chain.SweepStuckMinutes': '运行告警',
  'Collection.Chain.SkipRatioWindowMinutes': '运行告警',
  'Collection.Chain.SkipRatioThreshold': '运行告警',
  'Collection.Withdraw.MinAmount': '费用与限额',
  'Collection.Withdraw.FeeRate': '费用与限额',
  'Collection.Withdraw.MaxDailyAmount': '费用与限额',
  'Collection.Withdraw.MaxDailyCount': '费用与限额',
  'Collection.Withdraw.AutoApproveMaxAmount': '自动审核',
  'Collection.Withdraw.AutoApproveBatchSize': '自动审核',
  'Collection.Withdraw.BroadcastTimeoutMinutes': '链上打款',
  'Collection.Withdraw.MaxRetries': '链上打款',
  'Collection.Withdraw.BroadcastIntervalSeconds': '链上打款',
  'Collection.Withdraw.BroadcastBatchSize': '链上打款',
  'Collection.Withdraw.HotWalletUsdtFloor': '提现钱包',
  'Collection.Withdraw.MinHotWalletBnb': '提现钱包',
  [walletWatchInterval]: '提现钱包',
};
const chainSwitches: Record<RuleSection, { key: SwitchKey; label: string }[]> =
  {
    Chain: [
      { key: 'scanEnabled', label: '自动检测充值' },
      { key: 'sweepEnabled', label: '自动归集' },
    ],
    Withdraw: [{ key: 'hotWalletWatchEnabled', label: '提现钱包监控' }],
  };

function Web3RuleForm({ section }: { section: RuleSection }) {
  const [group, setGroup] = useState<API.SettingGroup>();
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    getSettingGroups()
      .then((groups) => {
        if (!active) return;
        const web3 = groups.find((item) => item.groupName === 'Web3');
        const settingInfos = (web3?.settingInfos ?? []).flatMap((info) => {
          if (!info.name || operationalSettings.has(info.name)) return [];
          const infoSection =
            info.name === walletWatchInterval
              ? 'Withdraw'
              : getSettingGroup2(info);
          if (infoSection !== section) return [];

          return [
            {
              ...info,
              properties: {
                ...info.properties,
                Group2: settingBlocks[info.name] ?? section,
              },
            },
          ];
        });
        setGroup(settingInfos.length ? { ...web3, settingInfos } : undefined);
      })
      .catch((error: unknown) => {
        if (!active) return;
        setGroup(undefined);
        setFailed(!isNotFound(error));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [section, revision]);

  const reload = () => setRevision((value) => value + 1);
  if (loading) return <Spin aria-label="加载规则" />;
  if (failed) {
    return (
      <Alert
        type="error"
        showIcon
        title="加载规则失败"
        action={<Button onClick={reload}>重试</Button>}
      />
    );
  }
  return group ? (
    <SettingGroupPanel group={group} onChanged={reload} columns={2} />
  ) : (
    <Empty description="暂无可管理的规则" />
  );
}

function ChainSwitches({ section }: { section: RuleSection }) {
  const { message } = App.useApp();
  const [status, setStatus] = useState<NetworkStatusDto>();
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const [pendingSwitch, setPendingSwitch] = useState<SwitchKey>();
  const updating = useRef(false);

  useEffect(() => {
    let active = true;
    setFailed(false);
    getNetworkStatus()
      .then((value) => {
        if (active) setStatus(value);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [revision]);

  const changeSwitch = async (key: SwitchKey, enabled: boolean) => {
    if (updating.current) return;
    updating.current = true;
    setPendingSwitch(key);
    try {
      await setChainSwitches({ [key]: enabled });
      setStatus((current) => current && { ...current, [key]: enabled });
      message.success('运行开关已更新');
    } catch (error: unknown) {
      if (isNotFound(error)) message.error('更新运行开关失败');
    } finally {
      updating.current = false;
      setPendingSwitch(undefined);
    }
  };

  if (failed) {
    return (
      <Alert
        type="error"
        showIcon
        title="加载运行开关失败"
        action={
          <Button onClick={() => setRevision((value) => value + 1)}>
            重试
          </Button>
        }
      />
    );
  }
  if (!status) return <Spin aria-label="加载运行开关" />;
  return (
    <Space size={24} wrap>
      {chainSwitches[section].map(({ key, label }) => (
        <Space key={key}>
          {label}
          <Switch
            aria-label={label}
            checked={status[key]}
            disabled={!!pendingSwitch}
            loading={pendingSwitch === key}
            onChange={(checked) => changeSwitch(key, checked)}
          />
        </Space>
      ))}
    </Space>
  );
}

export function Web3RuleSection({ section }: { section: RuleSection }) {
  const access = useAccess();
  const canConfigure = access.canConfigureCollectionNetwork;
  const canAdminWithdrawals =
    section === 'Withdraw' &&
    access.canManageCollectionWallets &&
    access.canAdminWithdrawals;
  if (!canConfigure && !canAdminWithdrawals) {
    return <Empty description="暂无规则管理权限" />;
  }

  return (
    <Space orientation="vertical" size={16} style={{ width: '100%' }}>
      {canAdminWithdrawals && <WithdrawalControls />}
      {canConfigure && access.canAdminNetwork && (
        <ChainSwitches section={section} />
      )}
      {canConfigure && access.canManageSettings && (
        <Web3RuleForm section={section} />
      )}
    </Space>
  );
}
