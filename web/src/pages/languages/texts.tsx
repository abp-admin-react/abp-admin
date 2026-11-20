import { RollbackOutlined, SaveOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  Button,
  Input,
  message,
  Popconfirm,
  Select,
  Space,
  Switch,
  Tooltip,
} from 'antd';
import React, { useEffect, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import {
  type EnabledLanguage,
  getEnabledLanguages,
  getLanguageTextResourceNames,
  type LanguageText,
  queryLanguageTexts,
  restoreLanguageText,
  updateLanguageText,
} from '@/abp/localizationAdmin';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const LanguageTextsPage: React.FC = () => {
  const [resourceNames, setResourceNames] = useState<string[]>([]);
  const [languages, setLanguages] = useState<EnabledLanguage[]>([]);
  const [baseCulture, setBaseCulture] = useState<string | undefined>(undefined);
  // 目标文化：Pro 同款由页面默认选中（第二个启用语言，否则第一个），不放进搜索表单
  const [targetCulture, setTargetCulture] = useState<string | undefined>(
    undefined,
  );
  const [editingKey, setEditingKey] = useState<string>('');
  const [editingValue, setEditingValue] = useState<string>('');
  const [onlyEmpty, setOnlyEmpty] = useState(false);
  const access = useAccess();

  useEffect(() => {
    // 加载资源名称列表；失败要可见——静默吞掉会让"资源"下拉为空且列表无从过滤
    getLanguageTextResourceNames()
      .then((res) => {
        setResourceNames(res.items || []);
      })
      .catch(() => message.error('加载资源列表失败，请刷新重试'));
    // 加载语言列表；基准/目标文化默认取启用语言（Pro 同款：基准=第一个，目标=第二个，否则第一个）。
    // 失败或无启用语言时 targetCulture 保持 undefined，列表守卫回空页——必须提示，否则空表像"无数据"
    getEnabledLanguages()
      .then((enabled) => {
        setLanguages(enabled);
        setBaseCulture((current) => current ?? enabled[0]?.cultureName);
        setTargetCulture(
          (current) =>
            current ?? enabled[1]?.cultureName ?? enabled[0]?.cultureName,
        );
        if (!enabled.length) {
          message.warning('没有启用的语言，无法浏览本地化文本');
        }
      })
      .catch(() => message.error('加载语言列表失败，请刷新重试'));
  }, []);

  // 覆盖保存：写入当前上下文（host 或本租户）的覆盖行，空值=显式标记未翻译。
  // 成功后 reload 当页：生效值与 isOverridden 都以下一次列表查询的最新口径为准。
  const handleSave = async (record: LanguageText) => {
    if (!record.resourceName || !record.cultureName || !record.name) return;
    try {
      await updateLanguageText({
        resourceName: record.resourceName,
        cultureName: record.cultureName,
        name: record.name,
        value: editingValue,
      });
      message.success('保存成功');
      setEditingKey('');
      grid.refresh();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '保存失败');
    }
  };

  // 恢复默认：后端只删当前上下文那一层覆盖行（per-context 语义）。
  // 与列表 isOverridden（当前层口径）同口径——按钮仅在 true 时可点，点击必然删除覆盖行；
  // 但若覆盖值恰好等于下层提供值，显示值可能不变（存储变化 ≠ 显示变化）。
  const handleRestore = async (record: LanguageText) => {
    if (!record.resourceName || !record.cultureName || !record.name) return;
    try {
      await restoreLanguageText({
        resourceName: record.resourceName,
        cultureName: record.cultureName,
        name: record.name,
      });
      message.success('已恢复默认');
      grid.refresh();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '恢复失败');
    }
  };

  const isEditing = (record: LanguageText) =>
    editingKey ===
    `${record.resourceName}-${record.cultureName}-${record.name}`;

  // 语言选择器的公共渲染（基准/目标两个下拉仅 label/值/回调不同）；
  // 返回 JSX 的普通函数而非内部组件——避免每次渲染产生新组件类型导致整棵子树重挂载
  const renderCultureSelect = (
    label: string,
    value: string | undefined,
    onChange: (value: string) => void,
  ) => (
    <>
      <span>{label}</span>
      <Select
        size="small"
        style={{ width: 140 }}
        value={value}
        onChange={onChange}
        options={languages.map((lang) => ({
          value: lang.cultureName ?? '',
          label: lang.displayName ?? lang.cultureName,
        }))}
        allowClear={false}
      />
    </>
  );

  const gridQuery = React.useCallback(
    (input: GridQuery, signal: AbortSignal) =>
      targetCulture
        ? queryLanguageTexts(
            {
              cultureName: targetCulture,
              baseCultureName: baseCulture ?? '',
              onlyEmpty,
            },
            input,
            signal,
          )
        : Promise.resolve({ items: [], totalCount: 0 }),
    [targetCulture, baseCulture, onlyEmpty],
  );
  const grid = useSearchableRemoteGrid<LanguageText>({
    columnDefs: toGridColumns<LanguageText>(
      [
        {
          title: '资源',
          dataIndex: 'resourceName',
          valueEnum: Object.fromEntries(
            resourceNames.map((name) => [name, name]),
          ),
        },
        {
          title: 'Key',
          dataIndex: 'name',
          ellipsis: true,
        },
        {
          title: '基准语言',
          dataIndex: 'baseValue',
          ellipsis: true,
          // 对照列只展示；后端没有按基准值过滤的参数，搜索表单里放行会静默无效
          search: false,
          render: (_, record) => {
            if (isEditing(record)) {
              return <span style={{ color: '#999' }}>—</span>;
            }
            return (
              record.baseValue || <span style={{ color: '#999' }}>(空)</span>
            );
          },
        },
        {
          title: '值',
          dataIndex: 'value',
          ellipsis: true,
          render: (_, record) => {
            if (isEditing(record)) {
              return (
                <Input.TextArea
                  value={editingValue}
                  onChange={(e) => setEditingValue(e.target.value)}
                  rows={2}
                />
              );
            }
            return _ || <span style={{ color: '#999' }}>(空)</span>;
          },
        },
        {
          title: '操作',
          valueType: 'option',
          width: 150,
          render: (_, record) => {
            if (isEditing(record)) {
              return [
                <Button
                  key="save"
                  type="link"
                  size="small"
                  icon={<SaveOutlined />}
                  onClick={() => handleSave(record)}
                >
                  保存
                </Button>,
                <Button
                  key="cancel"
                  type="link"
                  size="small"
                  onClick={() => setEditingKey('')}
                >
                  取消
                </Button>,
              ];
            }
            return [
              access.canEditLanguageTexts && (
                <Button
                  key="edit"
                  type="link"
                  size="small"
                  onClick={() => {
                    setEditingKey(
                      `${record.resourceName}-${record.cultureName}-${record.name}`,
                    );
                    setEditingValue(record.value || '');
                  }}
                >
                  编辑
                </Button>
              ),
              access.canEditLanguageTexts && (
                <Tooltip
                  title={
                    record.isOverridden
                      ? undefined
                      : // isOverridden = 当前层级（host 或本租户）是否有覆盖行（后端口径）。
                        // false = 当前层没有可删的覆盖行 → 按钮禁用；
                        // 此时显示的值来自静态默认或上级 host 配置，恢复本就无从谈起
                        '该条目在当前层级没有覆盖记录，「恢复默认」不会产生变化'
                  }
                >
                  <Popconfirm
                    key="restore"
                    title="确定恢复默认值？"
                    disabled={!record.isOverridden}
                    onConfirm={() => handleRestore(record)}
                  >
                    <Button
                      type="link"
                      size="small"
                      icon={<RollbackOutlined />}
                      disabled={!record.isOverridden}
                    >
                      恢复默认
                    </Button>
                  </Popconfirm>
                </Tooltip>
              ),
            ].filter(Boolean);
          },
        },
      ],
      { pro: true, kinds: {} },
    ),
    getRowId: ({ data }) =>
      JSON.stringify([data.resourceName, data.cultureName, data.name]),
    ariaLabel: '语言文本',
    query: gridQuery,
  });

  return (
    <PageContainer>
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          marginBottom: 8,
        }}
      >
        {grid.searchBox}
        {
          <Space>
            {renderCultureSelect('基准语言', baseCulture, setBaseCulture)}
            {renderCultureSelect('目标语言', targetCulture, setTargetCulture)}
            <Switch checked={onlyEmpty} onChange={setOnlyEmpty} />
            <span>只显示空值</span>
          </Space>
        }
      </div>
      <AgGridReact<LanguageText> {...grid.gridProps} />
    </PageContainer>
  );
};

export default LanguageTextsPage;
