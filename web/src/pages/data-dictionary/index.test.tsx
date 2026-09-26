import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as service from './service';

// Mock 服务层：页面不直接依赖生成客户端，mock 这一层即可
vi.mock('./service', async () => {
  const actual = await vi.importActual<typeof import('./service')>('./service');
  return {
    ...actual,
    getDataDictionaries: vi.fn(),
    getDataDictionaryByCode: vi.fn(),
    createDataDictionary: vi.fn(),
    deleteDataDictionary: vi.fn(),
    saveDataDictionaryItems: vi.fn(),
  };
});

vi.mock('@umijs/max', () => ({
  useAccess: () => ({
    canViewDataDictionary: true,
    canCreateDataDictionary: true,
    canUpdateDataDictionary: true,
    canDeleteDataDictionary: true,
  }),
  // 字典标签色白名单来自 initialState（application-configuration 下发，见组件内注释）
  useModel: () => ({
    initialState: {
      dataDictionaryTagTypes: [
        'success',
        'processing',
        'error',
        'warning',
        'default',
        'blue',
      ],
    },
  }),
}));

// Mock ProComponents：ProTable 真正执行 request 并渲染可点击的行；
// EditableProTable 渲染传入的完整 items。其余保留语义化 testid。
vi.mock('@ant-design/pro-components', async () => {
  const React = await import('react');
  return {
    PageContainer: ({ children }: any) => (
      <div data-testid="page-container">{children}</div>
    ),
    ProCard: ({ children }: any) => (
      <div data-testid="pro-card">{children}</div>
    ),
    ProTable: ({ columns, toolBarRender, request, onRow }: any) => {
      const [rows, setRows] = React.useState<any[]>([]);
      React.useEffect(() => {
        Promise.resolve(request?.({ current: 1, pageSize: 100 }, {}, {})).then(
          (r: any) => setRows(r?.data ?? []),
        );
      }, [request]);
      return (
        <div data-testid="dictionary-table">
          <div data-testid="table-columns">
            {columns?.map((col: any, i: number) => (
              <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
                {col.title}
              </div>
            ))}
          </div>
          {rows.map((row: any) => (
            <button
              type="button"
              key={row.id}
              data-testid={`dict-row-${row.code}`}
              onClick={() => onRow?.(row)?.onClick?.()}
            >
              {row.displayText}
            </button>
          ))}
          {toolBarRender && <div data-testid="toolbar">{toolBarRender()}</div>}
        </div>
      );
    },
    EditableProTable: ({ value, columns }: any) => (
      <div data-testid="item-editor">
        {columns?.map((col: any, i: number) => (
          <span key={col.dataIndex ?? i} data-testid="item-column">
            {col.title}
          </span>
        ))}
        {(value ?? []).map((row: any) => (
          <div key={row.code} data-testid={`item-row-${row.code}`}>
            {row.code}:{row.displayText}
          </div>
        ))}
      </div>
    ),
    ModalForm: ({ children, title, trigger }: any) => (
      <div data-testid="modal-form" data-title={title}>
        {trigger}
        {children}
      </div>
    ),
    ProFormText: ({ label }: any) => <input aria-label={label} />,
    ProFormTextArea: ({ label }: any) => <textarea aria-label={label} />,
  };
});

const genderDict = {
  id: 'dict-1',
  code: 'Gender',
  displayText: '性别',
  isStatic: true,
  items: [
    { code: 'Unknown', displayText: '未知' },
    { code: 'Male', displayText: '男' },
  ],
};

const customDict = {
  id: 'dict-2',
  code: 'CustomerLevel',
  displayText: '客户等级',
  isStatic: false,
  items: [{ code: 'VIP', displayText: '贵宾' }],
};

const renderPage = async () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const { default: Page } = await import('./index');
  return render(
    <QueryClientProvider client={queryClient}>
      {/* App.useApp() 需要 antd App 上下文，否则 message 是空对象 */}
      <AntdApp>
        <Page />
      </AntdApp>
    </QueryClientProvider>,
  );
};

