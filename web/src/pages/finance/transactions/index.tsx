import { PageContainer, type ProColumns } from '@ant-design/pro-components';
import { Link, useAccess, useLocation } from '@umijs/max';
import { App, Button, Descriptions, Drawer, Tag } from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  type FinVoucherDto,
  financeErrorText,
  getVoucher,
  getVouchers,
} from '@/abp/finance';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
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

  const openDetail = useCallback(async (id: string) => {
    const request = ++detailRequest.current;
    try {
      const result = await getVoucher(id);
      if (request === detailRequest.current) setDetail(result);
    } catch (error) {
      if (request === detailRequest.current) {
        message.error(
          isNotFound(error) ? '找不到该笔凭证的分录' : financeErrorText(error),
        );
      }
    }
  }, [message]);

  useEffect(() => {
    setDetail(undefined);
    if (linkedVoucher) void openDetail(linkedVoucher);
    return () => {
      detailRequest.current++;
    };
  }, [linkedVoucher, openDetail]);

  const columns: ProColumns<FinVoucherDto>[] = [
    { title: '凭证 ID', dataIndex: 'voucherId', hideInTable: true },
    {
      title: '凭证号',
      dataIndex: 'voucherNo',
      width: 150,
      search: false,
      ellipsis: true,
    },
    { title: '用户 ID', dataIndex: 'userId', width: 200, ellipsis: true },
    { title: '用户邮箱', dataIndex: 'email', hideInTable: true },
    {
      title: '业务类型',
      dataIndex: 'bizType',
      width: 130,
      valueEnum: voucherBusinessTypes,
    },
    { title: '业务单号', dataIndex: 'bizRefId', width: 190, ellipsis: true },
    { title: '业务阶段', dataIndex: 'phase', width: 100 },
    {
      title: '时间范围',
      dataIndex: 'dateRange',
      valueType: 'dateTimeRange',
      hideInTable: true,
    },
    {
      title: '摘要',
      dataIndex: 'summary',
      width: 230,
      search: false,
      ellipsis: true,
    },
    {
      title: '金额',
      dataIndex: 'amountText',
      width: 150,
      search: false,
      render: (_, row) =>
        `${row.amountText ?? row.debitTotal} ${row.currency ?? ''}`,
    },
    { title: '分录数', dataIndex: 'entryCount', width: 85, search: false },
    {
      title: '平衡',
      key: 'balanced',
      width: 85,
      search: false,
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
      search: false,
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
        <AutoHeightProTable<FinVoucherDto>
          key={`${load.nonce}:${linkedVoucher ?? ''}`}
          rowKey="id"
          columns={columns}
          form={{ initialValues: { voucherId: linkedVoucher ?? undefined } }}
          request={async (params) => {
            const range = params.dateRange as string[] | undefined;
            const result = await load.guard(() =>
              getVouchers({
                skipCount:
                  ((params.current ?? 1) - 1) * (params.pageSize ?? 20),
                maxResultCount: params.pageSize ?? 20,
                voucherId: params.voucherId,
                userId: params.userId,
                email: params.email,
                bizType: params.bizType,
                bizRefId: params.bizRefId,
                phase: params.phase,
                startTime: range?.[0]
                  ? new Date(range[0]).toISOString()
                  : undefined,
                endTime: range?.[1]
                  ? new Date(range[1]).toISOString()
                  : undefined,
              }),
            );
            return {
              data: result?.items ?? [],
              total: result?.totalCount ?? 0,
              success: !!result,
            };
          }}
        />
      </FinanceStateView>
      <Drawer
        title={detail?.voucherNo ? `分录明细 · ${detail.voucherNo}` : '分录明细'}
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
                  children: <Link to={voucherDetailsUrl(detail.id)}>{detail.id}</Link>,
                },
                {
                  key: 'time',
                  label: '时间',
                  children: formatForDisplay(detail.creationTime),
                },
                {
                  key: 'type',
                  label: '业务类型',
                  children: voucherBusinessTypes[detail.bizType] ?? detail.bizType,
                },
                { key: 'phase', label: '业务阶段', children: detail.phase },
                {
                  key: 'ref',
                  label: '业务单号',
                  children:
                    detail.bizType === 'wallet-adjust' && access.canViewBalanceAdjustments
                      ? <Link to={balanceAdjustmentUrl(detail.bizRefId)}>{detail.bizRefId}</Link>
                      : access.canViewFinanceBizDocs
                        ? <Link to={voucherSourceUrl(detail.bizRefId)}>{detail.bizRefId}</Link>
                        : detail.bizRefId,
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
            <AutoHeightProTable
              fillViewport={false}
              rowKey="seq"
              search={false}
              pagination={false}
              toolBarRender={false}
              scroll={{ x: 1010 }}
              dataSource={detail.entries ?? []}
              columns={[
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
                  renderText: (value: string) => <span style={{ whiteSpace: 'nowrap' }}>{value}</span>,
                },
                {
                  title: '变动后余额',
                  dataIndex: 'balanceAfterText',
                  width: 240,
                  renderText: (value: string | null) => <span style={{ whiteSpace: 'nowrap' }}>{value ?? '—'}</span>,
                },
              ]}
            />
          </>
        )}
      </Drawer>
    </PageContainer>
  );
}
