import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { message } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as proModules from '@/abp/proModules';

// T2.1 验收：覆盖「无导出权限时按钮隐藏、点击导出调用了正确的接口与参数」。
// 权限可变的 mock：默认有导出权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = { canExportAuditLogs: true };

// 页面支持 ?correlationId= 跨日志串联入口：mock 可变，供深链用例切换
let mockSearchParams = new URLSearchParams();

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useNavigate: () => vi.fn(),
  useSearchParams: () => [mockSearchParams, vi.fn()],
}));

// Mock 服务层：页面与 EntityChangeHistoryDrawer 都从这里取数
vi.mock('@/abp/proModules', async () => {
  const actual =
    await vi.importActual<typeof import('@/abp/proModules')>(
      '@/abp/proModules',
    );
  return {
    ...actual,
    getAuditLogs: vi.fn(),
    getAuditLog: vi.fn(),
    exportAuditLogs: vi.fn(),
    getEntityChangeHistory: vi.fn(),
  };
});

// 数据字典筛选下拉（T3.4）在测试中固定为空选项
vi.mock('@/utils/dictionary', () => ({
  dictionaryRequest: () => async () => [],
}));

// 二级抽屉不展开，打桩即可
vi.mock('./EntityChangeHistoryDrawer', () => ({
  default: () => <div data-testid="entity-change-history-drawer" />,
}));

// Mock ProComponents：保留语义化 testid，便于断言
let capturedAuditRequest:
  | ((
      params: Record<string, unknown>,
      sorter?: unknown,
      filter?: Record<string, unknown[]>,
    ) => Promise<unknown>)
  | undefined;
let capturedAuditOnChange:
  | ((pagination: unknown, filters: Record<string, unknown[]>) => void)
  | undefined;

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProDescriptions: () => <div data-testid="pro-descriptions" />,
  ProTable: ({ columns, toolbar, request, onChange }: any) => {
    // 捕获 request / onChange 供筛选与导出用例直接驱动（列头筛选流程）
    capturedAuditRequest = request;
    capturedAuditOnChange = onChange;
    // 模拟 ProTable 挂载即请求数据（列头筛选值经 request 第三参 filter 传入）
    request?.({ current: 1, pageSize: 10 }, {}, {});
    return (
      <div data-testid="pro-table">
        <div data-testid="table-columns">
          {columns?.map((col: any, i: number) => (
            <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
              {col.title}
            </div>
          ))}
        </div>
        <div data-testid="toolbar">{toolbar?.actions}</div>
      </div>
    );
  },
}));

describe('AuditLogsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams = new URLSearchParams();
    mockAccess = { canExportAuditLogs: true };
    vi.mocked(proModules.getAuditLogs).mockResolvedValue({
      items: [],
      totalCount: 0,
    } as never);
    vi.mocked(proModules.exportAuditLogs).mockResolvedValue({
      isQueued: true,
    } as never);
  });

  it('挂载即请求审计日志列表', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    await waitFor(() => {
      expect(proModules.getAuditLogs).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 10 }),
      );
    });
  });

  it('「仅未处理错误」把 select 字符串值转成布尔，清空时不传', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);
    await waitFor(() => expect(capturedAuditRequest).toBeDefined());

    // 列头筛选择的值以数组形式经 request 第三参 filter 传入（字符串 'true' → 布尔 true）。
    // 注意键是列的 dataIndex：「仅未处理错误」筛选挂在处理状态列（isHandled）上
    await capturedAuditRequest?.(
      { current: 1, pageSize: 10 },
      {},
      { isHandled: ['true'], correlationId: ['corr-abc'] },
    );
    expect(proModules.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        unhandledErrorOnly: true,
        correlationId: 'corr-abc',
      }),
    );

    // 「是否异常」筛选挂在状态列（httpStatusCode）上：字符串三态 → 布尔
    await capturedAuditRequest?.(
      { current: 1, pageSize: 10 },
      {},
      { httpStatusCode: ['true'] },
    );
    expect(proModules.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ hasException: true }),
    );

    // 清空筛选（filter 无该键）→ 不携带该参数（不回落到 URL 初始值）
    await capturedAuditRequest?.({ current: 1, pageSize: 10 }, {}, {});
    expect(proModules.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        unhandledErrorOnly: undefined,
        correlationId: undefined,
      }),
    );
  });

  it('深链 ?correlationId= 预填关联 ID 列头筛选（受控初值，清空不粘）', async () => {
    mockSearchParams = new URLSearchParams('?correlationId=corr-9');
    const { default: Page } = await import('./index');
    render(<Page />);

    // 受控初值作用于请求：挂载请求应带 CorrelationId（首请求 filter 非空）
    await waitFor(() => {
      expect(proModules.getAuditLogs).toHaveBeenCalledWith(
        expect.objectContaining({ correlationId: 'corr-9' }),
      );
    });
  });

  it('无导出权限时按钮隐藏', async () => {
    mockAccess = { canExportAuditLogs: false };
    const { default: Page } = await import('./index');
    render(<Page />);

    await screen.findByTestId('toolbar');
    expect(screen.queryByText('导出 Excel')).toBeNull();
  });

  it('有导出权限时显示导出按钮', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    expect(await screen.findByText('导出 Excel')).toBeTruthy();
  });

  it('点击导出带当前筛选条件调用导出接口，202 时提示转后台', async () => {
    const infoSpy = vi
      .spyOn(message, 'info')
      .mockImplementation(() => ({}) as never);
    const { default: Page } = await import('./index');
    render(<Page />);

    // 模拟列头筛选：受控 onChange 更新页面 filters 状态，导出读同一份状态。
    // 「关联 ID」「仅未处理错误」「是否异常」同口径作用于导出（同步与异步链路）
    capturedAuditOnChange?.(
      {},
      {
        httpMethod: ['GET'],
        url: ['/api/test'],
        correlationId: ['corr-1'],
        isHandled: ['true'],
        httpStatusCode: ['true'],
      },
    );
    fireEvent.click(await screen.findByText('导出 Excel'));

    await waitFor(() => {
      // 正确的接口：exportAuditLogs；正确的参数：列头筛选的当前筛选条件
      expect(proModules.exportAuditLogs).toHaveBeenCalledWith({
        httpMethod: 'GET',
        url: '/api/test',
        correlationId: 'corr-1',
        unhandledErrorOnly: true,
        userName: undefined,
        startTime: undefined,
        endTime: undefined,
        hasException: true,
      });
    });
    await waitFor(() => {
      expect(infoSpy).toHaveBeenCalledWith(
        '结果集较大，已转为后台任务，完成后将邮件通知',
      );
    });
    infoSpy.mockRestore();
  });
});
