import { DownloadOutlined } from '@ant-design/icons';
import { PageContainer, ProDescriptions } from '@ant-design/pro-components';
import { useAccess, useSearchParams } from '@umijs/max';
import { Button, Drawer, Input, Modal, message, Tag, Tooltip } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import React, { useRef, useState } from 'react';
import {
  exportAuditLogs,
  getAuditLog,
  getAuditLogs,
  markAuditLogHandled,
  unmarkAuditLogHandled,
} from '@/abp/proModules';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import CorrelationIdText from '@/components/CorrelationIdText';
import {
  dateRangeFilter,
  enumFilters,
  firstFilterValue,
  textFilter,
} from '@/components/tableColumnFilters';
import { dictionaryRequest } from '@/utils/dictionary';
import { toDayEnd, toDayStart } from '@/utils/format';
import { changeTypeText } from './changeType';
import PropertyChangesTable from './components/PropertyChangesTable';
import EntityChangeHistoryDrawer from './EntityChangeHistoryDrawer';

const formatHandledAt = (value?: string | null) =>
  value ? new Date(value).toLocaleString() : '';

// dateRange 只给到日期（YYYY-MM-DD）：补齐到整天边界，避免结束日整段被排除（共享工具 toDayStart/toDayEnd）
const splitExecutionTimeRange = (
  range?: unknown[] | null,
): { startTime?: string; endTime?: string } => {
  if (!range || range.length !== 2 || !range.every(dayjs.isDayjs)) {
    return {};
  }
  const [start, end] = range as [Dayjs, Dayjs];
  return {
    startTime: toDayStart(start.format('YYYY-MM-DD')),
    endTime: toDayEnd(end.format('YYYY-MM-DD')),
  };
};

// 「是否异常」下拉存的是字符串，与 unhandledErrorOnly 同一套约定
const parseTriBool = (value: unknown): boolean | undefined =>
  value === 'true' ? true : value === 'false' ? false : undefined;

