import { PageContainer } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  businessGridQueries,
  type LedgerBalanceGridRow,
} from '@/abp/businessGrid';
import { financeHelpHeader } from '@/components/FinanceHelp';
import { FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';

export default function BalancesPage() {
  const { formatMessage } = useIntl();
  const load = useFinanceLoad();
  const title = formatMessage({
    id: 'menu.navigation.finance-balances',
    defaultMessage: '账簿余额',
  });

  const balancesGrid = useSearchableRemoteGrid<LedgerBalanceGridRow>({
    ariaLabel: '账簿余额',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.balances, ''),
    columnDefs: toGridColumns<LedgerBalanceGridRow>(
      [
        { title: '科目', dataIndex: 'ownerName', width: 120 },
        { title: '归属 ID', dataIndex: 'ownerId', width: 220, ellipsis: true },
        { title: '币种', dataIndex: 'currency', width: 80 },
        { title: '可用', dataIndex: 'available', width: 90 },
        { title: '冻结', dataIndex: 'frozen', width: 90 },
        { title: '预收', dataIndex: 'prepayment', width: 90 },
        { title: '账面余额合计', dataIndex: 'ledgerDerived', width: 100 },
      ],
      {
        pro: true,
        kinds: {
          available: 'decimal',
          frozen: 'decimal',
          prepayment: 'decimal',
          ledgerDerived: 'decimal',
        },
      },
    ),
  });
  return (
    <PageContainer {...financeHelpHeader('A7', title)}>
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        {balancesGrid.searchBox}
        <AgGridReact<LedgerBalanceGridRow>
          {...balancesGrid.gridProps}
          key={load.nonce}
        />
      </FinanceStateView>
    </PageContainer>
  );
}
