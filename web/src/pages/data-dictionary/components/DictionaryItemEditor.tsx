import { EditableProTable, type ProColumns } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { useModel } from '@umijs/max';
import { Alert, App, Button, Tag } from 'antd';
import type React from 'react';
import { useEffect, useState } from 'react';
import { dictionaryQueryKey } from '@/hooks/useDictionary';
import { queryClient } from '@/queryClient';
import type { DataDictionaryListItem } from '../service';
import { getDataDictionaryByCode, saveDataDictionaryItems } from '../service';

interface EditableItem {
  // EditableProTable 的行 key 必须在编辑过程中保持稳定。
  // 不能直接拿可编辑的 code 当 rowKey：新行 code 初始为空串，
  // 输入第一个字符 rowKey 就变，可编辑表单按 rowKey 存状态，key 一换输入全丢。
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

/**
 * 右侧字典项编辑（T3.4 第 7 步）。
 * 全量加载、不分页（一个字典的项数是几十量级；真有几百项是数据模型设计问题）。
 * 保存走自建原子端点 saveDataDictionaryItems：显示信息 + 全部项 + 展示元数据一次提交
 * （同一后端工作单元；Order 取数组顺序；静态字典有服务端结构锁，漏传整单拒绝）。
 * 数据源是自建视图端点而不是模块端点——模块 DTO 不带 isStatic/tagType。
 */
const DictionaryItemEditor: React.FC<DictionaryItemEditorProps> = ({
  dictionary,
  onSaved,
}) => {
  const { message } = App.useApp();
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
  const [editableKeys, setEditableKeys] = useState<React.Key[]>([]);
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
        _key: it.code ?? '',
        code: it.code ?? '',
        displayText: it.displayText,
        description: it.description,
        tagType: it.tagType ?? undefined,
        isStatic: it.isStatic,
      })),
    );
  }, [viewQuery.data]);

  const isStaticDict = !!dictionary.isStatic;

  const save = async () => {
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

  const columns: ProColumns<EditableItem>[] = [
    {
      title: '编码',
      dataIndex: 'code',
      formItemProps: { rules: [{ required: true, message: '编码必填' }] },
      // 静态项的编码不允许改，后端代码可能强依赖
      editable: (_, row) => !row.isStatic,
    },
    {
      title: '显示名',
      dataIndex: 'displayText',
      formItemProps: { rules: [{ required: true, message: '显示名必填' }] },
    },
    { title: '描述', dataIndex: 'description' },
    {
      title: '标签颜色',
      dataIndex: 'tagType',
      valueType: 'select',
      fieldProps: { options: tagTypeOptions, allowClear: true },
      render: (_, row) =>
        row.tagType ? <Tag color={row.tagType}>{row.tagType}</Tag> : '—',
    },
    {
      title: '静态',
      dataIndex: 'isStatic',
      editable: false,
      width: 72,
      render: (_, row) =>
        row.isStatic ? (
          <Tag
            color="blue"
            title="由代码定义：可改显示名，不允许改编码、不允许删除"
          >
            静态
          </Tag>
        ) : (
          <Tag>可编辑</Tag>
        ),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 130,
    },
  ];

  return (
    <>
      {!isStaticDict && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title="保存是对全部字典项的一次性全量提交：列表里没有的项会被删除。本页面不支持并发编辑，后保存的会覆盖先保存的。"
        />
      )}
      {isStaticDict && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          title="这是静态字典（由代码定义并随种子写入）：只允许改显示名，不允许改编码、不允许删除、不允许新增。种子只在首次写入/补缺失项，您的修改不会被重新部署重置。"
        />
      )}
      <EditableProTable<EditableItem>
        rowKey="_key"
        loading={viewQuery.isLoading}
        value={items}
        onChange={(next) => setItems([...next])}
        pagination={false}
        // 静态字典不允许新增项（与服务端结构锁一致），直接关掉行创建器
        recordCreatorProps={
          isStaticDict
            ? false
            : {
                record: () => ({
                  _key: `__new_${++newItemKeySeq}`,
                  code: '',
                  displayText: '',
                  isStatic: false,
                }),
                creatorButtonText: '新增字典项',
              }
        }
        editable={{
          type: 'multiple',
          editableKeys,
          onChange: setEditableKeys,
          // 静态项不允许删除，删除按钮置灰的原因见 T3.4 第 5 步 IsStatic 语义
          actionRender: (row, _config, defaultDom) =>
            row.isStatic
              ? [defaultDom.save, defaultDom.cancel]
              : [defaultDom.save, defaultDom.delete, defaultDom.cancel],
        }}
        columns={columns}
      />
      <Button
        type="primary"
        onClick={save}
        loading={saving}
        style={{ marginTop: 16 }}
      >
        保存全部字典项
      </Button>
    </>
  );
};

export default DictionaryItemEditor;
