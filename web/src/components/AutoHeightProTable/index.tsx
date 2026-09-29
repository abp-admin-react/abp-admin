import type {
  ProCardProps,
  ProColumns,
  ProTableProps,
} from '@ant-design/pro-components';
import { ProTable } from '@ant-design/pro-components';
import { createStyles } from 'antd-style';
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

/**
 * 撑满视口剩余高度的 ProTable 封装，统一解决全站列表页四个问题：
 *
 * 1. 分页固定在底部，不再随行数上下飘。
 *    采用 antd v6 官网 auto-height demo 的方案：给表格一个确定高度，
 *    ResizeObserver 实测「表高 − 表头 − 分页」得到 scroll.y，表体内部滚动。
 *    （参考 https://ant.design/components/table#components-table-demo-auto-height）
 *
 * 2. 表格不被撑破：scroll.x 按列宽自动求和（rc-table 会给 table 设
 *    width/min-width:100%），列总宽超出容器时出横向滚动条而不是撑开页面。
 *    选择列/展开列等隐形列宽也已计入（见 scrollX 计算）。
 *
 * 3. 表头可拖拽调宽：沿用官网 resizable-column demo 的接线
 *    （components.header.cell + onHeaderCell 注入宽度），拖拽用原生
 *    Pointer Events 实现——react-resizable 依赖 findDOMNode，React 19 已移除。
 *    仅对配置了数字 width 的列生效。
 *
 * 4. 测量时序防御：挂载瞬间的视口/布局未必就绪——嵌入 webview 初次展开时
 *    window.innerHeight 为 0 或反复跳变（实测 0→720 / 720→0→720），且此间
 *    不派发 resize、ResizeObserver 不调度，单次测量会把错误高度固化（分页
 *    悬空/被裁、刷新才恢复）。measure() 对未就绪视口直接跳过；重测由多路
 *    触发器驱动（RO 观察表格自身/父容器/PageContainer 内容容器、字体加载
 *    完成、window resize、挂载后稳定窗口轮询），且对相同结果短路——稳定后
 *    零渲染开销。细节见 measure 与挂载 effect 的注释。
 *
 * 排序直接在列上加 `sorter`（ProTable 原生能力），服务端排序配合
 * `src/abp/sorting.ts` 的 sorterToAbpSorting 使用。
 *
 * ===== 与官网对齐的使用约定（props 全部透传，跟随官网更新） =====
 *
 * - 批量选择/批量操作：按官网 rowSelection 用法直接传给本组件，服务端分页
 *   务必带 `preserveSelectedRowKeys: true`（否则翻页丢选中），配合 ProTable 的
 *   `tableAlertOptionRender` 渲染批量操作条；选择列宽已自动计入 scroll.x。
 *   （https://ant.design/components/table-cn#components-table-demo-row-selection-custom）
 *
 * - 受控排序/筛选：列上传 sortOrder / filteredValue 即受控模式——官方要求
 *   只支持单列排序生效、且必须指定 column.key（本组件的拖拽列宽同样以
 *   column.key 为准）。仅需"默认排序"时用非受控的 defaultSortOrder 即可。
 *
 * - 固定列：按官网要求 fixed 列必须配置数字 width，且建议留一列不设宽度
 *   做弹性布局（全列定宽 + 固定列在表格被拉宽时会出现列头错位/白隙）。
 *   本组件会在开发环境对违反这两条的情况给出 console.warn。
 */

/** 拖拽调宽的下限 */
const MIN_COLUMN_WIDTH = 60;

/** 页面内容区（PageContainer）底部的预留间距，默认对齐其 padding-bottom: 32 */
const DEFAULT_BOTTOM_GAP = 32;

/** 组件对外部布局层的唯一 DOM 契约：PageContainer 内容容器类名（自校正投影与
 * 位移重测都锚定它；pro-components 升级改名时此处是唯一改动点） */
