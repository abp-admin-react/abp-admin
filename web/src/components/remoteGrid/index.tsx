import { AG_GRID_LOCALE_ZH_CN } from '@ag-grid-community/locale';
import {
  CellStyleModule,
  ClientSideRowModelApiModule,
  ClientSideRowModelModule,
  type ColDef,
  ColumnApiModule,
  CustomFilterModule,
  DateFilterModule,
  ExternalFilterModule,
  type GridApi,
  type GridOptions,
  GridStateModule,
  type IDatasource,
  type IGetRowsParams,
  InfiniteRowModelModule,
  type IOverlayParams,
  LocaleModule,
  NumberFilterModule,
  PaginationModule,
  RenderApiModule,
  RowApiModule,
  RowAutoHeightModule,
  RowSelectionModule,
  RowStyleModule,
  TextFilterModule,
  TooltipModule,
  themeQuartz,
} from 'ag-grid-community';
import type {
  AgGridReactProps,
  CustomFloatingFilterProps,
} from 'ag-grid-react';
import { Button, Input, theme } from 'antd';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import type { PagedResult } from '@/abp/identity';
import { type KeywordField, localColumn, matchesKeyword } from './localValues';
import { currentGridQuery, toGridQuery } from './query';
import { type ColumnKind, inputHint, serverHint } from './validation';

export { toGridColumns } from './columns';
export type { ColumnKind as RemoteColumnKind } from './validation';

type BooleanLabels = { true: string; false: string };
export type RemoteColumn<T> = ColDef<T> & {
  kind?: ColumnKind;
  booleanLabels?: BooleanLabels;
  choices?: Readonly<Record<string, string>>;
};
type QueryPage<T> = (
  input: GridQuery,
  signal: AbortSignal,
) => Promise<PagedResult<T>>;

