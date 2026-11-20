import type { ProColumns } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { App, Button, Popconfirm, Tag } from 'antd';
import type React from 'react';
import { useEffect } from 'react';
import { queryDataDictionaries } from '@/abp/dataDictionary';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { queryClient } from '@/queryClient';
import type { DataDictionaryListItem } from '../service';
import { deleteDataDictionary } from '../service';
import DictionaryForm from './DictionaryForm';

interface DictionaryListProps {
  selectedId?: string;
  /** 外部触发列表刷新（右侧保存后字典显示名可能变了） */
  version: number;
  onSelect: (dictionary: DataDictionaryListItem) => void;
}

/** 左侧字典列表。通过服务端查询选择要编辑的字典。 */
const DictionaryList: React.FC<DictionaryListProps> = ({
  selectedId,
  version,
  onSelect,
}) => {
  const { message } = App.useApp();
  const access = useAccess();

  const columns: ProColumns<DataDictionaryListItem>[] = [
    { title: '编码', dataIndex: 'code', search: false, ellipsis: true },
    { title: '显示名', dataIndex: 'displayText' },
    {
      title: '静态',
      dataIndex: 'isStatic',

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
            onSuccess={() => grid.refresh()}
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
                grid.refresh();
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

  const grid = useSearchableRemoteGrid<DataDictionaryListItem>({
    columnDefs: toGridColumns<DataDictionaryListItem>(columns, {
      pro: true,
      kinds: { isStatic: 'boolean' },
    }),
    getRowId: ({ data }) => String(data.id ?? data.code),
    ariaLabel: '字典',
    query: queryDataDictionaries,
  });
  useEffect(() => {
    grid.refresh();
  }, [version]);

  return (
    <>
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          marginBottom: 8,
        }}
      >
        {'字典'}
        {grid.searchBox}
        {[
          access.canCreateDataDictionary ? (
            <DictionaryForm
              key="create"
              title="新建字典"
              trigger={<Button type="primary">新建字典</Button>}
              onSuccess={() => grid.refresh()}
            />
          ) : null,
        ]}
      </div>
      <AgGridReact<DataDictionaryListItem>
        {...grid.gridProps}
        onRowClicked={({ data }) => data && onSelect(data)}
        getRowStyle={({ data }) => ({
          cursor: 'pointer',
          backgroundColor:
            data && data.id === selectedId
              ? 'var(--ag-selected-row-background-color)'
              : 'transparent',
        })}
      />
    </>
  );
};

export default DictionaryList;
