import type {
  ColDef,
  GridApi,
  ICellRendererParams,
  IGetRowsParams,
} from 'ag-grid-community';
import type { AgGridReactProps } from 'ag-grid-react';
import type { ComponentType } from 'react';

/** 无需模拟布局即可用 AG 的真实请求契约验证页偏移、列条件和深链范围。 */
export function fetchGridBlock<T>(
  grid: AgGridReactProps<T>,
  overrides: Partial<IGetRowsParams<T>> = {},
) {
  if (!grid.datasource) throw new Error('缺少远程数据源');
  const source = grid.datasource;
  return new Promise<{ items: T[]; totalCount: number | undefined }>(
    (resolve, reject) => {
      source.getRows({
        startRow: 0,
        endRow: grid.cacheBlockSize ?? 20,
        filterModel: grid.initialState?.filter?.filterModel ?? {},
        sortModel: [],
        context: {},
        api: {
          isDestroyed: () => false,
          getGridOption: () => source,
          setGridOption: () => {},
          updateGridOptions: () => {},
        } as unknown as GridApi<T>,
        successCallback: (items, totalCount) => resolve({ items, totalCount }),
        failCallback: () => reject(new Error('表格查询失败')),
        ...overrides,
      });
    },
  );
}

/** 在单元格真实上下文中触发业务操作，避免测试依赖 AG 的虚拟滚动尺寸。 */
export function actionCell<T>(grid: AgGridReactProps<T>, data: T) {
  const col = (grid.columnDefs as ColDef<T>[]).find(
    (c) => c.headerName === '操作',
  );
  if (!col?.cellRenderer) throw new Error('缺少操作列');
  const Cell = col.cellRenderer as ComponentType<ICellRendererParams<T>>;
  return (
    <Cell
      {...col.cellRendererParams}
      data={data}
      value={null}
      node={{ rowIndex: 0 }}
    />
  );
}