const modules = [
  RowSelectionModule,
  RowApiModule,
  RowStyleModule,
  RowAutoHeightModule,
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
const remoteModules = [...modules, InfiniteRowModelModule];
const localModules = [
  ...modules,
  ClientSideRowModelModule,
  ClientSideRowModelApiModule,
  ExternalFilterModule,
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
function ChoiceFilter({
  model,
  onModelChange,
  options,
  column,
}: CustomFloatingFilterProps & { options: Record<string, string> }) {
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
      {Object.entries(options).map(([key, label]) => (
        <option key={key} value={key}>
          {label}
        </option>
      ))}
    </select>
  );
}

function buildColumn<T>({
  kind = 'text',
  booleanLabels = { true: '是', false: '否' },
  choices,
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
  const options = choices
    ? Object.fromEntries(
        Object.entries(choices).map(([value, label]) => [
          `choice:${value}`,
          label,
        ]),
      )
    : kind === 'boolean'
      ? { isTrue: booleanLabels.true, isFalse: booleanLabels.false }
      : undefined;
  const filterOptions =
    numeric || kind === 'date'
      ? comparisonOps
      : options
        ? [
            'empty',
            ...Object.entries(options).map(([key, label]) => ({
              displayKey: key,
              displayName: label,
              numberOfInputs: 0,
              predicate: (_inputs: unknown[], value: unknown) =>
                choices
                  ? key === `choice:${value}`
                  : String(value) === String(key === 'isTrue'),
            })),
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
      maxNumConditions: options ? 1 : 2,
      debounceMs: 400,
      trimInput: true,
      inRangeInclusive: true,
      browserDatePicker: true,
    },
    ...(options
      ? {
          floatingFilterComponent: ChoiceFilter,
          floatingFilterComponentParams: { options },
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
  columns: { current: RemoteColumn<T>[] },
  keyword: string,
): IDatasource {
  let active = new AbortController();
  let currentModel = '';
  const datasource: IDatasource = {
    async getRows(params: IGetRowsParams<T>) {
      if (params.api.isDestroyed()) return;
      // AG 可能在替换后才调用旧块的 getRows；StrictMode 重新挂载时也可能再次使用同一数据源。
      if (
        active.signal.aborted &&
        params.api.getGridOption('datasource') !== datasource
      ) {
        return params.failCallback();
      }
      const model = JSON.stringify([params.sortModel, params.filterModel]);
      // 同一查询可并发加载多个块；条件切换或数据源销毁时，旧响应不得覆盖当前状态。
      if (model !== currentModel || active.signal.aborted) {
        active.abort();
        active = new AbortController();
        currentModel = model;
      }
      const { signal } = active;
      params.api.setGridOption('activeOverlay', undefined);
      try {
        const input = toGridQuery(params, keyword);
        const help = inputHint(input, columns.current);
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
        const help = serverHint(error, columns.current);
        if (help) return showFilterHint(params.api, help);
        params.api.setGridOption('activeOverlay', RetryOverlay);
      }
    },
    destroy() {
      active.abort();
    },
  };
  return datasource;
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

function useGridTheme() {
  const { token } = theme.useToken();
  return useMemo(
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
}

/** 复用查询与显示配置，业务页面直接渲染 AgGridReact 并可覆盖官方属性。 */
export function useRemoteGrid<T>(
  options: RemoteGridOptions<T>,
  keyword = '',
): AgGridReactProps<T> {
  const {
    columnDefs,
    query,
    getRowId,
    initialState,
    pageSize = 20,
    ariaLabel,
  } = options;
  const gridTheme = useGridTheme();
  const latestColumns = useRef(columnDefs);
  latestColumns.current = columnDefs;
  const columns = useMemo(() => columnDefs.map(buildColumn<T>), [columnDefs]);
  const datasource = useMemo(
    () => makeDatasource(query, latestColumns, keyword),
    [query, keyword],
  );
  return {
    columnDefs: columns,
    datasource,
    modules: remoteModules,
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

interface KeywordSearchOptions {
  label?: string;
  placeholder?: string;
  enterButton?: string;
}

function useGridSearch<T>(
  ariaLabel: string | undefined,
  search: KeywordSearchOptions | undefined,
  initialState?: GridOptions<T>['initialState'],
) {
  const [draft, setDraft] = useState('');
  const [keyword, setKeyword] = useState('');
  const [hasColumnFilters, setHasColumnFilters] = useState(
    () => Object.keys(initialState?.filter?.filterModel ?? {}).length > 0,
  );
  const api = useRef<GridApi<T> | null>(null);
  const events: Pick<AgGridReactProps<T>, 'onGridReady' | 'onFilterChanged'> = {
    onGridReady({ api: grid }) {
      api.current = grid;
      if (ariaLabel) grid.setGridAriaProperty('label', ariaLabel);
    },
    onFilterChanged(event) {
      setHasColumnFilters(Object.keys(event.api.getFilterModel()).length > 0);
      firstPage(event);
    },
  };
  function submit(text: string) {
    const next = text.trim();
    setDraft(next);
    if (next !== keyword) {
      api.current?.paginationGoToFirstPage();
      setKeyword(next);
    }
  }
  const searchBox = (
    <Input.Search
      aria-label={search?.label ?? `搜索${ariaLabel ?? '表格'}`}
      placeholder={search?.placeholder ?? '输入关键词搜索'}
      style={{ width: 'min(320px, 100%)' }}
      allowClear
      maxLength={512}
      enterKeyHint="search"
      autoCapitalize="none"
      autoCorrect="off"
      spellCheck={false}
      enterButton={search?.enterButton}
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      onSearch={(text, event) => {
        const key = event?.nativeEvent;
        if (
          key instanceof KeyboardEvent &&
          (key.isComposing || key.keyCode === 229)
        )
          return;
        submit(text);
      }}
    />
  );
  return {
    events,
    api,
    keyword,
    searchBox,
    hasFilters: hasColumnFilters || Boolean(keyword),
    clearFilters() {
      submit('');
      if (!api.current?.isDestroyed()) api.current?.setFilterModel(null);
    },
  };
}

/** 表格只提交关键词，参与匹配的字段和行级权限由服务端资源声明。 */
export function useSearchableRemoteGrid<T>({
  search,
  ...options
}: RemoteGridOptions<T> & { search?: KeywordSearchOptions }) {
  const state = useGridSearch<T>(
    options.ariaLabel,
    search,
    options.initialState,
  );
  const grid = useRemoteGrid(options, state.keyword);
  return {
    gridProps: { ...grid, ...state.events },
    searchBox: state.searchBox,
    clearFilters: state.clearFilters,
    hasFilters: state.hasFilters,
    refresh() {
      if (!state.api.current?.isDestroyed())
        state.api.current?.purgeInfiniteCache();
    },
    getQuery() {
      const api = state.api.current;
      return api && !api.isDestroyed()
        ? currentGridQuery(api, options.pageSize ?? 20, state.keyword)
        : undefined;
    },
  };
}

interface LocalGridOptions<T> extends Omit<RemoteGridOptions<T>, 'query'> {
  rowData: T[] | readonly T[] | null | undefined;
  keywordFields: readonly KeywordField<T>[];
  pagination?: boolean;
  search?: KeywordSearchOptions;
}

/** 适用于一次返回完整集合的配置和明细。外部关键词与 AG 列条件同时生效，排序与选择仍由 AG 维护。 */
export function useSearchableLocalGrid<T>(options: LocalGridOptions<T>) {
  const {
    columnDefs,
    rowData,
    getRowId,
    initialState,
    ariaLabel,
    pageSize = 20,
    pagination = true,
  } = options;
  const state = useGridSearch<T>(ariaLabel, options.search, initialState);
  const fields = useRef(options.keywordFields);
  fields.current = options.keywordFields;
  const keyword = useRef(state.keyword);
  keyword.current = state.keyword;
  const { api } = state;
  useEffect(() => {
    if (api.current && !api.current.isDestroyed())
      api.current.onFilterChanged();
  }, [api, state.keyword]);
  const theme = useGridTheme();
  const columns = useMemo(
    () => columnDefs.map((col) => localColumn(col, buildColumn(col))),
    [columnDefs],
  );
  const gridProps: AgGridReactProps<T> = {
    columnDefs: columns,
    rowData: rowData as T[] | undefined,
    getRowId,
    initialState,
    defaultColDef,
    theme,
    modules: localModules,
    loadThemeGoogleFonts: false,
    localeText: AG_GRID_LOCALE_ZH_CN,
    containerStyle: pagination
      ? containerStyle
      : {
          width: '100%',
          minWidth: 0,
          height: Math.min(460, 120 + Math.max(rowData?.length ?? 0, 1) * 42),
        },
    pagination,
    paginationPageSize: pageSize,
    paginationPageSizeSelector: [10, 20, 50, 100],
    tooltipShowMode: 'whenTruncated',
    tooltipShowDelay: 400,
    isExternalFilterPresent: () => Boolean(keyword.current),
    doesExternalFilterPass: (node) =>
      node.data != null &&
      matchesKeyword(node.data, fields.current, keyword.current),
    onSortChanged: firstPage,
    ...state.events,
  };
  return {
    gridProps,
    searchBox: state.searchBox,
    hasFilters: state.hasFilters,
    clearFilters: state.clearFilters,
    keyword: state.keyword,
  };
}
