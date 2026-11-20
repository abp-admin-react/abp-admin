import { PageContainer, type ProColumns } from '@ant-design/pro-components';
import { Link, useAccess, useLocation } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Input,
  Tag,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { gridScope } from '@/abp/businessGrid';
import {
  type FinVoucherDto,
  financeErrorText,
  financeGridQueries,
  getVoucher,
} from '@/abp/finance';
import { FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  toGridColumns,
  useSearchableLocalGrid,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import { isNotFound } from '@/requestErrorConfig';
import { formatForDisplay } from '@/utils/time';
import {
  balanceAdjustmentUrl,
  voucherBusinessTypes,
  voucherDetailsUrl,
  voucherSourceUrl,
} from '../vouchers/navigation';

export default function TransactionsPage() {
  const { message } = App.useApp();
  const access = useAccess();
  const location = useLocation();
  const [detail, setDetail] = useState<FinVoucherDto>();
  const detailRequest = useRef(0);
  const load = useFinanceLoad();
  const linkedVoucher = new URLSearchParams(location.search).get('voucherId');

  const openDetail = useCallback(
    async (id: string) => {
      const request = ++detailRequest.current;
      try {
        const result = await getVoucher(id);
        if (request === detailRequest.current) setDetail(result);
      } catch (error) {
        if (request === detailRequest.current) {
          message.error(
            isNotFound(error)
              ? '找不到该笔凭证的分录'
              : financeErrorText(error),
          );
        }
      }
    },
    [message],
  );

  useEffect(() => {
    setDetail(undefined);
    if (linkedVoucher) void openDetail(linkedVoucher);
    return () => {
      detailRequest.current++;
    };
  }, [linkedVoucher, openDetail]);

  const columns: ProColumns<FinVoucherDto>[] = [
    {
      title: '凭证号',
      dataIndex: 'voucherNo',
      width: 150,
      ellipsis: true,
    },
    { title: '用户 ID', dataIndex: 'userId', width: 200, ellipsis: true },
    {
      title: '业务类型',
      dataIndex: 'bizType',
      width: 130,
      valueEnum: voucherBusinessTypes,
    },
    { title: '业务单号', dataIndex: 'bizRefId', width: 190, ellipsis: true },
    { title: '业务阶段', dataIndex: 'phase', width: 100 },
    {
      title: '摘要',
      dataIndex: 'summary',
      width: 230,
      ellipsis: true,
    },
    {
      title: '金额',
      dataIndex: 'amountText',
      width: 150,
      render: (_, row) =>
        `${row.amountText ?? row.debitTotal} ${row.currency ?? ''}`,
    },
    { title: '分录数', dataIndex: 'entryCount', width: 85 },
    {
      title: '平衡',
      dataIndex: 'balanced',
      width: 85,
      render: (_, row) =>
        row.debitTotalText === row.creditTotalText ? (
          <Tag color="success">平衡</Tag>
        ) : (
          <Tag color="error">不平衡</Tag>
        ),
    },
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 180,
      render: (_, row) => formatForDisplay(row.creationTime),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 110,
      render: (_, row) => (
        <Button type="link" size="small" onClick={() => openDetail(row.id)}>
          查看分录
        </Button>
      ),
    },
  ];

  const detailGrid1 = useSearchableLocalGrid<
    NonNullable<FinVoucherDto['entries']>[number]
  >({
    ariaLabel: '资金流水分录',
    rowData: detail?.entries ?? [],
    keywordFields: ['accountCode', 'accountName', 'ownerId', 'bucket'],
    pagination: false,
    getRowId: ({ data }) => String(data.seq),
    columnDefs: toGridColumns<NonNullable<FinVoucherDto['entries']>[number]>(
      [
        { title: '科目', dataIndex: 'accountCode', width: 90 },
        { title: '名称', dataIndex: 'accountName', width: 140 },
        {
          title: '归属类型',
          dataIndex: 'ownerType',
          width: 90,
          valueEnum: {
            Platform: '平台',
            User: '用户',
            Agent: '代理',
            Channel: '渠道',
          },
        },
        {
          title: '归属 ID',
          dataIndex: 'ownerId',
          width: 190,
          ellipsis: true,
        },
        {
          title: '余额桶',
          dataIndex: 'bucket',
          width: 100,
          renderText: (value: string) => value || '—',
        },
        {
          title: '变动金额',
          dataIndex: 'changeAmountText',
          width: 160,
          renderText: (value: string) => (
            <span style={{ whiteSpace: 'nowrap' }}>{value}</span>
          ),
        },
        {
          title: '变动后余额',
          dataIndex: 'balanceAfterText',
          width: 240,
          renderText: (value: string | null) => (
            <span style={{ whiteSpace: 'nowrap' }}>{value ?? '—'}</span>
          ),
        },
      ],
      {
        pro: true,
        kinds: { balanceAfterText: 'decimal', changeAmountText: 'decimal' },
      },
    ),
  });
  const [scope, setScope] = useState<{
    voucherId?: string;
    email?: string;
    startTime?: string;
    endTime?: string;
  }>({});
  const vouchersGridScope = gridScope({
    ...scope,
    voucherId: linkedVoucher || scope.voucherId,
  });
  const vouchersGrid = useSearchableRemoteGrid<FinVoucherDto>({
    ariaLabel: '会计凭证',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(financeGridQueries.vouchers, vouchersGridScope),
    columnDefs: toGridColumns<FinVoucherDto>(columns, {
      pro: true,
      kinds: {
        balanced: 'boolean',
        amountText: 'decimal',
        entryCount: 'number',
        creationTime: 'date',
      },
    }),
  });
  return (
    <PageContainer
      title={false}
      breadcrumb={{ items: [{ title: '会计与对账' }, { title: '分录查询' }] }}
      extra={
        access.canViewFinanceVouchers && (
          <Link to="/finance/vouchers">返回会计凭证</Link>
        )
      }
    >
      <FinanceStateView
        state={load.state}
        errorText={load.errorText}
        onRetry={load.retry}
      >
        <Form
          layout="inline"
          onFinish={(values: {
            voucherId?: string;
            email?: string;
            dateRange?: [import('dayjs').Dayjs, import('dayjs').Dayjs];
          }) =>
            setScope({
              voucherId: values.voucherId?.trim(),
              email: values.email?.trim(),
              startTime: values.dateRange?.[0].toISOString(),
              endTime: values.dateRange?.[1].toISOString(),
            })
          }
        >
          {!linkedVoucher && (
            <Form.Item name="voucherId" label="凭证 ID">
              <Input allowClear />
            </Form.Item>
          )}
          <Form.Item name="email" label="用户邮箱">
            <Input allowClear />
          </Form.Item>
          <Form.Item name="dateRange" label="时间范围">
            <DatePicker.RangePicker showTime />
          </Form.Item>
          <Button htmlType="submit">查询</Button>
        </Form>
        {vouchersGrid.searchBox}
        <AgGridReact<FinVoucherDto>
          {...vouchersGrid.gridProps}
          key={`${load.nonce}:${linkedVoucher ?? ''}`}
        />
      </FinanceStateView>
      <Drawer
        title={
          detail?.voucherNo ? `分录明细 · ${detail.voucherNo}` : '分录明细'
        }
        size={1150}
        open={!!detail}
        onClose={() => {
          detailRequest.current++;
          setDetail(undefined);
        }}
      >
        {detail && (
          <>
            <Descriptions
              column={2}
              size="small"
              items={[
                {
                  key: 'id',
                  label: '凭证 ID',
                  children: (
                    <Link to={voucherDetailsUrl(detail.id)}>{detail.id}</Link>
                  ),
                },
                {
                  key: 'time',
                  label: '时间',
                  children: formatForDisplay(detail.creationTime),
                },
                {
                  key: 'type',
                  label: '业务类型',
                  children:
                    voucherBusinessTypes[detail.bizType] ?? detail.bizType,
                },
                { key: 'phase', label: '业务阶段', children: detail.phase },
                {
                  key: 'ref',
                  label: '业务单号',
                  children:
                    detail.bizType === 'wallet-adjust' &&
                    access.canViewBalanceAdjustments ? (
                      <Link to={balanceAdjustmentUrl(detail.bizRefId)}>
                        {detail.bizRefId}
                      </Link>
                    ) : access.canViewFinanceBizDocs ? (
                      <Link to={voucherSourceUrl(detail.bizRefId)}>
                        {detail.bizRefId}
                      </Link>
                    ) : (
                      detail.bizRefId
                    ),
                },
                {
                  key: 'user',
                  label: '用户 ID',
                  children: detail.userId ?? '—',
                },
                {
                  key: 'summary',
                  label: '摘要',
                  children: detail.summary ?? '—',
                  span: 2,
                },
              ]}
            />
            {detailGrid1.searchBox}
            <AgGridReact<NonNullable<FinVoucherDto['entries']>[number]>
              {...detailGrid1.gridProps}
            />
          </>
        )}
      </Drawer>
    </PageContainer>
  );
}
