import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as proModules from '@/abp/proModules';

// T2.1 验收：覆盖「无导出权限时按钮隐藏、点击导出调用了正确的接口与参数」。
// 权限可变的 mock：默认有导出权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = { canExportAuditLogs: true };

// 页面支持 ?correlationId= 跨日志串联入口：mock 可变，供深链用例切换
let mockSearchParams = new URLSearchParams();

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useLocation: () => ({
    pathname: '/administration/audit-logs',
    search: `?${mockSearchParams}`,
  }),
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
const queries = vi.hoisted(() => ({ read: vi.fn() }));
vi.mock('@/abp/gridQuery', () => ({ postGridQuery: queries.read }));
vi.mock('ag-grid-react', () => import('../../../tests/gridTestDouble'));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => <main>{children}</main>,
  ProDescriptions: () => null,
}));

describe('审计日志', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams = new URLSearchParams();
    mockAccess = { canExportAuditLogs: true };
    queries.read.mockResolvedValue({ items: [], totalCount: 0 });
    vi.mocked(proModules.exportAuditLogs).mockResolvedValue({ isQueued: true });
  });

  it('加载全量查询接口并将深链关联 ID 作为可清除的初始列条件', async () => {
    mockSearchParams.set('correlationId', 'trace-123');
    const { default: Page } = await import('./index');
    render(<Page />);
    await waitFor(() =>
      expect(queries.read).toHaveBeenCalledWith(
        '/api/app/audit-log/query',
        expect.objectContaining({
          filters: [
            {
              field: 'correlationId',
              join: 'and',
              conditions: [{ op: 'eq', value: 'trace-123' }],
            },
          ],
        }),
        expect.any(AbortSignal),
      ),
    );
    fireEvent.click(screen.getByRole('button', { name: '清空筛选' }));
    await waitFor(() =>
      expect(queries.read).toHaveBeenLastCalledWith(
        '/api/app/audit-log/query',
        expect.objectContaining({ filters: [] }),
        expect.any(AbortSignal),
      ),
    );
  });

  it('仅授权用户可导出，导出收到与列表相同的列条件', async () => {
    mockSearchParams.set('correlationId', 'trace-123');
    const { default: Page } = await import('./index');
    const view = render(<Page />);
    fireEvent.click(screen.getByRole('button', { name: /导出 Excel/ }));
    await waitFor(() =>
      expect(proModules.exportAuditLogs).toHaveBeenCalledWith({
        grid: expect.objectContaining({
          filters: [
            {
              field: 'correlationId',
              join: 'and',
              conditions: [{ op: 'eq', value: 'trace-123' }],
            },
          ],
        }),
      }),
    );
    mockAccess = {};
    view.rerender(<Page />);
    expect(screen.queryByRole('button', { name: /导出 Excel/ })).toBeNull();
  });
});
