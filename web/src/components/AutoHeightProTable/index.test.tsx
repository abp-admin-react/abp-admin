import { act, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * AutoHeightProTable 可测纯逻辑：scroll.x 求和（含选择/展开隐形列宽）、
 * 列宽拖拽状态键、scroll 透传优先级，以及测量触发的状态机（视口未就绪守卫、
 * 稳定窗口轮询的生命周期与早停、fillViewport 短路、两个开发期告警）——
 * 通过让 ProTable mock 渲染 .ant-table-wrapper + rect/innerHeight 打桩覆盖。
 * 仍不在本环境覆盖的只有真实布局几何与 Pointer 拖拽物理。
 */

let capturedProps: Record<string, any> | undefined;

vi.mock('@ant-design/pro-components', () => ({
  // 渲染 .ant-table-wrapper：组件的测量/观察器接线都以它为锚点（回归测试依赖）
  ProTable: (props: any) => {
    capturedProps = props;
    return <div className="ant-table-wrapper" data-testid="pro-table" />;
  },
}));

vi.mock('antd-style', () => ({
  // 真实 useStyles() 返回 { styles, cx, theme }，styles 扁平承载各 key
  createStyles: () => () => ({
    styles: {
      wrap: 'wrap-class',
      tableFlex: 'table-flex-class',
      resizableTh: 'resizable-th-class',
      resizeHandle: 'resize-handle-class',
    },
  }),
}));

const { default: AutoHeightProTable } = await import('./index');

function renderTable(props: Record<string, any>) {
  capturedProps = undefined;
  const view = render(<AutoHeightProTable {...props} />);
  return {
    view,
    props: () => capturedProps as Record<string, any>,
  };
}

describe('AutoHeightProTable', () => {
  it('scroll.x 按列宽数字求和，字符串宽度不计入', () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b', width: '30%' },
        { title: 'C', dataIndex: 'c', width: 200 },
      ],
    });
    expect(props().scroll).toMatchObject({ x: 300 });
  });

  it('全列无数字宽度时不注入 scroll.x', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a' }],
    });
    expect(props().scroll?.x).toBeUndefined();
  });

  it('隐形列宽计入：rowSelection 默认 32、expandable 默认 48，可被 columnWidth 覆盖', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      rowSelection: {},
      expandable: { expandedRowRender: () => <div /> },
    });
    expect(props().scroll).toMatchObject({ x: 180 });

    const { props: props2 } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      rowSelection: { columnWidth: 60 },
    });
    expect(props2().scroll).toMatchObject({ x: 160 });
  });

  it('页面显式传入的 scroll.x / scroll.y 优先，不被组件覆盖', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      scroll: { x: 888, y: 666 },
    });
    expect(props().scroll).toMatchObject({ x: 888, y: 666 });
  });

  it('拖拽调宽：onResize 触发内部状态，该列 width 与 scroll.x 同步更新', async () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b', width: 100 },
      ],
    });
    const colA = props().columns.find((c: any) => c.dataIndex === 'a');
    colA.onHeaderCell(colA).onResize(260);

    await waitFor(() => {
      const current = props().columns.find((c: any) => c.dataIndex === 'a');
      expect(current.width).toBe(260);
      // scroll.x 同步：260 + 100
      expect(props().scroll).toMatchObject({ x: 360 });
    });
  });

  it('resizable=false 不注入 onHeaderCell', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      resizable: false,
    });
    expect(props().columns[0].onHeaderCell).toBeUndefined();
  });

  it('数字 width 列注入 onHeaderCell（width + onResize），无 width 列不注入', () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b' },
      ],
    });
    const colA = props().columns[0];
    const headerProps = colA.onHeaderCell(colA);
    expect(headerProps.width).toBe(100);
    expect(typeof headerProps.onResize).toBe('function');
    expect(props().columns[1].onHeaderCell).toBeUndefined();
  });

  it('fillViewport=false 不注入 section 高度与内部滚动', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      fillViewport: false,
    });
    expect(props().styles?.section).toBeUndefined();
  });

  // ---- 测量触发的状态机（webview 首开分页不可见 bug 的回归锁） ----

  // happy-dom/jsdom 无头环境的 document.hidden 默认是 true（不可见）——本组测试
  // 描述的都是前台页签行为，统一钉为可见；需要隐藏态的用例自行覆盖
  beforeEach(() => {
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => false,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
    vi.stubGlobal('innerHeight', 768); // happy-dom 默认值复位
    vi.unstubAllGlobals();
  });

  /** 固定几何桩：wrapTop=100、各盒高 800 —— wrapHeight 期望 768-100-32=636 */
  const stubRects = () =>
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockReturnValue({
      top: 100,
      bottom: 900,
      left: 0,
      right: 1000,
      x: 0,
      y: 100,
      width: 1000,
      height: 800,
      toJSON: () => ({}),
    } as DOMRect);

  it('视口未就绪（innerHeight=0）不固化高度：守卫保持自然布局，未就绪拍不消耗轮询窗口', () => {
    vi.useFakeTimers();
    vi.stubGlobal('innerHeight', 0);
    const { props, view } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
    });

    // 未就绪期间轮询空转 5 秒（50 拍）：既不测量固化高度，也不耗尽窗口
    act(() => vi.advanceTimersByTime(5000));
    expect(props().styles?.section).toBeUndefined();
    expect((view.container.firstChild as HTMLElement).style.height).toBe('');
    expect(vi.getTimerCount()).toBe(1);

    // 视口就绪 + 有真实布局后，下一拍即测得正确高度
    const rectSpy = stubRects();
    vi.stubGlobal('innerHeight', 768);
    act(() => vi.advanceTimersByTime(100));
    expect(props().styles?.section).toBeDefined();
    expect((view.container.firstChild as HTMLElement).style.height).toBe(
      '636px',
    );
    rectSpy.mockRestore();
  });

  it('隐藏页签跳过稳定轮询（不消耗窗口），回到可见的边沿立即补测', () => {
    vi.useFakeTimers();
    // 隐藏的 webview/标签页是"视口恒 0"的现实形态：Page Visibility 最佳实践要求
    // 不可见时不做不必要的工作——挂载首测与轮询都必须跳过（失真几何不得固化）
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => true,
    });
    const rectSpy = stubRects();
    const { props, view } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
    });

    // 隐藏期间空转 5 秒（远超 30 拍窗口）：不固化高度、定时器仍在（拍未被消耗）
    act(() => vi.advanceTimersByTime(5000));
    expect(props().styles?.section).toBeUndefined();
    expect(vi.getTimerCount()).toBe(1);

    // 回到可见：visibilitychange 边沿同步补测，不等下一个 100ms 节拍
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => false,
    });
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    expect(props().styles?.section).toBeDefined();
    expect((view.container.firstChild as HTMLElement).style.height).toBe(
      '636px',
    );
    rectSpy.mockRestore();
  });

  it('轮询稳定早停：结果连续一致的拍数达到阈值后定时器自清', () => {
    vi.useFakeTimers();
    const rectSpy = stubRects();
    const { view } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
    });

    expect(vi.getTimerCount()).toBe(1);
    // 636px 连续 3 拍一致（含首拍建立基线）→ 第 4 拍内收工，远早于 30 拍上限
    act(() => vi.advanceTimersByTime(400));
    expect(vi.getTimerCount()).toBe(0);
    expect((view.container.firstChild as HTMLElement).style.height).toBe(
      '636px',
    );
    rectSpy.mockRestore();
  });

  it('卸载清理：settleTimer 随卸载清除，不残留定时器', () => {
    vi.useFakeTimers();
    const { view } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
    });
    expect(vi.getTimerCount()).toBe(1);
    view.unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it('fillViewport=false 不安装任何轮询/监听（零定时器）', () => {
    vi.useFakeTimers();
    renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      fillViewport: false,
    });
    expect(vi.getTimerCount()).toBe(0);
  });

  it('开发期告警：自带 components.header.cell 而未关 resizable 时提示覆盖风险', () => {
    vi.stubEnv('NODE_ENV', 'development');
    const warnSpy = vi
      .spyOn(console, 'warn')
      .mockImplementation(() => undefined);

    renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      components: {
        header: { cell: (p: unknown) => <th {...(p as object)} /> },
      },
    });
    expect(warnSpy).toHaveBeenCalledWith(expect.stringMatching(/resizable/));

    warnSpy.mockClear();
    renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      resizable: false,
      components: {
        header: { cell: (p: unknown) => <th {...(p as object)} /> },
      },
    });
    // 只断言 resizable 覆盖告警消失——锚点缺失告警（另一契约）在本测试环境
    // 没有 PageContainer 时仍会触发，与 resizable 无关
    expect(
      warnSpy.mock.calls.some((args) => /resizable/.test(String(args[0]))),
    ).toBe(false);
  });

  it('开发期告警：fillViewport 模式下找不到 PageContainer 内容容器时提示契约降级', () => {
    vi.stubEnv('NODE_ENV', 'development');
    const warnSpy = vi
      .spyOn(console, 'warn')
      .mockImplementation(() => undefined);

    renderTable({ columns: [{ title: 'A', dataIndex: 'a', width: 100 }] });
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('.ant-pro-page-container-children-container'),
    );
  });

  it('自校正熔断：固定高度祖先（容器底不随表格移动）连续同向修正达阈值后停用，退回基础公式', () => {
    vi.useFakeTimers();
    vi.stubEnv('NODE_ENV', 'development');
    const warnSpy = vi
      .spyOn(console, 'warn')
      .mockImplementation(() => undefined);
    // 固定几何桩：wrap top=100/height=800、PageContainer 内容容器 bottom=900 恒定——
    // 无论 wrapHeight 设成多少容器底边都不动（真实世界对应固定高度面板/误用场景）。
    // 基础公式 = 768-100-32=636；投影 delta = (900-(800-636))-768 = -32 恒负：
    // 拍 1/2 各修正一次到 668（streak=1/2），拍 3 streak 达 3 → 熔断 + 告警，保持 636
    const rectSpy = stubRects();

    const view = render(
      <div className="ant-pro-page-container-children-container">
        <AutoHeightProTable
          columns={[{ title: 'A', dataIndex: 'a', width: 100 }]}
        />
      </div>,
    );

    // wrap 是组件根 div（PageContainer 包裹 div 的第一个子元素）
    const wrap = view.container.firstElementChild
      ?.firstElementChild as HTMLElement;

    // 熔断前的修正确实发生过：挂载期首测 + 前 1-2 拍把高度推到修正值 668（= 636 - delta(-32)）
    // ——不钉这一步，熔断阈值退化成 1（首测即熔断、零合法修正）时最终断言照样绿
    act(() => vi.advanceTimersByTime(150));
    expect(wrap.style.height).toBe('668px');

    act(() => vi.advanceTimersByTime(1000));
    expect(wrap.style.height).toBe('636px');
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('自校正连续多次未收敛'),
    );

    // 熔断后（disabled 生命周期内不恢复）继续推进时间也不再改高度：稳定在基础公式值
    const callsBefore = warnSpy.mock.calls.length;
    act(() => vi.advanceTimersByTime(2000));
    expect(wrap.style.height).toBe('636px');
    expect(warnSpy.mock.calls.length).toBe(callsBefore);
    rectSpy.mockRestore();
  });
});
