import { ProTable } from '@ant-design/pro-components';
import React from 'react';

interface PropertyChangeRow {
  propertyName?: string;
  originalValue?: string;
  newValue?: string;
}

interface PropertyChangesTableProps {
  /** 属性变更列表（实体变更历史 / 审计详情共用）。 */
  propertyChanges?: PropertyChangeRow[];
  /** 嵌在父表格列里时隐藏表头（父列已有"属性变更"标题）。 */
  showHeader?: boolean;
}

/** 「属性/原值/新值」三列的属性变更嵌套表格（audit-logs 页面内两处共用）。 */
const PropertyChangesTable: React.FC<PropertyChangesTableProps> = ({
  propertyChanges,
  showHeader = true,
}) => (
  <ProTable
    rowKey={(row: PropertyChangeRow) => row.propertyName ?? ''}
    search={false}
    pagination={false}
    toolBarRender={false}
    showHeader={showHeader}
    dataSource={propertyChanges || []}
    columns={[
      { title: '属性', dataIndex: 'propertyName', width: 120 },
      { title: '原值', dataIndex: 'originalValue', ellipsis: true },
      { title: '新值', dataIndex: 'newValue', ellipsis: true },
    ]}
  />
);

export default PropertyChangesTable;
