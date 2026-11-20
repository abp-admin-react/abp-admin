import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import type {
  GridApi,
  GridReadyEvent,
  IGetRowsParams,
} from 'ag-grid-community';
import type { AgGridReactProps } from 'ag-grid-react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { type OperationLogDto, queryOperationLogs } from '@/abp/operationLogs';
import Page from './index';

let grid: AgGridReactProps<OperationLogDto>;
let url = new URLSearchParams();
let auditAccess = false;
vi.mock('ag-grid-react', () => ({
  AgGridReact: (props: AgGridReactProps<OperationLogDto>) => {
    grid = props;
    return <table />;
  },
}));
vi.mock('@umijs/max', () => ({
  useAccess: () => ({ canManageAuditLogs: auditAccess }),
  useNavigate: () => vi.fn(),
  useSearchParams: () => [url, vi.fn()],
}));
vi.mock('@/abp/operationLogs', () => ({ queryOperationLogs: vi.fn() }));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: React.ReactNode }) => (
    <main>{children}</main>
  ),
  ProDescriptions: () => null,
}));

function getRows(
  overrides: Partial<IGetRowsParams<OperationLogDto>> = {},
  datasource = grid.datasource,
) {
  const request: IGetRowsParams<OperationLogDto> = {
    startRow: 0,
    endRow: 10,
    filterModel: {},
    sortModel: [],
    context: {},
    api: {
      isDestroyed: () => false,
      setGridOption: vi.fn(),
      getGridOption: () => grid.datasource,
      updateGridOptions: vi.fn(),
    } as unknown as GridApi<OperationLogDto>,
    successCallback: vi.fn(),
    failCallback: vi.fn(),
    ...overrides,
  };
  if (!datasource) throw new Error('缺少远程数据源');
  return { request, done: datasource.getRows(request) };
}

