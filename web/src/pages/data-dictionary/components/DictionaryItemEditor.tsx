import { useQuery } from '@tanstack/react-query';
import { useAccess, useModel } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  Alert,
  App,
  Button,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Tag,
} from 'antd';
import type React from 'react';
import { useEffect, useState } from 'react';
import {
  type RemoteColumn,
  useSearchableLocalGrid,
} from '@/components/remoteGrid';
import { dictionaryQueryKey } from '@/hooks/useDictionary';
import { queryClient } from '@/queryClient';
import type { DataDictionaryListItem } from '../service';
import { getDataDictionaryByCode, saveDataDictionaryItems } from '../service';

interface EditableItem {
  // 用户可修改编码，本地行标识随草稿保留。
  _key: string;
  code: string;
  displayText?: string;
  description?: string;
  tagType?: string;
  isStatic?: boolean;
}

/** 新行的稳定 key 生成器（同一会话内递增即可，无需全局唯一） */
let newItemKeySeq = 0;

interface DictionaryItemEditorProps {
  dictionary: DataDictionaryListItem;
  onSaved: () => void;
}

/** 排序和搜索只影响显示，保存始终按原始顺序提交完整草稿。 */
const DictionaryItemEditor: React.FC<DictionaryItemEditorProps> = ({
  dictionary,
  onSaved,
}) => {
  const { message } = App.useApp();
  const { canUpdateDataDictionary } = useAccess();
  // 标签色白名单的唯一来源是后端 DataDictionaryTagTypes（经 application-configuration
  // 的 extraProperties.dataDictionaryTagTypes 下发，见 DataDictionaryTagTypesContributor），
  // 前端不再硬编码副本
  const { initialState } = useModel('@@initialState');
  const tagTypeOptions = (initialState?.dataDictionaryTagTypes ?? []).map(
    (v) => ({
      label: v,
      value: v,
    }),
  );
  const [items, setItems] = useState<EditableItem[]>([]);
  const [editing, setEditing] = useState<EditableItem>();
  const [form] = Form.useForm<EditableItem>();
  const [saving, setSaving] = useState(false);

  // 合并视图查询：enabled 卡住空 code——上级行保证挂载时必有 code，这里只做防御
  const viewQuery = useQuery({
    queryKey: dictionaryQueryKey(dictionary.code ?? ''),
    queryFn: () => getDataDictionaryByCode(dictionary.code ?? ''),
    enabled: !!dictionary.code,
  });

  // 服务端 → 本地可编辑副本的单向同步：查询成功/换字典重挂时整表重置。
  // 之后表格以本地 items 为编辑对象，保存成功会失效字典缓存触发重查，进而走这里重置。
  useEffect(() => {
    setItems(
      (viewQuery.data?.items ?? []).map((it) => ({
        _key: `stored:${it.code ?? ''}`,
        code: it.code ?? '',
        displayText: it.displayText,
        description: it.description ?? undefined,
        tagType: it.tagType ?? undefined,
        isStatic: it.isStatic,
      })),
    );
  }, [viewQuery.data]);

  const isStaticDict = !!dictionary.isStatic;
  const canSave =
    canUpdateDataDictionary &&
    !!viewQuery.data &&
    !viewQuery.isFetching &&
    !editing;

  const save = async () => {
    if (!canSave || saving) return;
    if (items.some((it) => !it.code || !it.displayText)) {
      message.error('编码与显示名不能为空');
      return;
    }
    if (new Set(items.map((it) => it.code)).size !== items.length) {
      message.error('编码不能重复');
      return;
    }

    setSaving(true);
    try {
      // 字典本体字段（显示名/描述）现取服务端合并视图：左侧编辑弹窗保存后 index.tsx 的
      // selected 还是旧快照，拿旧 prop 回写会把刚保存的改名静默打回旧名（后端 SetContent
      // 是无条件覆盖）。项集合仍以本地表格为准——这是本编辑器的编辑对象。
      const view = await getDataDictionaryByCode(dictionary.code ?? '');
      if (!view) {
        throw new Error('字典已被删除，请刷新列表');
      }
      // 原子保存：显示信息 + 全部字典项 + 展示元数据一次提交（同一后端工作单元），
      // 替代"模块 UpdateAsync 全量替换 + items-meta"两次调用——中间失败会留下不一致中间态。
      // Items 是全量替换语义：必须提交完整列表，漏掉的条目会被服务端删除（静态字典禁止增删）。
      // Order 取数组顺序——枚举字典的视图返回顺序就是声明顺序。
      await saveDataDictionaryItems(dictionary.code ?? '', {
        displayText: view.displayText ?? dictionary.displayText ?? '',
        description: view.description ?? null,
        items: items.map((it) => ({
          code: it.code,
          displayText: it.displayText ?? '',
          description: it.description,
          // undefined 序列化后等价 null（无颜色）
          tagType: it.tagType,
        })),
      });

      // 让全站字典缓存失效（useDictionary / dictionaryRequest 共用这份缓存）
      await queryClient.invalidateQueries({ queryKey: ['data-dictionary'] });
      message.success('已保存');
      onSaved();
    } catch (e) {
      // 服务端校验失败（非法 tagType / 编码重复 / 静态字典改结构）必须可见：
      // 吞掉异常时弹窗只是转一下 spinner，用户不知道保存没发生
      message.error(
        e instanceof Error && e.message
          ? e.message
          : '保存失败，请检查输入后重试',
      );
    } finally {
      setSaving(false);
    }
  };

  const openEditor = (row: EditableItem) => {
    form.setFieldsValue({
      ...row,
      description: row.description,
      tagType: row.tagType,
    });
    setEditing(row);
  };
  const columns: RemoteColumn<EditableItem>[] = [
    { field: 'code', headerName: '编码', flex: 1 },
    { field: 'displayText', headerName: '显示名', flex: 1 },
    { field: 'description', headerName: '描述', flex: 1 },
    {
      field: 'tagType',
      headerName: '标签颜色',
      cellRenderer: ({ value }: { value?: string }) =>
        value ? <Tag color={value}>{value}</Tag> : '—',
    },
    {
      field: 'isStatic',
      headerName: '静态',
      kind: 'boolean',
      booleanLabels: { true: '静态', false: '可编辑' },
      cellRenderer: ({ value }: { value?: boolean }) => (
        <Tag color={value ? 'blue' : undefined}>
          {value ? '静态' : '可编辑'}
        </Tag>
      ),
    },
  ];
  if (canUpdateDataDictionary && viewQuery.data) {
    columns.push({
      colId: 'actions',
      kind: 'display',
      headerName: '操作',
      cellRenderer: ({ data }: { data: EditableItem }) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEditor(data)}>
            编辑
          </Button>
          {!isStaticDict && !data.isStatic && (
            <Popconfirm
              title="移除此字典项？保存全部后生效。"
              onConfirm={() =>
                setItems((rows) => rows.filter((r) => r._key !== data._key))
              }
            >
              <Button type="link" size="small">
                删除
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    });
  }
  const grid = useSearchableLocalGrid<EditableItem>({
    columnDefs: columns,
    rowData: items,
    getRowId: ({ data }) => data._key,
    ariaLabel: '字典项',
    keywordFields: ['code', 'displayText', 'description', 'tagType'],
  });

  return (
    <>
      {(viewQuery.isError || (viewQuery.isSuccess && !viewQuery.data)) && (
        <Alert
          type="error"
          showIcon
          title={viewQuery.isError ? '字典项加载失败' : '字典已不存在'}
          action={
            <Button onClick={() => void viewQuery.refetch()}>重试</Button>
          }
        />
      )}
      {canUpdateDataDictionary && !isStaticDict && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title="保存是对全部字典项的一次性全量提交：列表里没有的项会被删除。本页面不支持并发编辑，后保存的会覆盖先保存的。"
        />
      )}
      {canUpdateDataDictionary && isStaticDict && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          title="这是静态字典（由代码定义并随种子写入）：只允许改显示名，不允许改编码、不允许删除、不允许新增。种子只在首次写入/补缺失项，您的修改不会被重新部署重置。"
        />
      )}
      <Space wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {canUpdateDataDictionary && viewQuery.data && !isStaticDict && (
          <Button
            onClick={() =>
              openEditor({
                _key: `draft:${++newItemKeySeq}`,
                code: '',
                displayText: '',
                isStatic: false,
              })
            }
          >
            新增字典项
          </Button>
        )}
      </Space>
      <AgGridReact<EditableItem>
        {...grid.gridProps}
        loading={viewQuery.isLoading}
        containerStyle={{ width: '100%', height: 420 }}
      />
      <Modal
        open={!!editing}
        forceRender
        title="编辑字典项"
        onCancel={() => setEditing(undefined)}
        onOk={() => form.submit()}
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={(values) => {
            if (!editing) return;
            const item = { ...editing, ...values };
            setItems((rows) =>
              rows.some((r) => r._key === item._key)
                ? rows.map((r) => (r._key === item._key ? item : r))
                : [...rows, item],
            );
            setEditing(undefined);
          }}
        >
          <Form.Item
            name="code"
            label="编码"
            rules={[{ required: true, whitespace: true }]}
          >
            <Input disabled={isStaticDict || editing?.isStatic} />
          </Form.Item>
          <Form.Item
            name="displayText"
            label="显示名"
            rules={[{ required: true, whitespace: true }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="description" label="描述">
            <Input />
          </Form.Item>
          <Form.Item name="tagType" label="标签颜色">
            <Select options={tagTypeOptions} allowClear />
          </Form.Item>
        </Form>
      </Modal>
      {canUpdateDataDictionary && (
        <Button
          type="primary"
          onClick={save}
          loading={saving}
          disabled={!canSave}
          style={{ marginTop: 16 }}
        >
          保存全部字典项
        </Button>
      )}
    </>
  );
};

export default DictionaryItemEditor;