const AuditLogsPage: React.FC = () => {
  const [detail, setDetail] = useState<any>();
  const [historyDrawer, setHistoryDrawer] = useState<{
    open: boolean;
    entityTypeFullName?: string;
    entityId?: string;
  }>({ open: false });
  const [exporting, setExporting] = useState(false);
  const tableRef = useRef<any>(null);
  const access = useAccess();
  // 支持从操作日志详情跳转过来（?correlationId=xxx 串联同一次请求的两类日志）
  const [searchParams] = useSearchParams();
  // 只作受控筛选的初始值：用户清空筛选后不应被 URL 初始值粘住（审查修复）。
  // 列头筛选走官网「可控的筛选」模式（columns.filteredValue + onChange）：
  // 导出与列表同口径，直接读这份状态。
  const initialCorrelationId = searchParams.get('correlationId') ?? undefined;
  const [filters, setFilters] = useState<Record<string, React.Key[] | null>>(
    initialCorrelationId ? { correlationId: [initialCorrelationId] } : {},
  );

  // 「方法」筛选选项来自数据字典（异步），落到列头 filters 需要同步数组
  const [methodFilters, setMethodFilters] = useState<
    { text: string; value: string }[]
  >([]);
  React.useEffect(() => {
    let alive = true;
    dictionaryRequest('AuditLogHttpMethod')()
      .then((options) => {
        if (alive) {
          setMethodFilters(
            options.map((o) => ({ text: o.label, value: String(o.value) })),
          );
        }
      })
      .catch(() => {
        // 失败要可见：静默吞掉会让「方法」筛选菜单永远为空
        if (alive) message.error('加载方法筛选选项失败');
      });
    return () => {
      alive = false;
    };
  }, []);

  // 「标记已处理」弹窗目标行与备注
  const [handleTarget, setHandleTarget] = useState<any>();
  const [handleNote, setHandleNote] = useState('');
  const [handling, setHandling] = useState(false);

  const isAuditLogError = (record: any) =>
    (record?.httpStatusCode ?? 0) >= 400 || !!record?.exceptions;

  // 处理状态三态：已处理（Tooltip 带处理人/时间/备注）/ 未处理（错误口径）/ 非错误无标记。
  // 独立渲染函数而非链式三元（前端规则组：no nested ternaries）
  const renderHandleState = (_: any, record: any) => {
    if (record.isHandled) {
      return (
        <Tooltip
          title={`处理人：${record.handledBy ?? '-'}\n时间：${formatHandledAt(record.handledAt)}${
            record.handleNote ? `\n备注：${record.handleNote}` : ''
          }`}
          overlayInnerStyle={{ whiteSpace: 'pre-wrap' }}
        >
          <Tag color="success">已处理</Tag>
        </Tooltip>
      );
    }

    if (isAuditLogError(record)) {
      return <Tag color="error">未处理</Tag>;
    }

    return <Tag>-</Tag>;
  };

  const openDetail = async (id: string) => {
    setDetail(await getAuditLog(id));
  };

  const confirmMarkHandled = async () => {
    if (!handleTarget) {
      return;
    }
    setHandling(true);
    try {
      await markAuditLogHandled(handleTarget.id, handleNote || undefined);
      message.success('已标记为已处理');
      setHandleTarget(undefined);
      setHandleNote('');
      tableRef.current?.reload();
      if (detail?.id === handleTarget.id) {
        await openDetail(handleTarget.id);
      }
    } catch (e: any) {
      message.error(e?.message || '操作失败');
    } finally {
      setHandling(false);
    }
  };

  const handleUnmarkHandled = async (record: any) => {
    try {
      await unmarkAuditLogHandled(record.id);
      message.success('已取消已处理标记');
      tableRef.current?.reload();
      if (detail?.id === record.id) {
        await openDetail(record.id);
      }
    } catch (e: any) {
      message.error(e?.message || '操作失败');
    }
  };

  const handleExport = async () => {
    setExporting(true);
    try {
      // 从列头筛选的受控状态取当前筛选条件（与列表同口径）
      const timeRange = splitExecutionTimeRange(filters.executionTime);
      const result = await exportAuditLogs({
        httpMethod: firstFilterValue(filters, 'httpMethod'),
        url: firstFilterValue(filters, 'url'),
        // 导出与列表同口径：关联 ID 与「仅未处理错误」筛选同样作用于导出（同步与异步链路）
        correlationId: firstFilterValue(filters, 'correlationId'),
        unhandledErrorOnly:
          firstFilterValue(filters, 'isHandled') === 'true' || undefined,
        userName: firstFilterValue(filters, 'userName'),
        startTime: timeRange.startTime,
        endTime: timeRange.endTime,
        hasException: parseTriBool(firstFilterValue(filters, 'httpStatusCode')),
      });

      if (result.isQueued) {
        message.info('结果集较大，已转为后台任务，完成后将邮件通知');
      } else {
        // 同步下载
        const blob = new Blob([result.blob], {
          type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `audit-logs-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.xlsx`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        message.success('导出成功');
      }
    } catch (e: any) {
      message.error(e?.message || '导出失败');
    } finally {
      setExporting(false);
    }
  };

  return (
    <PageContainer>
      <AutoHeightProTable
        actionRef={tableRef}
        rowKey="id"
        search={false}
        columns={[
          {
            title: '时间',
            dataIndex: 'executionTime',
            valueType: 'dateTime',
            filteredValue: filters.executionTime as React.Key[] | undefined,
            ...dateRangeFilter(),
          },
          {
            title: '用户',
            dataIndex: 'userName',
            filteredValue: filters.userName as React.Key[] | undefined,
            ...textFilter(),
          },
          {
            title: '方法',
            dataIndex: 'httpMethod',
            // T3.4：筛选下拉读数据字典（AuditLogHttpMethod），不再靠手输文本；
            // 单选：页面按首值下发，多选两项会静默只按第一项过滤
            filterMultiple: false,
            filters: methodFilters,
            filteredValue: filters.httpMethod as React.Key[] | undefined,
          },
          {
            title: 'URL',
            dataIndex: 'url',
            ellipsis: true,
            filteredValue: filters.url as React.Key[] | undefined,
            ...textFilter('按 URL 筛选'),
          },
          {
            title: '状态',
            dataIndex: 'httpStatusCode',
            // 「是否异常」筛选挂在状态列：两者同属请求结果维度
            filterMultiple: false,
            filters: enumFilters({ 有异常: 'true', 无异常: 'false' }),
            filteredValue: filters.httpStatusCode as React.Key[] | undefined,
          },
          {
            title: '关联 ID',
            dataIndex: 'correlationId',
            width: 130,
            copyable: true,
            ellipsis: true,
            filteredValue: filters.correlationId as React.Key[] | undefined,
            ...textFilter('与操作日志串联的关联 ID'),
            render: (_, record) => (
              <CorrelationIdText
                value={record.correlationId}
                linkTo="/administration/operation-logs"
                linkText="查关联操作日志"
              />
            ),
          },
          {
            title: '处理状态',
            dataIndex: 'isHandled',
            width: 110,
            // 「仅未处理错误」筛选挂在处理状态列（错误认领工作流口径）
            filterMultiple: false,
            filters: enumFilters({ 仅未处理错误: 'true' }),
            filteredValue: filters.isHandled as React.Key[] | undefined,
            render: renderHandleState,
          },
          { title: '耗时(ms)', dataIndex: 'executionDuration' },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => [
              <a key="detail" onClick={() => openDetail(record.id)}>
                详情
              </a>,
              ...(access.canHandleAuditLogErrors
                ? [
                    record.isHandled ? (
                      <a
                        key="unhandle"
                        onClick={() => handleUnmarkHandled(record)}
                      >
                        取消处理
                      </a>
                    ) : (
                      <a
                        key="handle"
                        onClick={() => {
                          setHandleNote('');
                          setHandleTarget(record);
                        }}
                      >
                        标记已处理
                      </a>
                    ),
                  ]
                : []),
            ],
          },
        ]}
        toolbar={{
          actions: access.canExportAuditLogs
            ? [
                <Button
                  key="export"
                  type="primary"
                  icon={<DownloadOutlined />}
                  loading={exporting}
                  onClick={handleExport}
                >
                  导出 Excel
                </Button>,
              ]
            : [],
        }}
        request={async (params, _sorter, filter) => {
          const f = (filter ?? {}) as Record<string, unknown[] | undefined>;
          // 列头受控筛选是单一事实源；第三参 f 在挂载/未交互时不含
          // filterDropdown 列（ProTable 初值只收配了 filters 的列），
          // 故 f 缺失时回退受控状态——深链初值才能作用于首请求
          const read = (key: string): string | undefined =>
            firstFilterValue(f, key) ?? firstFilterValue(filters, key);
          const timeRange = splitExecutionTimeRange(f.executionTime);
          const result = await getAuditLogs({
            current: params.current,
            pageSize: params.pageSize,
            httpMethod: read('httpMethod'),
            url: read('url'),
            userName: read('userName'),
            correlationId: read('correlationId'),
            unhandledErrorOnly:
              firstFilterValue(f, 'isHandled') === 'true' || undefined,
            startTime: timeRange.startTime,
            endTime: timeRange.endTime,
            hasException: parseTriBool(firstFilterValue(f, 'httpStatusCode')),
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        onChange={(_pagination, nextFilters) => {
          setFilters(nextFilters as Record<string, React.Key[] | null>);
        }}
      />
      <Drawer
        title="审计详情"
        open={!!detail}
        onClose={() => setDetail(undefined)}
        size={880}
        destroyOnHidden
      >
        <ProDescriptions
          column={1}
          bordered
          dataSource={detail}
          columns={[
            { title: '用户', dataIndex: 'userName' },
            { title: '方法', dataIndex: 'httpMethod' },
            { title: 'URL', dataIndex: 'url' },
            { title: '状态', dataIndex: 'httpStatusCode' },
            { title: '耗时(ms)', dataIndex: 'executionDuration' },
            {
              title: '时间',
              dataIndex: 'executionTime',
              valueType: 'dateTime',
            },
            { title: '客户端', dataIndex: 'clientId' },
            {
              title: 'IP',
              dataIndex: 'clientIpAddress',
              render: (_, record) =>
                `${record?.clientIpAddress ?? '-'}${
                  record?.ipLocation ? `（${record.ipLocation}）` : ''
                }`,
            },
            {
              title: '关联 ID',
              dataIndex: 'correlationId',
              render: (_, record) => (
                <CorrelationIdText
                  value={record?.correlationId}
                  linkTo="/administration/operation-logs"
                  linkText="查关联操作日志"
                />
              ),
            },
            {
              title: '处理状态',
              dataIndex: 'isHandled',
              hideInDescriptions: !detail?.isHandled,
              render: (_, record) =>
                `已处理 · ${record?.handledBy ?? '-'} · ${formatHandledAt(record?.handledAt)}${
                  record?.handleNote ? ` · 备注：${record.handleNote}` : ''
                }`,
            },
            {
              title: '异常',
              dataIndex: 'exceptions',
              valueType: 'jsonCode',
            },
          ]}
        />
        <AutoHeightProTable
          headerTitle="调用"
          rowKey={(row) =>
            `${row.serviceName}-${row.methodName}-${row.executionDuration}`
          }
          search={false}
          /* 抽屉内嵌套布局，不撑满视口 */
          fillViewport={false}
          pagination={false}
          dataSource={detail?.actions || []}
          columns={[
            { title: '服务', dataIndex: 'serviceName', ellipsis: true },
            { title: '方法', dataIndex: 'methodName' },
            { title: '耗时(ms)', dataIndex: 'executionDuration', width: 120 },
          ]}
        />
        <AutoHeightProTable
          headerTitle="实体变更"
          rowKey={(row) =>
            `${row.entityTypeFullName}-${row.entityId}-${row.changeType}`
          }
          search={false}
          /* 抽屉内嵌套布局，不撑满视口 */
          fillViewport={false}
          pagination={false}
          dataSource={detail?.entityChanges || []}
          columns={[
            { title: '实体', dataIndex: 'entityTypeFullName', ellipsis: true },
            { title: '实体 Id', dataIndex: 'entityId' },
            {
              title: '类型',
              dataIndex: 'changeType',
              render: (_, record) => changeTypeText(record.changeType),
            },
            {
              title: '操作',
              valueType: 'option',
              width: 100,
              render: (_, record) => [
                <a
                  key="history"
                  onClick={() =>
                    setHistoryDrawer({
                      open: true,
                      entityTypeFullName: record.entityTypeFullName,
                      entityId: record.entityId,
                    })
                  }
                >
                  完整历史
                </a>,
              ],
            },
          ]}
          expandable={{
            expandedRowRender: (record) => (
              <PropertyChangesTable propertyChanges={record.propertyChanges} />
            ),
          }}
        />
      </Drawer>
      <Modal
        title="标记为已处理"
        open={!!handleTarget}
        confirmLoading={handling}
        onCancel={() => setHandleTarget(undefined)}
        onOk={confirmMarkHandled}
        destroyOnHidden
      >
        <p>
          将「{handleTarget?.httpMethod} {handleTarget?.url}」标记为已处理，
          记录处理人与时间；可填写处置备注（结论/根因/工单号）。
        </p>
        <Input.TextArea
          value={handleNote}
          onChange={(e) => setHandleNote(e.target.value)}
          placeholder="处置备注（可选）"
          rows={3}
          maxLength={500}
          showCount
        />
      </Modal>
      <EntityChangeHistoryDrawer
        open={historyDrawer.open}
        onClose={() => setHistoryDrawer({ open: false })}
        entityTypeFullName={historyDrawer.entityTypeFullName}
        entityId={historyDrawer.entityId}
      />
    </PageContainer>
  );
};

export default AuditLogsPage;
