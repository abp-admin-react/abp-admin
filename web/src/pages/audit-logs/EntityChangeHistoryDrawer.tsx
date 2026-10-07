import { ProTable } from '@ant-design/pro-components';
import { App, Button, Drawer, Popconfirm } from 'antd';
import React, { useRef, useState } from 'react';
import type { ActionType } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  getEntityChangeHistory,
  restoreEntityChange,
  type EntityRestoreResult,
} from '@/abp/proModules';

import type { PropertyChangeRow } from './components/PropertyChangesTable';

interface EntityChangeHistoryItem {
  id: string;
  changeType: number;
  userName?: string;
  propertyChanges: PropertyChangeRow[];
}
import { changeTypeText } from './changeType';
import PropertyChangesTable from './components/PropertyChangesTable';

interface EntityChangeHistoryDrawerProps {
  open: boolean;
  onClose: () => void;
  entityTypeFullName?: string;
  entityId?: string;
}

/**
 * 实体变更历史抽屉。回滚动作只在 Updated 型变更行展示（Created/Deleted 的还原
 * 涉及删除/重建实体，后端明确不支持），按钮级权限 AbpAdmin.AuditLogs.Restore。
 */
const EntityChangeHistoryDrawer: React.FC<EntityChangeHistoryDrawerProps> = ({
  open,
  onClose,
  entityTypeFullName,
  entityId,
}) => {
  const access = useAccess();
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(undefined);

  const [restoringId, setRestoringId] = useState<string | null>(null);

  const handleRestore = async (entityChangeId: string) => {
    if (!entityTypeFullName || !entityId || restoringId) {
      return;
    }
    setRestoringId(entityChangeId);
    try {
      const result: EntityRestoreResult = await restoreEntityChange({
        entityChangeId,
        entityId,
        entityTypeFullName,
      });
      const skippedNote = result.skippedProperties.length
        ? `，${result.skippedProperties.length} 项跳过：${result.skippedProperties
            .map((x) => x.propertyName)
            .join('、')}`
        : '';
      message.success(`已回滚 ${result.restoredProperties.length} 项属性${skippedNote}`);
      actionRef.current?.reload();
    } finally {
      // 失败时全局 errorHandler 已 toast 服务端真实原因（避免 modal + toast 双重提示）
      setRestoringId(null);
    }
  };

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
        actionRef={actionRef}
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
          ...(access.canRestoreEntityChange
            ? [
                {
                  title: '操作',
                  width: 80,
                  render: (_dom: unknown, record: EntityChangeHistoryItem) =>
                    // 仅 Updated（1）型变更可回滚；回滚会写回该次变更前的旧值
                    record.changeType === 1 ? (
                      <Popconfirm
                        title="回滚此变更？"
                        description="会把该次变更涉及的属性恢复为变更前的值，回滚本身会生成一条新的变更记录。"
                        onConfirm={() => handleRestore(record.id)}
                      >
                        <Button size="small" type="link" disabled={restoringId === record.id}>
                          回滚
                        </Button>
                      </Popconfirm>
                    ) : null,
                },
              ]
            : []),
        ]}
      />
    </Drawer>
  );
};

export default EntityChangeHistoryDrawer;
