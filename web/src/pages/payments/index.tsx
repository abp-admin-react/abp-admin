import {
  type ActionType,
  PageContainer,
  ProCard,
} from '@ant-design/pro-components';
import { App, Button, Descriptions, Drawer, Popconfirm } from 'antd';
import React, { useRef, useState } from 'react';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import type {
  PaymentDto,
  PrepaymentAccountDto,
  PrepaymentTransactionDto,
} from './service';
import {
  getPayment,
  getPayments,
  getPrepaymentAccounts,
  getPrepaymentTransactions,
  refundPayment,
} from './service';

const PaymentsPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const paymentRef = useRef<ActionType>(undefined);
  const accountRef = useRef<ActionType>(undefined);
  const [detail, setDetail] = useState<PaymentDto>();

  // 金额展示：decimal 经 JSON 序列化为 number 后直接拼接会有精度尾差，统一格式化到分
  const formatAmount = (value?: number) =>
    new Intl.NumberFormat('zh-CN', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value ?? 0);

  // 展示/判断用途的金额运算：按分取整规避 IEEE-754 尾差（0.3 - 0.1 = 0.1999…）
  const toCents = (value?: number) => Math.round((value ?? 0) * 100);

  return (
    <PageContainer>
      <ProCard
        tabs={{
          items: [
            {
              key: 'payments',
              label: '支付单',
              children: (
                <AutoHeightProTable<PaymentDto>
                  rowKey="id"
                  actionRef={paymentRef}
                  search={false}
                  request={async (params) => {
                    const result = await getPayments({
                      current: params.current,
                      pageSize: params.pageSize,
                    });
                    return {
                      data: result.items ?? [],
                      total: result.totalCount ?? 0,
                      success: true,
                    };
                  }}
                  columns={[
                    {
                      title: 'Id',
                      dataIndex: 'id',
                      ellipsis: true,
                      width: 220,
                    },
                    { title: '渠道', dataIndex: 'paymentMethod', width: 120 },
                    { title: '币种', dataIndex: 'currency', width: 80 },
                    {
                      title: '应付',
                      dataIndex: 'originalPaymentAmount',
                      width: 100,
                      render: (_, record) =>
                        formatAmount(record.originalPaymentAmount),
                    },
                    {
                      title: '实付',
                      dataIndex: 'actualPaymentAmount',
                      width: 100,
                      render: (_, record) =>
                        formatAmount(record.actualPaymentAmount),
                    },
                    {
                      title: '已退',
                      dataIndex: 'refundAmount',
                      width: 100,
                      render: (_, record) => formatAmount(record.refundAmount),
                    },
                    {
                      title: '创建时间',
                      dataIndex: 'creationTime',
                      valueType: 'dateTime',
                      width: 170,
                    },
                    {
                      title: '操作',
                      valueType: 'option',
                      width: 160,
                      render: (_, record) => {
                        const refundableRemainingCents =
                          toCents(record.actualPaymentAmount) -
                          toCents(record.refundAmount) -
                          toCents(record.pendingRefundAmount);
                        const actions = [
                          <a
                            key="detail"
                            onClick={async () => {
                              setDetail(await getPayment(record.id));
                            }}
                          >
                            详情
                          </a>,
                        ];
                        if (refundableRemainingCents > 0) {
                          actions.push(
                            <Popconfirm
                              key="refund"
                              title="按剩余可退金额全额退款？已退和在途退款不会再退一次。"
                              onConfirm={async () => {
                                // 金额以服务端为准：不传 refundAmount 即全额退款，
                                // 由服务端按行项剩余可退计算，规避前端精度尾差
                                await refundPayment(record.id, {
                                  displayReason: 'admin refund',
                                });
                                message.success('已提交退款');
                                paymentRef.current?.reload();
                              }}
                            >
                              <a>退款</a>
                            </Popconfirm>,
                          );
                        }
                        return actions;
                      },
                    },
                  ]}
                />
              ),
            },
            {
              key: 'prepayment',
              label: '预存款',
              children: (
                <AutoHeightProTable<PrepaymentAccountDto>
                  rowKey="id"
                  actionRef={accountRef}
                  search={false}
                  request={async (params) => {
                    const result = await getPrepaymentAccounts({
                      current: params.current,
                      pageSize: params.pageSize,
                    });
                    return {
                      data: result.items ?? [],
                      total: result.totalCount ?? 0,
                      success: true,
                    };
                  }}
                  columns={[
                    { title: '账户 Id', dataIndex: 'id', ellipsis: true },
                    { title: '分组', dataIndex: 'accountGroupName' },
                    { title: '用户', dataIndex: 'userId', ellipsis: true },
                    { title: '余额', dataIndex: 'balance' },
                    { title: '锁定', dataIndex: 'lockedBalance' },
                    {
                      title: '操作',
                      valueType: 'option',
                      render: (_, record) => [
                        <a
                          key="tx"
                          onClick={() => {
                            modal.info({
                              title: '账户流水',
                              width: 720,
                              content: (
                                <AccountTransactions accountId={record.id} />
                              ),
                            });
                          }}
                        >
                          流水
                        </a>,
                      ],
                    },
                  ]}
                />
              ),
            },
          ],
        }}
      />
      <Drawer
        title="支付单详情"
        open={!!detail}
        onClose={() => setDetail(undefined)}
        size={480}
      >
        {detail && (
          <Descriptions column={1} size="small">
            <Descriptions.Item label="Id">{detail.id}</Descriptions.Item>
            <Descriptions.Item label="渠道">
              {detail.paymentMethod}
            </Descriptions.Item>
            <Descriptions.Item label="外部单号">
              {detail.externalTradingCode || '-'}
            </Descriptions.Item>
            <Descriptions.Item label="应付">
              {formatAmount(detail.originalPaymentAmount)}
            </Descriptions.Item>
            <Descriptions.Item label="实付">
              {formatAmount(detail.actualPaymentAmount)}
            </Descriptions.Item>
            <Descriptions.Item label="完成时间">
              {detail.completionTime || '-'}
            </Descriptions.Item>
            <Descriptions.Item label="取消时间">
              {detail.canceledTime || '-'}
            </Descriptions.Item>
          </Descriptions>
        )}
        <Button style={{ marginTop: 16 }} onClick={() => setDetail(undefined)}>
          关闭
        </Button>
      </Drawer>
    </PageContainer>
  );
};

const AccountTransactions: React.FC<{ accountId: string }> = ({
  accountId,
}) => {
  return (
    <AutoHeightProTable<PrepaymentTransactionDto>
      rowKey="id"
      search={false}
      pagination={{ pageSize: 8 }}
      request={async (params) => {
        const result = await getPrepaymentTransactions({
          current: params.current,
          pageSize: params.pageSize,
          accountId,
        });
        return {
          data: result.items ?? [],
          total: result.totalCount ?? 0,
          success: true,
        };
      }}
      columns={[
        { title: '变动', dataIndex: 'changedBalance', width: 100 },
        { title: '原余额', dataIndex: 'originalBalance', width: 100 },
        { title: '类型', dataIndex: 'type', width: 80 },
        { title: '时间', dataIndex: 'creationTime', valueType: 'dateTime' },
      ]}
    />
  );
};

export default PaymentsPage;
