import { Tag } from 'antd';
import type { VaultReconciliationStatus } from '@/abp/collectionAdmin';

const labels = {
  Legacy: ['default', '历史记录'],
  Balanced: ['success', '平衡'],
  Surplus: ['success', '盈余'],
  TimingDifference: ['processing', '记账时差'],
  Shortfall: ['error', '缺口'],
  Inconclusive: ['warning', '待核实'],
} as const satisfies Record<
  VaultReconciliationStatus,
  readonly [string, string]
>;

/** 旧版响应没有核对依据；保留原始金额，只有服务端确认的缺口才标红。 */
export default function VaultReconciliationTag({
  status,
}: {
  status: VaultReconciliationStatus;
}) {
  const [color, text] =
    status == null ? labels.Legacy : (labels[status] ?? labels.Inconclusive);
  return <Tag color={color}>{text}</Tag>;
}
