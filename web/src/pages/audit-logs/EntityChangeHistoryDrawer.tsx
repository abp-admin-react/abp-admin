import { ProTable } from '@ant-design/pro-components';
import { Drawer } from 'antd';
import React from 'react';
import { getEntityChangeHistory } from '@/abp/proModules';
import { changeTypeText } from './changeType';
import PropertyChangesTable from './components/PropertyChangesTable';

interface EntityChangeHistoryDrawerProps {
  open: boolean;
  onClose: () => void;
  entityTypeFullName?: string;
  entityId?: string;
}

const EntityChangeHistoryDrawer: React.FC<EntityChangeHistoryDrawerProps> = ({
  open,
  onClose,
  entityTypeFullName,
  entityId,
}) => {
  return (
    <Drawer
      title={`实体变更历史 - ${entityTypeFullName?.split('.').pop()} (${entityId})`}
      open={open}
      onClose={onClose}
      size={720}
      destroyOnHidden
    >
      <ProTable
        rowKey="id"
        search={false}
        options={false}
        request={async (params) => {
          if (!entityTypeFullName || !entityId) {
            return { data: [], total: 0, success: true };
          }
          const result = await getEntityChangeHistory({
            entityTypeFullName,
            entityId,
            current: params.current,
            pageSize: params.pageSize,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        columns={[
          {
            title: '变更时间',
            dataIndex: 'changeTime',
            valueType: 'dateTime',
            width: 160,
          },
          {
            title: '变更类型',
            dataIndex: 'changeType',
            width: 80,
            render: (_, record) => changeTypeText(record.changeType),
          },
          {
            title: '操作人',
            dataIndex: 'userName',
            width: 100,
            render: (_, record) => record.userName || '-',
          },
          {
            title: '属性变更',
            dataIndex: 'propertyChanges',
            render: (_, record) => (
              <PropertyChangesTable
                propertyChanges={record.propertyChanges}
                showHeader={false}
              />
            ),
          },
        ]}
      />
    </Drawer>
  );
};

export default EntityChangeHistoryDrawer;
