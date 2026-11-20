import { AG_GRID_LOCALE_ZH_CN } from '@ag-grid-community/locale';
import {
  CellStyleModule,
  ColumnApiModule,
  CustomFilterModule,
  DateFilterModule,
  GridStateModule,
  InfiniteRowModelModule,
  LocaleModule,
  NumberFilterModule,
  PaginationModule,
  RenderApiModule,
  TextFilterModule,
  TooltipModule,
  themeQuartz,
  type ColDef,
  type GridApi,
  type GridOptions,
  type IDatasource,
  type IGetRowsParams,
  type IOverlayParams,
} from 'ag-grid-community';
import type {
  AgGridReactProps,
  CustomFloatingFilterProps,
} from 'ag-grid-react';
import { Button, theme } from 'antd';
import { useMemo } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import type { PagedResult } from '@/abp/identity';
import { toGridQuery } from './query';
import { type ColumnKind, inputHint, serverHint } from './validation';

type BooleanLabels = { true: string; false: string };
export type RemoteColumn<T> = ColDef<T> & {
  kind?: ColumnKind;
  booleanLabels?: BooleanLabels;
};
type QueryPage<T> = (
  input: GridQuery,
  signal: AbortSignal,
) => Promise<PagedResult<T>>;

const modules = [
  InfiniteRowModelModule,
  PaginationModule,
  TextFilterModule,
  NumberFilterModule,
  DateFilterModule,
  CustomFilterModule,
  CellStyleModule,
  LocaleModule,
  GridStateModule,
  ColumnApiModule,
  RenderApiModule,
  TooltipModule,
];
const textOps = [
  'contains',
  'equals',
  'startsWith',
  'endsWith',
  'notContains',
  'notEqual',
];
const comparisonOps = [
  'equals',
  'greaterThan',
  'greaterThanOrEqual',
  'lessThan',
  'lessThanOrEqual',
  'inRange',
  'notEqual',
];
function BooleanFilter({
  model,
  onModelChange,
  labels,
  column,
}: CustomFloatingFilterProps & { labels: BooleanLabels }) {
  return (
    <select
      aria-label={`筛选${column.getColDef().headerName ?? ''}`}
      style={{
        width: '100%',
        color: 'inherit',
        background: 'var(--ag-background-color)',
        border: '1px solid var(--ag-border-color)',
        borderRadius: 4,
      }}
      value={model?.type ?? ''}
      onChange={({ target }) =>
        onModelChange(
          target.value ? { filterType: 'text', type: target.value } : null,
        )
      }
    >
      <option value="">全部</option>
      <option value="isTrue">{labels.true}</option>
      <option value="isFalse">{labels.false}</option>
    </select>
  );
}

function buildColumn<T>({
  kind = 'text',
  booleanLabels = { true: '是', false: '否' },
  ...column
}: RemoteColumn<T>): ColDef<T> {
  if (kind === 'display')
    return { ...column, sortable: false, filter: false, floatingFilter: false };
  const numeric = kind === 'number' || kind === 'decimal';
  const filter =
    kind === 'date'
      ? 'agDateColumnFilter'
      : kind === 'number'
        ? 'agNumberColumnFilter'
        : 'agTextColumnFilter';
  // 十进制金额使用文本输入保存尾数，由后端 decimal 执行数字比较。
  const filterOptions =
    numeric || kind === 'date'
      ? comparisonOps
      : kind === 'boolean'
        ? [
            'empty',
            {
              displayKey: 'isTrue',
              displayName: booleanLabels.true,
              numberOfInputs: 0,
              predicate: (_inputs: unknown[], value: unknown) =>
                String(value) === 'true',
            },
            {
              displayKey: 'isFalse',
              displayName: booleanLabels.false,
              numberOfInputs: 0,
              predicate: (_inputs: unknown[], value: unknown) =>
                String(value) === 'false',
            },
          ]
        : kind === 'identifier' || kind === 'uuid'
          ? ['equals', 'notEqual']
          : textOps;
  return {
    cellDataType: false,
    tooltipField: column.field,
    filter,
    floatingFilter: true,
    filterParams: {
      filterOptions,
      maxNumConditions: kind === 'boolean' ? 1 : 2,
      debounceMs: 400,
      trimInput: true,
      inRangeInclusive: true,
      browserDatePicker: true,
    },
    ...(kind === 'boolean'
      ? {
          floatingFilterComponent: BooleanFilter,
          floatingFilterComponentParams: { labels: booleanLabels },
        }
      : {}),
    ...(numeric ? { type: 'numericColumn' } : {}),
    ...column,
  };
}