const PAGE_CONTAINER_CONTENT_SELECTOR =
  '.ant-pro-page-container-children-container';

/** 挂载后稳定窗口轮询：间隔/拍数上限/连续稳定拍数（实测 webview 展开过渡 <2.5s，
 * 上限 30 拍为慢设备兜底；首拍建基线 + 连续 3 拍结果一致即提前停，桌面端
 * 约 400ms 收工——见挂载 effect 里的轮询实现） */
const SETTLE_INTERVAL_MS = 100;
const SETTLE_POLLS = 30;
const SETTLE_STABLE_TICKS = 3;

/** 自校正连续同向修正的熔断阈值：固定高度祖先（fillViewport 误用）下投影 delta
 * 不收敛，连续同向 ≥3 次即停用自校正，防止 RO 回调形成无限增长回路。
 * 只对"同向"计数是刻意取舍：ResizeObserver 规范本身带每帧迭代上限（超限丢弃
 * 并告警 "ResizeObserver loop completed with undelivered notifications"），
 * 交替方向的震荡（真实布局需负响应增益，仓库内不存在）最坏也是每帧有限次测量，
 * 由规范上限兜底；组件级若连方向翻转也计数，1:1 响应的首轮合法修正可能被误杀。 */
const SELF_CORRECT_MAX_STREAK = 3;

/** ProColumns 的稳定 key：优先 key，其次 dataIndex，兜底下标 */
const columnKeyOf = <T, V>(col: ProColumns<T, V>, index: number): string => {
  if (col.key != null) return String(col.key);
  const { dataIndex } = col;
  if (typeof dataIndex === 'string') return dataIndex;
  if (Array.isArray(dataIndex)) return dataIndex.join('.');
  return `index-${index}`;
};

/** 元素高度含上下 margin（与官方 demo 的 getHeight 一致） */
const getOuterHeight = (el: HTMLElement | null | undefined): number => {
  if (!el) return 0;
  const cs = getComputedStyle(el);
  return (
    el.getBoundingClientRect().height +
    (Number.parseFloat(cs.marginTop) || 0) +
    (Number.parseFloat(cs.marginBottom) || 0)
  );
};

const useStyles = createStyles(({ token, css }) => ({
  wrap: css`
    position: relative;
    width: 100%;
  `,
  /** 内层 antd Table 根节点：在纵向 flex 布局里占据剩余空间 */
  tableFlex: css`
    flex: 1;
    min-height: 0;
  `,
  resizableTh: css`
    position: relative;
  `,
  resizeHandle: css`
    position: absolute;
    top: 0;
    right: -3px;
    bottom: 0;
    width: 7px;
    z-index: 1;
    cursor: col-resize;
    touch-action: none;
    user-select: none;

    &::after {
      content: '';
      position: absolute;
      top: 50%;
      right: 2px;
      width: 3px;
      height: 60%;
      transform: translateY(-50%);
      border-radius: ${token.borderRadiusXS}px;
      background: transparent;
      transition: background-color ${token.motionDurationFast};
    }

    &:hover::after {
      background: ${token.colorPrimary};
    }
  `,
}));

type ResizableHeaderCellProps = React.ThHTMLAttributes<HTMLTableCellElement> & {
  /** 由 onHeaderCell 注入的当前列宽 */
  width?: number;
  /** 由 onHeaderCell 注入的宽度更新回调 */
  onResize?: (nextWidth: number) => void;
};

