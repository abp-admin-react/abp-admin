import type {
  ColDef,
  GridApi,
  ICellRendererParams,
  IGetRowsParams,
  ValueFormatterParams,
  ValueGetterParams,
} from 'ag-grid-community';
import type { AgGridReactProps } from 'ag-grid-react';
import {
  type ComponentType,
  useEffect,
  useReducer,
  useRef,
  useState,
} from 'react';

/** jsdom 没有布局。通过 AG 的公开数据源接口加载首块，并执行页面提供的实际单元格渲染器。 */
export function AgGridReact<T extends Record<string, unknown>>(
  props: AgGridReactProps<T>,
) {
  const [rows, setRows] = useState<T[]>([]);
  const [revision, refresh] = useReducer((v) => v + 1, 0);
  const [filterModel, setFilterModel] = useState<Record<string, unknown>>(
    () => props.initialState?.filter?.filterModel ?? {},
  );
  const current = useRef({ props, filterModel });
  current.current = { props, filterModel };
  const [api] = useState(
    () =>
      ({
        isDestroyed: () => false,
        setGridOption: () => {},
        updateGridOptions: () => {},
        setGridAriaProperty: () => {},
        paginationGoToFirstPage: () => {},
        onFilterChanged: () => {},
        getFilterModel: () => current.current.filterModel,
        setFilterModel: (model: Record<string, unknown> | null) =>
          setFilterModel(model ?? {}),
        getGridOption: (key: keyof AgGridReactProps<T>) =>
          current.current.props[key],
        getColumnState: () =>
          (current.current.props.initialState?.sort?.sortModel ?? []).map(
            (c, sortIndex) => ({ ...c, sortIndex }),
          ),
        purgeInfiniteCache: refresh,
        refreshInfiniteCache: refresh,
      }) as unknown as GridApi<T>,
  );
  useEffect(() => {
    props.onGridReady?.({ api } as Parameters<
      NonNullable<typeof props.onGridReady>
    >[0]);
  }, [api]);
  useEffect(() => {
    let current = true;
    props.datasource?.getRows({
      api,
      startRow: 0,
      endRow: props.cacheBlockSize ?? 20,
      filterModel,
      context: props.context,
      sortModel: props.initialState?.sort?.sortModel ?? [],
      successCallback: (items: T[]) => {
        if (current) setRows(items);
      },
      failCallback: () => {},
    } as IGetRowsParams<T>);
    return () => {
      current = false;
    };
  }, [api, props.datasource, props.cacheBlockSize, revision, filterModel]);
  const columns = (props.columnDefs ?? []) as ColDef<T>[];
  return (
    <table>
      <thead>
        <tr>
          {columns.map((c) => (
            <th key={c.colId ?? c.field} aria-label={c.headerName}>
              {c.headerName}
              {c.floatingFilterComponentParams?.options && (
                <select
                  aria-label={`筛选${c.headerName}`}
                  onChange={(e) =>
                    setFilterModel({
                      [c.colId ?? String(c.field)]: {
                        filterType: 'text',
                        type: e.target.value,
                      },
                    })
                  }
                >
                  {Object.entries(c.floatingFilterComponentParams.options).map(
                    ([key, label]) => (
                      <option key={key} value={key}>
                        {String(label)}
                      </option>
                    ),
                  )}
                </select>
              )}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {(props.rowData ?? rows).map((data, index) => (
          <tr
            key={String(data.id ?? data._key ?? index)}
            onClick={() =>
              props.onRowClicked?.({ data } as Parameters<
                NonNullable<typeof props.onRowClicked>
              >[0])
            }
          >
            {columns.map((c) => {
              const value =
                typeof c.valueGetter === 'function'
                  ? c.valueGetter({ data, colDef: c } as ValueGetterParams<T>)
                  : data[String(c.field)];
              const display =
                typeof c.valueFormatter === 'function'
                  ? c.valueFormatter({
                      value,
                      data,
                      colDef: c,
                    } as ValueFormatterParams<T>)
                  : String(value ?? '');
              const Cell = c.cellRenderer as
                | ComponentType<ICellRendererParams<T>>
                | undefined;
              return (
                <td key={c.colId ?? c.field}>
                  {Cell ? (
                    <Cell
                      {...({
                        ...c.cellRendererParams,
                        value,
                        valueFormatted: display,
                        data,
                        node: { rowIndex: index },
                        api,
                      } as ICellRendererParams<T>)}
                    />
                  ) : (
                    display
                  )}
                </td>
              );
            })}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
