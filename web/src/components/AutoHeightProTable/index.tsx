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
 * 撑满视口剩余高度的 ProTable 封装，统一解决全站列表页三个问题：
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
   * 页面是「目录树 + 列表」等多面板嵌套布局时关掉，保留拖拽列宽与防撑破。 */
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

  /** 实测布局并刷新 wrapper 高度与 scroll.y（官方 auto-height demo 的 measure 思路）。
   * 表格可用高度从「父容器内容盒 − 兄弟节点（搜索/工具栏/提示条）」反推，
   * 不读表格自身高度——它受本组件设置的 section 高度影响，会形成反馈回路。 */
  const measure = useCallback(() => {
    if (!fillViewport) return;
    const wrap = wrapRef.current;
    const tableRoot = wrap?.querySelector<HTMLElement>('.ant-table-wrapper');
    if (!wrap || !tableRoot) return;
    // 无真实布局（单测 jsdom / 隐藏页签）时不启用内部滚动，保持普通表格
    const parent = tableRoot.parentElement;
    const parentHeight = parent?.getBoundingClientRect().height ?? 0;
    const wrapTop = wrap.getBoundingClientRect().top;
    if (parentHeight <= 0 || wrapTop <= 0) return;

    let nextWrapHeight = Math.max(
      160,
      Math.floor(window.innerHeight - wrapTop - bottomGap),
    );
    // 自校正：PageContainer 内容容器的底边应恰好落在视口底。按「若 wrapper 高度
    // 调整为 nextWrapHeight，容器底边将到哪里」投影，把卡片标题/内边距等尾随
    // 空间一并吸收，不依赖对 gap 的静态估计。
    const contentContainer = wrap.closest<HTMLElement>(
      '.ant-pro-page-container-children-container',
    );
    if (contentContainer) {
      const currentWrapHeight = wrap.getBoundingClientRect().height;
      const projectedBottom =
        contentContainer.getBoundingClientRect().bottom -
        (currentWrapHeight - nextWrapHeight);
      const delta = Math.round(projectedBottom - window.innerHeight);
      if (Math.abs(delta) > 1) {
        nextWrapHeight = Math.max(160, nextWrapHeight - delta);
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
  }, [bottomGap, fillViewport]);

  useEffect(() => {
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const wrap = wrapRef.current;
    const tableRoot = wrap?.querySelector('.ant-table-wrapper');
    if (!tableRoot || !wrap) return;
    const observer = new ResizeObserver(() => measure());
    // 表格自身（内容/表头变化）与父容器（视口/搜索表单/页高变化）都要监听
    observer.observe(tableRoot);
    if (tableRoot.parentElement) observer.observe(tableRoot.parentElement);
    window.addEventListener('resize', measure);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', measure);
    };
  }, [measure]);

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