/** 可伸缩表头单元格（官网 resizable-column demo 的 ResizableTitle 替代实现） */
const ResizableHeaderCell: React.FC<ResizableHeaderCellProps> = (props) => {
  const { width, onResize, children, ...thProps } = props;
  const { styles } = useStyles();
  const dragRef = useRef<{ startX: number; startWidth: number } | null>(null);

  const handlePointerDown = (e: React.PointerEvent<HTMLSpanElement>) => {
    if (e.button !== 0 || width == null || !onResize) return;
    e.preventDefault();
    e.stopPropagation();
    e.currentTarget.setPointerCapture(e.pointerId);
    dragRef.current = { startX: e.clientX, startWidth: width };
  };
  const handlePointerMove = (e: React.PointerEvent<HTMLSpanElement>) => {
    const drag = dragRef.current;
    if (!drag || !onResize) return;
    e.stopPropagation();
    onResize(
      Math.max(MIN_COLUMN_WIDTH, drag.startWidth + e.clientX - drag.startX),
    );
  };
  const handlePointerUp = (e: React.PointerEvent<HTMLSpanElement>) => {
    if (!dragRef.current) return;
    dragRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
    }
  };

  if (width == null || !onResize) {
    return <th {...thProps}>{children}</th>;
  }

  return (
    <th
      {...thProps}
      className={`${styles.resizableTh} ${thProps.className ?? ''}`.trim()}
    >
      {children}
      <span
        className={styles.resizeHandle}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={handlePointerUp}
        onClick={(e) => e.stopPropagation()}
      />
    </th>
  );
};

export type AutoHeightProTableProps<
  DataType extends Record<string, any> = Record<string, any>,
  Params extends Record<string, any> = Record<string, any>,
  ValueType = 'text',
> = ProTableProps<DataType, Params, ValueType> & {
  /** 距视口底部的预留间距，默认 32（对齐 PageContainer 底部 padding-bottom） */
  bottomGap?: number;
  /** 是否允许拖拽表头调整列宽，默认 true；仅对配置了数字 width 的列生效 */
  resizable?: boolean;
  /** 是否撑满视口剩余高度（分页钉底、表体内部滚动），默认 true。
   * 页面是「目录树 + 列表」等多面板嵌套布局时关掉，保留拖拽列宽与防撑破。
   * 注意：撑满语义依赖文档流（内容容器底边随表格高度移动）；把默认模式的
   * 表格放进固定高度祖先（height 固定 / overflow 裁切的抽屉、面板）时底边
   * 不再联动，自校正无法收敛（组件会熔断并退回基础公式）——这类布局必须关掉。 */
  fillViewport?: boolean;
};

const AutoHeightProTable = <
  DataType extends Record<string, any>,
  Params extends Record<string, any> = Record<string, any>,
  ValueType = 'text',
