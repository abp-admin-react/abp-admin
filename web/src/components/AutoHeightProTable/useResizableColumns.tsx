import { Table } from 'antd';
import { createStyles } from 'antd-style';
import React, { useMemo, useRef, useState } from 'react';

/**
 * 表头可拖拽调宽（antd 官网 Table「可伸缩列 / resizable-column」demo 的全站共享实现）。
 *
 * 接线与官网一致：`components.header.cell` 换成可伸缩单元格 + 列上 `onHeaderCell`
 * 注入当前宽度与 onResize 回调。拖拽用原生 Pointer Events 实现——官网示例依赖的
 * react-resizable 内部走 findDOMNode，React 19 已移除该 API（见 AutoHeightProTable
 * 头注释）。仅对配置了**数字 width** 的列生效（官网同款约束）。
 *
 * 两个消费口：
 * - {@link AutoHeightProTable}（默认已启用，列配数字 width 即可拖）；
 * - 任意裸 antd `<Table>`：`const { columns, components, scrollX } = useResizableColumns(cols);`
 *   然后 `columns={columns} components={components} scroll={{ x: scrollX }}`。
 */

/** 拖拽调宽的下限 */
export const MIN_COLUMN_WIDTH = 60;

/** 列的稳定 key：优先 key，其次 dataIndex（antd/ProColumns 的 dataIndex 形状
 * 各版本不同，这里按 unknown 收进来只做运行时形状判断），兜底下标（拖拽宽度按 key 记忆） */
export const columnKeyOf = (
  col: { key?: React.Key; dataIndex?: unknown },
  index: number,
): string => {
  if (col.key != null) return String(col.key);
  const { dataIndex } = col;
  if (typeof dataIndex === 'string') return dataIndex;
  if (typeof dataIndex === 'number') return String(dataIndex);
  if (Array.isArray(dataIndex)) return dataIndex.map(String).join('.');
  return `index-${index}`;
};

const useStyles = createStyles(({ token, css }) => ({
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
export const ResizableHeaderCell: React.FC<ResizableHeaderCellProps> = (
  props,
) => {
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

/** 结构化列约束：antd ColumnsType / ProColumns 都满足（dataIndex 各自定义
 * 宽泛，不进约束——columnKeyOf 按 unknown 运行时判断） */
type ResizableColumn = {
  key?: React.Key;
  width?: number | string;
  onHeaderCell?: (...args: any[]) => any;
};

export type UseResizableColumnsReturn<C> = {
  /** 注入 onHeaderCell 后的列（拖拽后的宽度覆盖原始 width） */
  columns: C[];
  /** 传给 Table 的 components（header.cell 换成可伸缩单元格） */
  components: { header: { cell: typeof ResizableHeaderCell } } | undefined;
  /** 各列宽度求和（含拖拽后），供 scroll.x 使用；无数字宽度列时 undefined */
  scrollX: number | undefined;
  /** 当前拖拽宽度表（AutoHeightProTable 的 scroll.x 计算含隐形列补贴时需要） */
  columnWidths: Record<string, number>;
};

/**
 * 给一组列接上可伸缩表头。`resizable = false` 时原样返回（components 为 undefined）。
 */
export function useResizableColumns<C extends ResizableColumn>(
  columns: C[] | undefined,
  resizable = true,
): UseResizableColumnsReturn<C> {
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>({});

  const mergedColumns = useMemo(
    () =>
      columns?.map((col, index) => {
        const width = columnWidths[columnKeyOf(col, index)];
        return width == null ? col : { ...col, width };
      }),
    [columns, columnWidths],
  );

  /** 给配置了数字 width 的列接上 onHeaderCell（官网 resizable-column 的
   * mergedColumns 步骤）。onHeaderCell 需要把自定义的 width/onResize 透传给
   * ResizableHeaderCell，超出 antd 表头属性类型，故透传消费者的同名字段后收窄。 */
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
      }) as C[],
    [mergedColumns, resizable],
  );

  const components = useMemo(
    () =>
      resizable
        ? ({ header: { cell: ResizableHeaderCell } } as const)
        : undefined,
    [resizable],
  );

  const scrollX = useMemo(() => {
    let total = 0;
    columns?.forEach((col, index) => {
      const width = columnWidths[columnKeyOf(col, index)] ?? col.width;
      if (typeof width === 'number') total += width;
    });
    return total > 0 ? total : undefined;
  }, [columns, columnWidths]);

  return { columns: finalColumns ?? [], components, scrollX, columnWidths };
}

/** antd Table 的全量 props（列/组件/滚动由本包装接管，其余透传） */
type ResizableTableProps<RecordType extends object> = Omit<
  import('antd').TableProps<RecordType>,
  'columns' | 'components' | 'scroll'
> & {
  columns?: import('antd').TableProps<RecordType>['columns'];
  components?: import('antd').TableProps<RecordType>['components'];
  scroll?: import('antd').TableProps<RecordType>['scroll'];
  /** 关闭表头拖拽（默认开启） */
  resizable?: boolean;
};

/**
 * 裸 antd Table 的「可伸缩列」一键替换：`<Table` → `<ResizableTable`，
 * 其余 props 原样。内部走 {@link useResizableColumns}（官网 resizable-column 接线）。
 * 适合 columns 内联在 JSX 里的页面——不必把列定义提出来。AutoHeightProTable
 * 页面无需使用（已内置同一实现）。
 */
export function ResizableTable<RecordType extends object = any>(
  props: ResizableTableProps<RecordType>,
) {
  const {
    columns,
    components,
    scroll,
    resizable = true,
    ...rest
  } = props;
  const { columns: resizableColumns, components: resizableComponents, scrollX } =
    useResizableColumns(columns as ResizableColumn[] | undefined, resizable);
  return (
    <Table<RecordType>
      {...(rest as import('antd').TableProps<RecordType>)}
      columns={resizableColumns as import('antd').TableProps<RecordType>['columns']}
      components={{ ...components, ...resizableComponents }}
      scroll={{ ...(scrollX != null && scroll?.x == null ? { x: scrollX } : {}), ...scroll }}
    />
  );
}
