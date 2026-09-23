import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { message } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as proModules from '@/abp/proModules';

// T2.1 验收：覆盖「无导出权限时按钮隐藏、点击导出调用了正确的接口与参数」。
// 权限可变的 mock：默认有导出权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = { canExportAuditLogs: true };

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useNavigate: () => vi.fn(),
  // 页面支持 ?correlationId= 跨日志串联入口，测试环境给空查询串
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
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
  | ((params: Record<string, unknown>) => Promise<unknown>)
  | undefined;

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProDescriptions: () => <div data-testid="pro-descriptions" />,
  ProTable: ({ columns, toolbar, request, formRef }: any) => {
    // 捕获 request 供筛选转换用例直接驱动（ProTable select 值恒为字符串）
    capturedAuditRequest = request;
    // 模拟 ProTable 挂载即请求数据。搜索表单值只通过 formRef prop 暴露
    // （actionRef.current 上没有 formRef 成员——这是 pro-components v3 的真实行为，
    // 页面代码也只应从 formRef prop 拿筛选值），mock 不再凭空提供 actionRef.formRef。
    request?.({ current: 1, pageSize: 10 }, {}, {});
    if (formRef) {
      formRef.current = {
        getFieldsValue: () => ({ httpMethod: 'GET', url: '/api/test' }),
      };
    }
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

    // select 选中的是字符串 'true' → 服务层收到布尔 true
    await capturedAuditRequest?.({
      current: 1,
      pageSize: 10,
      unhandledErrorOnly: 'true',
      correlationId: 'corr-abc',
    });
    expect(proModules.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        unhandledErrorOnly: true,
        correlationId: 'corr-abc',
      }),
    );

    // 清空筛选（undefined）→ 不携带该参数（不回落到 URL 初始值）
    await capturedAuditRequest?.({
      current: 1,
      pageSize: 10,
      unhandledErrorOnly: undefined,
      correlationId: undefined,
    });
    expect(proModules.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        unhandledErrorOnly: undefined,
        correlationId: undefined,
      }),
    );
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

    fireEvent.click(await screen.findByText('导出 Excel'));

    await waitFor(() => {
      // 正确的接口：exportAuditLogs；正确的参数：ProTable 搜索表单的当前筛选条件
      expect(proModules.exportAuditLogs).toHaveBeenCalledWith({
        httpMethod: 'GET',
        url: '/api/test',
        userName: undefined,
        startTime: undefined,
        endTime: undefined,
        hasException: undefined,
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