>(
  props: AutoHeightProTableProps<DataType, Params, ValueType>,
) => {
  const {
    bottomGap = DEFAULT_BOTTOM_GAP,
    resizable = true,
    fillViewport = true,
    columns,
    style,
    tableClassName,
    cardProps,
    scroll,
    components,
    styles,
    ...rest
  } = props;
  const { styles: css } = useStyles();

  const wrapRef = useRef<HTMLDivElement>(null);
  const [wrapHeight, setWrapHeight] = useState<number>();
  const [scrollY, setScrollY] = useState<number>();
  const [sectionHeight, setSectionHeight] = useState<number>();
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>({});

  /** 自校正熔断状态：连续同向不收敛即停用（组件生命周期内不恢复，重挂载复位） */
  const selfCorrectRef = useRef({ dir: 0, streak: 0, disabled: false });

  /** 实测布局并刷新 wrapper 高度与 scroll.y（官方 auto-height demo 的 measure 思路）。
   * 表格可用高度从「父容器内容盒 − 兄弟节点（搜索/工具栏/提示条）」反推，
   * 不读表格自身高度——它受本组件设置的 section 高度影响，会形成反馈回路。
   * 返回本次应用/保持的 wrapHeight；四类早退（fillViewport 关 / 组件未挂载 /
   * 视口未就绪 / 无真实布局）返回 null，供稳定窗口轮询区分「已稳定」与
   * 「未就绪不消耗窗口」。 */
  const measure = useCallback((): number | null => {
    if (!fillViewport) return null;
    const wrap = wrapRef.current;
    const tableRoot = wrap?.querySelector<HTMLElement>('.ant-table-wrapper');
    if (!wrap || !tableRoot) return null;
    // 视口未就绪（嵌入 webview 初次展开时 innerHeight 为 0，布局随之失真）或
    // 页签隐藏（隐藏的 webview/标签页布局失真，且后台标签打开页面时挂载首测
    // 就处于此态）：不测量——此时按失真几何固化，错误的 wrapHeight 会让分页
    // 悬空/被裁。保持未测量状态（表格自然高度，分页仍在文档流中可见），由
    // effect 里的轮询重试等可见后就绪后补测（visibilitychange 边沿也补一拍）。
    if (window.innerHeight <= 0 || document.hidden) return null;
    // 无真实布局（单测 DOM 环境 / 隐藏页签，rect 全 0）时不启用内部滚动，
    // 保持普通表格
    const parent = tableRoot.parentElement;
    const parentHeight = parent?.getBoundingClientRect().height ?? 0;
    const wrapTop = wrap.getBoundingClientRect().top;
    if (parentHeight <= 0 || wrapTop <= 0) return null;

    let nextWrapHeight = Math.max(
      160,
      Math.floor(window.innerHeight - wrapTop - bottomGap),
    );
    // 自校正：PageContainer 内容容器的底边应恰好落在视口底。按「若 wrapper 高度
    // 调整为 nextWrapHeight，容器底边将到哪里」投影，把卡片标题/内边距等尾随
    // 空间一并吸收，不依赖对 gap 的静态估计。前提是表格为内容容器最后一个流内
    // 子元素（容器底随 wrap 高度 1:1 移动）；表格下方还有内容时投影口径失真。
    // 熔断：固定高度祖先（fillViewport 误用）下容器底不随 wrap 移动，每次迭代
    // 产生同向恒定 delta、永不收敛——连续同向 SELF_CORRECT_MAX_STREAK 次即停用，
    // 防止 RO 回调 → 修正 → 容器 resize → RO 的无限增长回路。
    const contentContainer = wrap.closest<HTMLElement>(
      PAGE_CONTAINER_CONTENT_SELECTOR,
    );
    const sc = selfCorrectRef.current;
    if (contentContainer && !sc.disabled) {
      const currentWrapHeight = wrap.getBoundingClientRect().height;
      const projectedBottom =
        contentContainer.getBoundingClientRect().bottom -
        (currentWrapHeight - nextWrapHeight);
      const delta = Math.round(projectedBottom - window.innerHeight);
      if (Math.abs(delta) > 1) {
        const dir = delta > 0 ? 1 : -1;
        if (sc.dir === dir) sc.streak += 1;
        else {
          sc.dir = dir;
          sc.streak = 1;
        }
        if (sc.streak >= SELF_CORRECT_MAX_STREAK) {
          sc.disabled = true;
          if (process.env.NODE_ENV === 'development') {
            console.warn(
              '[AutoHeightProTable] 自校正连续多次未收敛：当前布局下内容容器底边不随表格高度移动' +
                '（典型原因是组件被放进固定高度面板）。已停用自校正、退回基础高度公式；' +
                '嵌套面板布局请显式传 fillViewport={false}。',
            );
          }
        } else {
          nextWrapHeight = Math.max(160, nextWrapHeight - delta);
        }
      } else {
        sc.dir = 0;
        sc.streak = 0;
      }
    }
    setWrapHeight((prev) => (prev !== nextWrapHeight ? nextWrapHeight : prev));

    // 父容器（卡片 body / ghost 下的外层 div）的可用内容高度 = 自身高度 − 上下 padding
    const parentCs = getComputedStyle(parent as Element);
    const parentInner =
      parentHeight -
      (Number.parseFloat(parentCs.paddingTop) || 0) -
      (Number.parseFloat(parentCs.paddingBottom) || 0);
    // 表格的兄弟节点（工具栏、行选择提示条等）按含 margin 的高度扣除
    let othersHeight = 0;
    for (const child of (parent as HTMLElement).children) {
      if (child === tableRoot) continue;
      othersHeight += getOuterHeight(child as HTMLElement);
    }
    const slot = Math.max(120, Math.floor(parentInner - othersHeight));

    const headerHeight = getOuterHeight(
      tableRoot.querySelector<HTMLElement>('.ant-table-thead'),
    );
    const paginationHeight = getOuterHeight(
      tableRoot.querySelector<HTMLElement>('.ant-table-pagination'),
    );
    const nextScrollY = Math.max(
      0,
      Math.floor(slot - headerHeight - paginationHeight),
    );
    const nextSectionHeight = Math.max(0, Math.floor(slot - paginationHeight));
    setScrollY((prev) => (prev !== nextScrollY ? nextScrollY : prev));
    setSectionHeight((prev) =>
      prev !== nextSectionHeight ? nextSectionHeight : prev,
    );
    return nextWrapHeight;
  }, [bottomGap, fillViewport]);

  useEffect(() => {
    measure();
    // fillViewport=false 时 measure 恒早退，整套重测触发器（观察器/监听/轮询）
    // 都是无谓开销；ResizeObserver 缺失的古老环境同样按不支持处理（现代浏览器
    // 与测试环境均已具备）。
    if (!fillViewport || typeof ResizeObserver === 'undefined') return;
    const wrap = wrapRef.current;
    const tableRoot = wrap?.querySelector('.ant-table-wrapper');
    if (!tableRoot || !wrap) return;
    const observer = new ResizeObserver(() => measure());
    // 表格自身（内容/表头变化）与父容器（视口/搜索表单/页高变化）都要监听。
    // PageContainer 内容容器同样要监听：表格上方的兄弟内容变化（筛选卡换行、
    // 面包屑/页头异步出现）只平移 wrap 的位置，表格自身与父容器盒尺寸不变，
    // ResizeObserver 对"纯位移"不触发——不补听的话高度会固化在挂载瞬间的
    // 错误值（首次进入分页悬空/被裁、刷新才恢复）。观察它形成收敛迭代：
    // wrapHeight 更新 → 内容容器高度变 → 重测 → 结果不变即短路停住
    // （固定高度祖先等不收敛布局由 measure 内的自校正熔断兜底）。
    const contentContainer = wrap.closest<HTMLElement>(
      PAGE_CONTAINER_CONTENT_SELECTOR,
    );
    if (contentContainer) {
      observer.observe(contentContainer);
    } else if (process.env.NODE_ENV === 'development') {
      console.warn(
        `[AutoHeightProTable] 未找到 PageContainer 内容容器（${PAGE_CONTAINER_CONTENT_SELECTOR}）：` +
          '尾随空间自校正与上方内容位移重测不可用，表格高度退回基础公式。' +
          '本组件默认假定在 PageContainer 下使用；嵌套在固定高度面板时请传 fillViewport={false}。',
      );
    }
    observer.observe(tableRoot);
    if (tableRoot.parentElement) observer.observe(tableRoot.parentElement);
    // 字体加载完成后的 reflow 也只改内容盒位置/尺寸之外的部分布局，同理补测一次
    // （组件可能已卸载：measure 对 wrapRef 判空安全返回；此 promise 无法取消）
    document.fonts?.ready.then(() => measure());
    window.addEventListener('resize', measure);
    // webview 初次展开阶段视口尺寸反复跳变（实测 0→720 或 720→0→720），此间
    // RO 不调度、resize 不派发，任何单次测量都可能落在过渡值上——挂载后轮询
    // 测量兜底。三道退出：① 组件卸载（wrapRef 置空）；② 有效拍数耗尽（慢设备
    // 兜底上限）；③ 连续 SETTLE_STABLE_TICKS 拍结果一致即稳定收工（首拍建基线，
    // 桌面端约 400ms）。另：视口未就绪（innerHeight<=0）的拍直接跳过且不消耗
    // 窗口——webview 展开超过标称窗口也能等到就绪后测到稳定值（measure 对相同
    // 结果短路，稳定后零渲染开销）。
    // 隐藏页签同样跳过且不消耗窗口（Page Visibility 最佳实践："页面不可见时不做
    // 不必要的工作"——隐藏的 webview/标签页 innerHeight 常为 0，这正是"视口恒 0
    // 永轮询"的形态）；回到可见的边沿立即补测一拍，等不到下一个 100ms 节拍）。
    let polls = SETTLE_POLLS;
    let lastHeight: number | null = null;
    let stableStreak = 0;
    const settleTimer = setInterval(() => {
      if (wrapRef.current == null) {
        clearInterval(settleTimer);
        return;
      }
      if (window.innerHeight <= 0 || document.hidden) return;
      if (--polls <= 0) {
        clearInterval(settleTimer);
        return;
      }
      const next = measure();
      if (next != null && next === lastHeight) {
        stableStreak += 1;
        if (stableStreak >= SETTLE_STABLE_TICKS) {
          clearInterval(settleTimer);
        }
      } else {
        stableStreak = 0;
        lastHeight = next;
      }
    }, SETTLE_INTERVAL_MS);
    const onVisibilityChange = () => {
      if (!document.hidden) measure();
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      observer.disconnect();
      clearInterval(settleTimer);
      window.removeEventListener('resize', measure);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, [fillViewport, measure]);

  /** 用户拖拽后的列宽覆盖原始 width */
  const mergedColumns = useMemo(
    () =>
      columns?.map((col, index) => {
        const width = columnWidths[columnKeyOf(col, index)];
        return width == null ? col : { ...col, width };
      }),
    [columns, columnWidths],
  );

  /** 给配置了数字 width 的列接上 onHeaderCell（官网 resizable-column 的 mergedColumns 步骤）。
   * onHeaderCell 需要把自定义的 width/onResize 透传给 ResizableHeaderCell，超出 antd
   * 表头属性类型，故整体按 ProColumns 收窄。 */
  const finalColumns = useMemo(
    () =>
      mergedColumns?.map((col, index) => {
        if (!resizable || typeof col.width !== 'number') return col;
        const key = columnKeyOf(col, index);
        const width = col.width;
        return {
          ...col,
          onHeaderCell: (column: any) => ({
            width,
            onResize: (nextWidth: number) =>
              setColumnWidths((prev) => ({ ...prev, [key]: nextWidth })),
            ...(col.onHeaderCell?.(column) ?? {}),
          }),
        };
      }) as ProColumns<DataType, ValueType>[],
    [mergedColumns, resizable],
  );

  /** scroll.x = 各列宽度求和（含拖拽后的宽度），列总宽超出容器时只出横向滚动条。
   * 官网 rowSelection 默认选择列 32px、expandable 展开列默认 48px，
   * 这些"隐形列"不在 columns 里，需按官网默认宽度假补进总宽。 */
  const scrollX = useMemo(() => {
    let total = 0;
    columns?.forEach((col, index) => {
      const width = columnWidths[columnKeyOf(col, index)] ?? col.width;
      if (typeof width === 'number') total += width;
    });
    if (total === 0) return undefined;
    if (props.rowSelection) {
      total +=
        (typeof props.rowSelection === 'object'
          ? (props.rowSelection.columnWidth as number | undefined)
          : undefined) ?? 32;
    }
    if (props.expandable) {
      total +=
        (typeof props.expandable === 'object'
          ? (props.expandable.columnWidth as number | undefined)
          : undefined) ?? 48;
    }
    return total;
  }, [columns, columnWidths, props.rowSelection, props.expandable]);

  /** 官网「固定头和列」注意事项的开发期检查（仅 dev 告警，不改变行为）：
   * 1. fixed 列必须有数字 width，否则列头与内容错位；
   * 2. 存在 fixed 列时建议留一列不设宽度做弹性布局，
   *    全列定宽 + 固定列在表格被拉宽时会出现白色垂直空隙。 */
  useEffect(() => {
    if (process.env.NODE_ENV !== 'development' || !columns?.length) return;
    columns.forEach((col) => {
      if (col.fixed && typeof col.width !== 'number') {
        console.warn(
          `[AutoHeightProTable] 列「${columnKeyOf(col, 0)}」设置了 fixed 但没有数字 width，` +
            '会导致列头与内容不对齐（见官网「固定头和列」），请补 width。',
        );
      }
    });
    const fixedExists = columns.some((col) => col.fixed);
    const allHaveWidth = columns.every((col) => typeof col.width === 'number');
    if (fixedExists && allHaveWidth && columns.length > 0) {
      console.warn(
        '[AutoHeightProTable] 存在固定列且所有列都设置了 width：建议按官网「固定头和列」' +
          '的指引留一列不设宽度以适应弹性布局，避免表格被拉宽时出现错位或白隙。',
      );
    }
  }, [columns]);

  const mergedScroll = useMemo(
    () => ({
      ...scroll,
      ...(scrollX != null && scroll?.x == null ? { x: scrollX } : {}),
      ...(scrollY != null && scroll?.y == null ? { y: scrollY } : {}),
    }),
    [scroll, scrollX, scrollY],
  );

  const mergedComponents = useMemo(() => {
    if (!resizable) return components;
    return {
      ...components,
      header: {
        ...components?.header,
        cell: ResizableHeaderCell,
      },
    } as typeof components;
  }, [components, resizable]);

  // 与"props 全部透传"的文档承诺冲突的点：resizable 默认开启时会覆盖消费者的 header.cell。
  // 自带可伸缩列表头的方案（如"列宽拖拽 + localStorage 持久化"的自定义 hook）忘传
  // resizable={false} 时会被静默接管、持久化空转——开发期给出告警，避免无感的双实现陷阱。
  useEffect(() => {
    if (process.env.NODE_ENV !== 'development' || !resizable) return;
    if (components?.header?.cell) {
      console.warn(
        '[AutoHeightProTable] 检测到消费者自带 components.header.cell：resizable（默认开启）会用内置' +
          '可伸缩表头覆盖它。若你的表头单元格带拖拽调宽/持久化逻辑，请显式传 resizable={false}。',
      );
    }
  }, [resizable, components]);

  const mergedStyles = useMemo(() => {
    if (sectionHeight == null) return styles;
    // styles 允许函数形式（antd 语义化 API），这里只需按对象合并 section 高度
    const objectStyles = styles as
      | { section?: React.CSSProperties }
      | undefined;
    return {
      ...styles,
      section: { ...objectStyles?.section, height: sectionHeight },
    };
  }, [styles, sectionHeight]);

  const mergedCardProps: ProCardProps | false =
    cardProps === false
      ? cardProps
      : {
          ...cardProps,
          style: {
            flex: 1,
            minHeight: 0,
            display: 'flex',
            flexDirection: 'column',
            ...cardProps?.style,
          },
          styles: {
            ...cardProps?.styles,
            body: {
              flex: 1,
              minHeight: 0,
              display: 'flex',
              flexDirection: 'column',
              overflow: 'hidden',
              ...cardProps?.styles?.body,
            },
          },
        };

  return (
    <div
      ref={wrapRef}
      className={css.wrap}
      style={
        fillViewport ? { height: wrapHeight, overflow: 'hidden' } : undefined
      }
    >
      <ProTable<DataType, Params, ValueType>
        {...rest}
        columns={finalColumns}
        style={{
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          ...style,
        }}
        tableClassName={`${css.tableFlex} ${tableClassName ?? ''}`.trim()}
        cardProps={mergedCardProps}
        scroll={mergedScroll}
        components={mergedComponents}
        styles={mergedStyles}
      />
    </div>
  );
};

export default AutoHeightProTable;
