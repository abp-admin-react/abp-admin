import type { ProColumns } from '@ant-design/pro-components';
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import { Space, type TableColumnsType, Typography } from 'antd';
import type { ReactNode } from 'react';
import type { RemoteColumn } from './index';
import type { ColumnKind } from './validation';

interface ColumnSettings {
  kinds?: Readonly<Record<string, ColumnKind>>;
  pro?: boolean;
}

// 此适配仅复用存量表的列展示。新表直接定义 RemoteColumn。
interface ExistingColumn<T> {
  dataIndex?: string | number | readonly (string | number)[];
  key?: string | number;
  title?: ReactNode;
  width?: string | number;
  fixed?: boolean | 'left' | 'right' | 'start' | 'end';
  hideInTable?: boolean;
  hidden?: boolean;
  copyable?: boolean;
  valueType?: string;
  valueEnum?:
    | Record<string, string | { text?: ReactNode }>
    | Map<unknown, string | { text?: ReactNode }>;
  renderText?: (value: unknown, record: T, index: number) => ReactNode;
  render?: (value: unknown, record: T, index: number) => ReactNode;
}
/** 保持渲染器类型稳定。工具栏或草稿更新时，同一业务行内的输入和操作按钮可保留焦点。 */
function ExistingCell<T>(
  params: ICellRendererParams<T> & {
    col: ExistingColumn<T>;
    choices: Record<string, string>;
    pro?: boolean;
  },
) {
  const { col, choices, pro } = params;
  if (params.data == null) return null;
  const index = params.node.rowIndex ?? 0;
  const raw = params.value;
  let display: ReactNode = col.renderText
    ? col.renderText(raw, params.data, index)
    : (choices[String(raw)] ?? raw ?? '—');
  if (
    !col.renderText &&
    raw &&
    (col.valueType === 'dateTime' || col.valueType === 'date')
  ) {
    const date = new Date(raw);
    if (Number.isFinite(date.getTime()))
      display =
        col.valueType === 'dateTime'
          ? date.toLocaleString('zh-CN', { hour12: false })
          : date.toLocaleDateString('zh-CN');
  }
  if (col.render) display = col.render(pro ? display : raw, params.data, index);
  if (Array.isArray(display)) display = <Space>{display}</Space>;
  return col.copyable && raw != null ? (
    <Typography.Text copyable={{ text: String(raw) }}>
      {display}
    </Typography.Text>
  ) : (
    display
  );
}
export function toGridColumns<T>(
  columns: readonly ProColumns<T>[],
  options: ColumnSettings & { pro: true },
): RemoteColumn<T>[];
export function toGridColumns<T>(
  columns: Readonly<TableColumnsType<T>>,
  options?: ColumnSettings,
): RemoteColumn<T>[];
export function toGridColumns<T>(
  columns: readonly unknown[],
  options: ColumnSettings = {},
): RemoteColumn<T>[] {
  return (columns as ExistingColumn<T>[])
    .filter((col) => !col.hideInTable && !col.hidden)
    .map((col, index) => {
      const path = Array.isArray(col.dataIndex)
        ? col.dataIndex.join('.')
        : col.dataIndex?.toString();
      const id = String(col.key ?? path ?? `display-${index}`);
      const header = typeof col.title === 'string' ? col.title : (path ?? '');
      const mapping =
        col.valueEnum instanceof Map
          ? Array.from(col.valueEnum.entries())
          : Object.entries(col.valueEnum ?? {});
      const choices = Object.fromEntries(
        mapping.map(([key, value]) => [
          String(key),
          typeof value === 'object' && value !== null && 'text' in value
            ? String(value.text)
            : String(value),
        ]),
      );
      const kind =
        options.kinds?.[id] ??
        options.kinds?.[path ?? ''] ??
        (!path || col.valueType === 'option'
          ? 'display'
          : col.valueType === 'dateTime' || col.valueType === 'date'
            ? 'date'
            : 'text');
      const result: RemoteColumn<T> = {
        colId: id,
        headerName: header,
        field: path as ColDef<T>['field'],
        kind,
        ...(col.fixed
          ? {
              initialPinned: ['right', 'end'].includes(String(col.fixed))
                ? 'right'
                : 'left',
            }
          : {}),
        ...(typeof col.width === 'number'
          ? { initialWidth: col.width, minWidth: Math.min(100, col.width) }
          : { minWidth: 140, flex: 1 }),
        ...(mapping.length ? { choices } : {}),
      };
      if (
        col.render ||
        col.renderText ||
        col.copyable ||
        mapping.length ||
        col.valueType === 'dateTime' ||
        col.valueType === 'date'
      ) {
        result.cellRenderer = ExistingCell;
        result.cellRendererParams = { col, choices, pro: options.pro };
      }
      return result;
    });
}