describe('DataDictionaryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(service.getDataDictionaries).mockResolvedValue({
      items: [genderDict, customDict],
      totalCount: 2,
    } as never);
    // 按 code 分流两个夹具：Gender=静态字典（项不可删、编码不可改），CustomerLevel=非静态
    vi.mocked(service.getDataDictionaryByCode).mockImplementation(
      (code: string) =>
        Promise.resolve(
          code === 'Gender'
            ? {
                code: 'Gender',
                displayText: '性别',
                isStatic: true,
                items: [
                  {
                    code: 'Unknown',
                    displayText: '未知',
                    isStatic: true,
                    order: 0,
                  },
                  {
                    code: 'Male',
                    displayText: '男',
                    isStatic: true,
                    tagType: 'blue',
                    order: 1,
                  },
                ],
              }
            : {
                code: 'CustomerLevel',
                displayText: '客户等级',
                isStatic: false,
                items: [{ code: 'VIP', displayText: '贵宾', order: 0 }],
              },
        ) as never,
    );
    vi.mocked(service.saveDataDictionaryItems).mockResolvedValue({} as never);
  });

  it('挂载即请求字典列表', async () => {
    await renderPage();

    await waitFor(() => {
      expect(service.getDataDictionaries).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 100 }),
      );
    });
  });

  it('渲染规格要求的列（含静态标识）', async () => {
    await renderPage();

    const columns = await screen.findAllByTestId('column');
    const titles = columns.map((c) => c.textContent);
    expect(titles).toContain('编码');
    expect(titles).toContain('显示名');
    expect(titles).toContain('静态');
    expect(titles).toContain('操作');
  });

  it('工具栏有新建字典入口', async () => {
    await renderPage();

    const toolbar = await screen.findByTestId('toolbar');
    expect(toolbar.textContent).toContain('新建字典');
  });

  it('选中字典后右侧加载其全部项（不分页）', async () => {
    await renderPage();

    fireEvent.click(await screen.findByTestId('dict-row-Gender'));

    await waitFor(() => {
      expect(service.getDataDictionaryByCode).toHaveBeenCalledWith('Gender');
    });
    expect(await screen.findByTestId('item-row-Unknown')).toBeTruthy();
    expect(await screen.findByTestId('item-row-Male')).toBeTruthy();
  });

  it('保存是单端点原子提交：显示信息 + 完整 items + 展示元数据一次 PUT', async () => {
    await renderPage();

    fireEvent.click(await screen.findByTestId('dict-row-CustomerLevel'));
    await screen.findByTestId('item-row-VIP');

    fireEvent.click(screen.getByText('保存全部字典项'));

    await waitFor(() => {
      // 原子保存：同一后端工作单元提交，替代"模块 UpdateAsync + items-meta"两次调用
      expect(service.saveDataDictionaryItems).toHaveBeenCalledWith(
        'CustomerLevel',
        expect.objectContaining({
          displayText: '客户等级',
          items: [
            {
              code: 'VIP',
              displayText: '贵宾',
              description: undefined,
              tagType: undefined,
            },
          ],
        }),
      );
    });
  });

  it('保存失败时错误提示可见且不出现已保存', async () => {
    await renderPage();

    fireEvent.click(await screen.findByTestId('dict-row-CustomerLevel'));
    await screen.findByTestId('item-row-VIP');

    // 服务端整单拒绝（编码重复/静态结构锁/非法 tagType）：错误必须可见，
    // 吞掉异常时弹窗只是转一下 spinner，用户不知道保存没发生
    vi.mocked(service.saveDataDictionaryItems).mockRejectedValueOnce(
      new Error('编码不能重复'),
    );

    fireEvent.click(screen.getByText('保存全部字典项'));

    expect(await screen.findByText('编码不能重复')).toBeTruthy();
    expect(screen.queryByText('已保存')).toBeNull();
  });

  it('静态字典显示只读提示', async () => {
    await renderPage();

    fireEvent.click(await screen.findByTestId('dict-row-Gender'));
    await screen.findByTestId('item-row-Male');

    expect(screen.getByText(/这是静态字典/)).toBeTruthy();
  });
});
