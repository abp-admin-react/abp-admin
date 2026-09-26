import {
  type ActionType,
  type ProColumns,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm, Tag } from 'antd';
import type React from 'react';
import { useRef } from 'react';
import { queryClient } from '@/queryClient';
import type { DataDictionaryListItem } from '../service';
import { deleteDataDictionary, getDataDictionaries } from '../service';
import DictionaryForm from './DictionaryForm';

interface DictionaryListProps {
  selectedId?: string;
  /** 外部触发列表刷新（右侧保存后字典显示名可能变了） */
  version: number;
  onSelect: (dictionary: DataDictionaryListItem) => void;
}

/** 左侧字典列表。字典数量是几十量级，一次拉 100 条。 */
const DictionaryList: React.FC<DictionaryListProps> = ({
  selectedId,
  version,
  onSelect,
}) => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);

  const columns: ProColumns<DataDictionaryListItem>[] = [
    { title: '编码', dataIndex: 'code', search: false, ellipsis: true },
    { title: '显示名', dataIndex: 'displayText', search: false },
    {
      title: '静态',
      dataIndex: 'isStatic',
      search: false,
      width: 64,
      render: (_, record) =>
        record.isStatic ? <Tag color="blue">静态</Tag> : <Tag>可编辑</Tag>,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 110,
      render: (_, record) => [
        access.canUpdateDataDictionary ? (
          <DictionaryForm
            key="edit"
            title={`编辑字典 - ${record.code ?? ''}`}
            trigger={<a>编辑</a>}
            initialValues={record}
            onSuccess={() => actionRef.current?.reload()}
          />
        ) : null,
        // 静态字典由代码定义，不允许删除（后端会拒绝，UI 同步隐藏入口）
        access.canDeleteDataDictionary && !record.isStatic ? (
          <Popconfirm
            key="delete"
            title={`确认删除字典「${record.displayText ?? record.code}」？`}
            onConfirm={async () => {
              try {
                await deleteDataDictionary(record.code ?? '');
                // 该字典的合并视图缓存（useDictionary / dictionaryRequest 共用）一并失效
                await queryClient.invalidateQueries({
                  queryKey: ['data-dictionary'],
                });
                message.success('已删除');
                actionRef.current?.reload();
              } catch (e) {
                // 服务端拒绝（静态结构锁等）必须可见，与表单/编辑器的错误呈现契约一致
                message.error(
                  e instanceof Error && e.message
                    ? e.message
                    : '删除失败，请重试',
                );
              }
            }}
          >
            <a>删除</a>
          </Popconfirm>
        ) : null,
      ],
    },
  ];

  return (
    <ProTable<DataDictionaryListItem>
      headerTitle="字典"
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      search={false}
      // params 变化会让 ProTable 重新执行 request：父组件用递增 version 驱动左列重查
      params={{ version }}
      pagination={{ pageSize: 100, hideOnSinglePage: true }}
      request={async (params) => {
        const result = await getDataDictionaries({
          current: params.current,
          pageSize: params.pageSize,
        });
        return {
          data: result.items ?? [],
          total: result.totalCount ?? 0,
          success: true,
        };
      }}
      rowClassName={(record) =>
        record.id === selectedId ? 'ant-table-row-selected' : ''
      }
      onRow={(record) => ({
        onClick: () => onSelect(record),
        style: { cursor: 'pointer' },
      })}
      toolBarRender={() => [
        access.canCreateDataDictionary ? (
          <DictionaryForm
            key="create"
            title="新建字典"
            trigger={<Button type="primary">新建字典</Button>}
            onSuccess={() => actionRef.current?.reload()}
          />
        ) : null,
      ]}
    />
  );
};

export default DictionaryList;
