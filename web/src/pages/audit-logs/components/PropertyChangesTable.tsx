import { AgGridReact } from 'ag-grid-react';
import React from 'react';
import {
  type RemoteColumn,
  useSearchableLocalGrid,
} from '@/components/remoteGrid';

export interface PropertyChangeRow {
  propertyName?: string;
  originalValue?: string;
  newValue?: string;
}

const columns: RemoteColumn<PropertyChangeRow>[] = [
  { field: 'propertyName', headerName: '属性' },
  { field: 'originalValue', headerName: '原值' },
  { field: 'newValue', headerName: '新值' },
];

export default function PropertyChangesTable({
  propertyChanges,
}: {
  propertyChanges?: PropertyChangeRow[];
}) {
  const grid = useSearchableLocalGrid({
    ariaLabel: '属性变更',
    columnDefs: columns,
    rowData: propertyChanges,
    keywordFields: ['propertyName', 'originalValue', 'newValue'],
    pagination: false,
    getRowId: ({ data }) => data.propertyName ?? '',
  });
  return (
    <>
      {grid.searchBox}
      <AgGridReact {...grid.gridProps} />
    </>
  );
}