describe('操作日志远程网格', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    url = new URLSearchParams();
    auditAccess = false;
    vi.mocked(queryOperationLogs).mockResolvedValue({
      items: [],
      totalCount: 0,
    });
  });

  it.each([true, false])('关联审计链接随目标权限显示（授权=%s）', (allowed) => {
    auditAccess = allowed;
    render(<Page />);
    const col = grid.columnDefs?.find(
      (column) => 'field' in column && column.field === 'correlationId',
    );
    if (!col || !('cellRenderer' in col)) throw new Error('缺少关联 ID 列');
    render(col.cellRenderer({ value: 'trace-123' }));
    expect(screen.getByText('trace-123')).toBeInTheDocument();
    expect(!!screen.queryByText('查关联审计日志')).toBe(allowed);
  });

  it('深链仅作为原生网格的初始条件，清空后的请求不再包含关联 ID', async () => {
    url = new URLSearchParams('correlationId=test-id');
    render(<Page />);
    expect(grid.initialState?.filter?.filterModel).toEqual({
      correlationId: { filterType: 'text', type: 'equals', filter: 'test-id' },
    });
    await getRows().done;
    expect(queryOperationLogs).toHaveBeenCalledWith(
      expect.objectContaining({ filters: [] }),
      expect.any(AbortSignal),
    );
  });

  it('向服务端同时传递分页、失败筛选和耗时排序', async () => {
    render(<Page />);
    const { request, done } = getRows({
      startRow: 20,
      endRow: 30,
      filterModel: { success: { filterType: 'text', type: 'isFalse' } },
      sortModel: [{ colId: 'duration', sort: 'desc' }],
    });
    await done;
    expect(queryOperationLogs).toHaveBeenCalledWith(
      {
        skipCount: 20,
        maxResultCount: 10,
        sort: [{ field: 'duration', direction: 'desc' }],
        filters: [
          {
            field: 'success',
            join: 'and',
            conditions: [{ op: 'eq', value: 'false' }],
          },
        ],
      },
      expect.any(AbortSignal),
    );
    expect(request.successCallback).toHaveBeenCalledWith([], 0);
  });

  it('保留四字段关键词搜索并与列条件一起传递', async () => {
    render(<Page />);
    fireEvent.change(screen.getByLabelText('跨列搜索操作日志'), {
      target: { value: ' invoice ' },
    });
    fireEvent.click(screen.getByRole('button', { name: '搜 索' }));
    await getRows({
      filterModel: {
        type: { filterType: 'text', type: 'contains', filter: '订单' },
      },
    }).done;
    expect(queryOperationLogs).toHaveBeenCalledWith(
      expect.objectContaining({
        keyword: 'invoice',
        filters: [
          {
            field: 'type',
            join: 'and',
            conditions: [{ op: 'contains', value: '订单' }],
          },
        ],
      }),
      expect.any(AbortSignal),
    );
  });

  it('输入法确认和未提交的草稿保留原查询，提交及清空关键词回到首页并保留列条件', async () => {
    render(<Page />);
    const goToFirst = vi.fn();
    act(() =>
      grid.onGridReady?.({
        api: {
          setGridAriaProperty: vi.fn(),
          paginationGoToFirstPage: goToFirst,
        },
      } as unknown as GridReadyEvent<OperationLogDto>),
    );
    const initial = grid.datasource;
    const input = screen.getByLabelText('跨列搜索操作日志');
    fireEvent.compositionStart(input);
    fireEvent.change(input, { target: { value: '  运维  ' } });
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 13, isComposing: true });
    fireEvent.compositionEnd(input);
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 229 });
    fireEvent.keyUp(input, { key: 'Enter' });
    expect(grid.datasource).toBe(initial);
    expect(goToFirst).not.toHaveBeenCalled();

    fireEvent.keyDown(input, { key: 'Enter', keyCode: 13 });
    expect(grid.datasource).not.toBe(initial);
    expect(input).toHaveValue('运维');
    expect(goToFirst).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: /清除|clear/i }));
    const filters = { success: { filterType: 'text', type: 'isFalse' } };
    await getRows({ filterModel: filters }).done;
    expect(goToFirst).toHaveBeenCalledTimes(2);
    const query = vi.mocked(queryOperationLogs).mock.lastCall?.[0];
    expect(query).toMatchObject({ filters: [{ field: 'success' }] });
    expect(query).not.toHaveProperty('keyword');
  });

  it('新关键词替换数据源后，旧查询及延迟派发的旧块都不能覆盖结果；当前实例仍可重新挂载', async () => {
    let resolveOld = () => {};
    vi.mocked(queryOperationLogs).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveOld = () => resolve({ items: [], totalCount: 100 });
        }),
    );
    render(<Page />);
    const old = grid.datasource;
    const pending = getRows({}, old);
    fireEvent.change(screen.getByLabelText('跨列搜索操作日志'), {
      target: { value: '订单' },
    });
    fireEvent.click(screen.getByRole('button', { name: '搜 索' }));
    expect(grid.datasource).not.toBe(old);
    old?.destroy?.();
    const late = getRows({}, old);
    await late.done;
    expect(queryOperationLogs).toHaveBeenCalledTimes(1);
    expect(late.request.api.setGridOption).not.toHaveBeenCalled();
    expect(late.request.failCallback).toHaveBeenCalledOnce();
    resolveOld();
    await pending.done;
    expect(pending.request.successCallback).not.toHaveBeenCalled();

    grid.datasource?.destroy?.();
    const current = getRows();
    await current.done;
    expect(current.request.successCallback).toHaveBeenCalledWith([], 0);
    expect(queryOperationLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: '订单' }),
      expect.any(AbortSignal),
    );
  });

  it('接口失败时显示可重试状态', async () => {
    vi.mocked(queryOperationLogs).mockRejectedValue(new Error('加载失败'));
    render(<Page />);
    const { request, done } = getRows();
    await done;
    await waitFor(() => expect(request.failCallback).toHaveBeenCalledTimes(1));
    expect(request.successCallback).not.toHaveBeenCalled();
    expect(request.api.setGridOption).toHaveBeenCalledWith(
      'activeOverlay',
      expect.any(Function),
    );
  });

  it('更换筛选后，迟到的旧响应不会覆盖当前记录', async () => {
    let release = () => {};
    vi.mocked(queryOperationLogs).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          release = () => resolve({ items: [], totalCount: 99 });
        }),
    );
    render(<Page />);
    const old = getRows();
    const current = getRows({
      filterModel: { success: { filterType: 'text', type: 'isTrue' } },
    });
    await current.done;
    release();
    await old.done;
    expect(vi.mocked(queryOperationLogs).mock.calls[0][1].aborted).toBe(true);
    expect(old.request.successCallback).not.toHaveBeenCalled();
    expect(current.request.successCallback).toHaveBeenCalledWith([], 0);
  });

  it('服务端校验失败定位到列，修改条件后可继续加载', async () => {
    vi.mocked(queryOperationLogs).mockRejectedValueOnce({
      response: {
        status: 400,
        data: {
          error: {
            details: '不应展示的内部详情',
            validationErrors: [
              { members: ['duration'], message: '内部校验消息' },
            ],
          },
        },
      },
    });
    render(<Page />);
    const invalid = getRows({
      filterModel: {
        duration: { filterType: 'number', type: 'equals', filter: 1.5 },
      },
    });
    await invalid.done;
    expect(invalid.request.api.updateGridOptions).toHaveBeenCalledWith({
      activeOverlay: expect.any(Function),
      activeOverlayParams: {
        message: '「耗时（ms）」筛选值无效。请检查数字的范围和精度。',
      },
    });
    const corrected = getRows({
      filterModel: {
        duration: { filterType: 'number', type: 'equals', filter: 1 },
      },
    });
    await corrected.done;
    expect(corrected.request.successCallback).toHaveBeenCalledWith([], 0);
  });
});
