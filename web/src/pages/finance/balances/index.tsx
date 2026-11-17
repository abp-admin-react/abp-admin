import { PageContainer } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import { type FinBalanceProjectionDto, getBalanceProjections } from '@/abp/finance';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { financeHelpHeader } from '@/components/FinanceHelp';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';

export default function BalancesPage() {
  const { formatMessage } = useIntl();
  const load = useFinanceLoad();
  const title = formatMessage({ id: 'menu.navigation.finance-balances', defaultMessage: '账簿余额' });

  return (
    <PageContainer {...financeHelpHeader('A7', title)}>
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
        <AutoHeightProTable<FinBalanceProjectionDto>
          key={load.nonce}
          rowKey="id"
          locale={{ emptyText: <FinanceEmpty text="暂无账簿余额" /> }}
          search={{ labelWidth: 'auto' }}
          request={async (params) => {
            const maxResultCount = params.pageSize ?? 20;
            const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
            const result = await load.guard(() =>
              getBalanceProjections({ skipCount, maxResultCount, filter: params.filter }),
            );
            if (!result) return { data: [], total: 0, success: false };
            return { data: result.items, total: result.totalCount, success: true };
          }}
          columns={[
            { title: '科目或归属 ID', dataIndex: 'filter', hideInTable: true },
            { title: '科目', dataIndex: 'ownerName', width: 120, search: false },
            { title: '归属 ID', dataIndex: 'ownerId', width: 220, ellipsis: true, search: false },
            { title: '币种', dataIndex: 'currency', width: 80, search: false },
            { title: '可用', dataIndex: 'available', width: 90, search: false },
            { title: '冻结', dataIndex: 'frozen', width: 90, search: false },
            { title: '预收', dataIndex: 'prepayment', width: 90, search: false },
            { title: '账面余额合计', dataIndex: 'ledgerDerived', width: 100, search: false },
          ]}
        />
      </FinanceStateView>
    </PageContainer>
  );
}