function RetryOverlay({ api }: IOverlayParams) {
  return (
    <div role="status" style={{ pointerEvents: 'auto' }}>
      数据未能加载
      <Button type="link" onClick={() => api.purgeInfiniteCache()}>
        重试
      </Button>
    </div>
  );
}

function FilterHintOverlay({ message }: { message: string }) {
  return <div role="status">{message}</div>;
}

function showFilterHint(api: GridApi, message: string) {
  api.updateGridOptions({
    activeOverlay: FilterHintOverlay,
    activeOverlayParams: { message },
  });
}

function makeDatasource<T>(
  query: QueryPage<T>,
  columns: RemoteColumn<T>[],
): IDatasource {
  let active = new AbortController();
  let currentModel = '';
  return {
    async getRows(params: IGetRowsParams<T>) {
      const model = JSON.stringify([params.sortModel, params.filterModel]);
      // 同一查询可并发加载多个块；条件切换或数据源销毁时，旧响应不得覆盖当前状态。
      if (model !== currentModel || active.signal.aborted) {
        active.abort();
        active = new AbortController();
        currentModel = model;
      }
      const { signal } = active;
      if (signal.aborted || params.api.isDestroyed()) return;
      params.api.setGridOption('activeOverlay', undefined);
      try {
        const input = toGridQuery(params);
        const help = inputHint(input, columns);
        if (help) {
          params.failCallback();
          return showFilterHint(params.api, help);
        }
        const { items, totalCount } = await query(input, signal);
        if (params.api.isDestroyed()) return;
        if (signal.aborted) return params.failCallback();
        params.successCallback(items, totalCount);
        if (totalCount === 0)
          params.api.setGridOption('activeOverlay', 'agNoRowsOverlay');
      } catch (error) {
        if (params.api.isDestroyed()) return;
        params.failCallback();
        if (signal.aborted) return;
        const help = serverHint(error, columns);
        if (help) return showFilterHint(params.api, help);
        params.api.setGridOption('activeOverlay', RetryOverlay);
      }
    },
    destroy() {
      active.abort();
    },
  };
}

interface RemoteGridOptions<T> {
  columnDefs: RemoteColumn<T>[];
  query: QueryPage<T>;
  getRowId: NonNullable<GridOptions<T>['getRowId']>;
  initialState?: GridOptions<T>['initialState'];
  pageSize?: number;
  ariaLabel?: string;
}

const firstPage = ({ api }: { api: GridApi }) => api.paginationGoToFirstPage();
const defaultColDef = { minWidth: 100, sortable: true, resizable: true };
const containerStyle = {
  width: '100%',
  minWidth: 0,
  height: 'max(310px, calc(100dvh - 260px))',
};

/** 复用查询与显示配置，业务页面直接渲染 AgGridReact 并可覆盖官方属性。 */
export function useRemoteGrid<T>({
  columnDefs,
  query,
  getRowId,
  initialState,
  pageSize = 20,
  ariaLabel,
}: RemoteGridOptions<T>): AgGridReactProps<T> {
  const { token } = theme.useToken();
  const gridTheme = useMemo(
    () =>
      themeQuartz.withParams({
        fontFamily: token.fontFamily,
        fontSize: token.fontSize,
        backgroundColor: token.colorBgContainer,
        foregroundColor: token.colorText,
        headerBackgroundColor: token.colorFillQuaternary,
        accentColor: token.colorPrimary,
        borderColor: token.colorBorderSecondary,
        wrapperBorderRadius: token.borderRadiusLG,
        headerHeight: 42,
        rowHeight: 42,
      }),
    [token],
  );
  const columns = useMemo(() => columnDefs.map(buildColumn<T>), [columnDefs]);
  const datasource = useMemo(
    () => makeDatasource(query, columnDefs),
    [query, columnDefs],
  );
  return {
    columnDefs: columns,
    datasource,
    modules,
    getRowId,
    initialState,
    defaultColDef,
    theme: gridTheme,
    containerStyle,
    localeText: AG_GRID_LOCALE_ZH_CN,
    loadThemeGoogleFonts: false,
    rowModelType: 'infinite',
    cacheBlockSize: pageSize,
    maxBlocksInCache: 5,
    pagination: true,
    paginationPageSize: pageSize,
    paginationPageSizeSelector: [10, 20, 50, 100],
    tooltipShowMode: 'whenTruncated',
    tooltipShowDelay: 400,
    onSortChanged: firstPage,
    onFilterChanged: firstPage,
    onGridReady: ({ api }) => {
      if (ariaLabel) api.setGridAriaProperty('label', ariaLabel);
    },
  };
}
