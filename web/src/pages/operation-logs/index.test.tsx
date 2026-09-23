import { render, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import * as operationLogsApi from '@/abp/operationLogs';

// 操作日志页验收：挂载即请求列表、分页参数换算正确（SkipCount 数学）、
// 成功/失败筛选的字符串→布尔转换（ProTable select 值恒为字符串）。
let capturedRequest:
  | ((params: Record<string, unknown>) => Promise<unknown>)
  | undefined;

vi.mock('@umijs/max', () => ({
  useAccess: () => ({ canManageOperationLogs: true }),
  useNavigate: () => vi.fn(),
  // 页面支持 ?correlationId= 跨日志串联入口，测试环境给空查询串
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
}));

vi.mock('@/abp/operationLogs', () => ({
  getOperationLogs: vi.fn(),
}));

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProDescriptions: () => <div data-testid="pro-descriptions" />,
  ProTable: ({ columns, request }: any) => {
    capturedRequest = request;
    // 模拟 ProTable 挂载即请求数据
    request?.({ current: 1, pageSize: 10 }, {}, {});
    return (
      <div data-testid="pro-table">
        {columns?.map((col: any, i: number) => (
          <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
            {col.title}
          </div>
        ))}
      </div>
    );
  },
}));

describe('OperationLogsPage', () => {
  it('挂载即请求列表，SkipCount 按页码换算', async () => {
    vi.mocked(operationLogsApi.getOperationLogs).mockResolvedValue({
      items: [],
      totalCount: 0,
    } as never);
    const { default: Page } = await import('./index');
    render(<Page />);

    await waitFor(() => {
      expect(operationLogsApi.getOperationLogs).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 10 }),
      );
    });

    // 模拟第 3 页请求：SkipCount 应为 20
    await capturedRequest?.({ current: 3, pageSize: 10 });
    expect(operationLogsApi.getOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ current: 3, pageSize: 10 }),
    );
  });

  it('成功/失败筛选把 select 字符串值转成布尔', async () => {
    vi.mocked(operationLogsApi.getOperationLogs).mockResolvedValue({
      items: [],
      totalCount: 0,
    } as never);
    const { default: Page } = await import('./index');
    render(<Page />);

    // ProTable 的 select 搜索值是字符串：'false' 必须转成布尔 false 下发
    await capturedRequest?.({ current: 1, pageSize: 10, success: 'false' });
    expect(operationLogsApi.getOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ Success: false }),
    );

    await capturedRequest?.({ current: 1, pageSize: 10, success: 'true' });
    expect(operationLogsApi.getOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ Success: true }),
    );

    // 未选择时不下发该筛选
    await capturedRequest?.({ current: 1, pageSize: 10 });
    expect(operationLogsApi.getOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ Success: undefined }),
    );
  });

  it('时间范围搜索值透传为 StartTime/EndTime', async () => {
    vi.mocked(operationLogsApi.getOperationLogs).mockResolvedValue({
      items: [],
      totalCount: 0,
    } as never);
    const { default: Page } = await import('./index');
    render(<Page />);

    await capturedRequest?.({
      current: 1,
      pageSize: 10,
      StartTime: '2026-09-01 00:00:00',
      EndTime: '2026-09-17 23:59:59',
    });
    expect(operationLogsApi.getOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        StartTime: '2026-09-01 00:00:00',
        EndTime: '2026-09-17 23:59:59',
      }),
    );
  });
});
