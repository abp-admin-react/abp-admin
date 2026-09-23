import { RollbackOutlined, SaveOutlined } from '@ant-design/icons';
import { PageContainer, ProTable } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { Button, Input, message, Popconfirm, Select, Space, Switch, Tooltip } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { getApiAppLanguage } from '@/services/abpadmin/language';
import {
  getApiAppLanguageText,
  getApiAppLanguageTextResourceNames,
  postApiAppLanguageTextRestoreToDefault,
  putApiAppLanguageText,
} from '@/services/abpadmin/languageText';

const LanguageTextsPage: React.FC = () => {
  const [resourceNames, setResourceNames] = useState<string[]>([]);
  const [languages, setLanguages] = useState<API.LanguageDto[]>([]);
  const [baseCulture, setBaseCulture] = useState<string | undefined>(undefined);
  // 目标文化：Pro 同款由页面默认选中（第二个启用语言，否则第一个），不放进搜索表单
  const [targetCulture, setTargetCulture] = useState<string | undefined>(undefined);
  const [editingKey, setEditingKey] = useState<string>('');
  const [editingValue, setEditingValue] = useState<string>('');
  const [onlyEmpty, setOnlyEmpty] = useState(false);
  const tableRef = useRef<any>(null);
  const access = useAccess();

  // 目标文化就绪后重载：首请求在语言列表到达前发出（带空目标文化会被后端 400 拒绝，
  // request 里的守卫此时回空页），就绪后必须补一发真实请求，否则首屏停留在空表
  useEffect(() => {
    if (targetCulture) {
      tableRef.current?.reload();
    }
  }, [targetCulture]);

  useEffect(() => {
    // 加载资源名称列表；失败要可见——静默吞掉会让"资源"下拉为空且列表无从过滤
    getApiAppLanguageTextResourceNames()
      .then((res) => {
        setResourceNames(res.items || []);
      })
      .catch(() => message.error('加载资源列表失败，请刷新重试'));
    // 加载语言列表；基准/目标文化默认取启用语言（Pro 同款：基准=第一个，目标=第二个，否则第一个）。
    // 失败或无启用语言时 targetCulture 保持 undefined，列表守卫回空页——必须提示，否则空表像"无数据"
    getApiAppLanguage()
      .then((res) => {
        const enabled = res.items?.filter((x) => x.isEnabled) || [];
        setLanguages(enabled);
        setBaseCulture((current) => current ?? enabled[0]?.cultureName);
        setTargetCulture((current) => current ?? enabled[1]?.cultureName ?? enabled[0]?.cultureName);
        if (!enabled.length) {
          message.warning('没有启用的语言，无法浏览本地化文本');
        }
      })
      .catch(() => message.error('加载语言列表失败，请刷新重试'));
  }, []);

  // 覆盖保存：写入当前上下文（host 或本租户）的覆盖行，空值=显式标记未翻译。
  // 成功后 reload 当页：生效值与 isOverridden 都以下一次列表查询的最新口径为准。
  const handleSave = async (record: API.LanguageTextDto) => {
    try {
      await putApiAppLanguageText({
        resourceName: record.resourceName!,
        cultureName: record.cultureName!,
        name: record.name!,
        value: editingValue,
      });
      message.success('保存成功');
      setEditingKey('');
      tableRef.current?.reload();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '保存失败');
    }
  };

  // 恢复默认：后端只删当前上下文那一层覆盖行（per-context 语义）。
  // 与列表 isOverridden（当前层口径）同口径——按钮仅在 true 时可点，点击必然删除覆盖行；
  // 但若覆盖值恰好等于下层提供值，显示值可能不变（存储变化 ≠ 显示变化）。
  const handleRestore = async (record: API.LanguageTextDto) => {
    try {
      await postApiAppLanguageTextRestoreToDefault({
        resourceName: record.resourceName!,
        cultureName: record.cultureName!,
        name: record.name!,
      });
      message.success('已恢复默认');
      tableRef.current?.reload();
    } catch (e: any) {
      message.error(e?.response?.data?.error?.message || '恢复失败');
    }
  };

  const isEditing = (record: API.LanguageTextDto) =>
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
          value: lang.cultureName!,
          label: lang.displayName ?? lang.cultureName!,
        }))}
        allowClear={false}
      />
    </>
  );

  return (
    <PageContainer>
      <ProTable
        actionRef={tableRef}
        rowKey={(record) =>
          `${record.resourceName}-${record.cultureName}-${record.name}`
        }
        columns={[
          {
            title: '资源',
            dataIndex: 'resourceName',
            valueType: 'select',
            // 不选（可清空）= 跨全部注册资源列出（后端合并静态基线 + 覆盖行）
            fieldProps: { allowClear: true, placeholder: '全部资源' },
            valueEnum: resourceNames.reduce(
              (acc, name) => {
                acc[name] = { text: name };
                return acc;
              },
              {} as Record<string, { text: string }>,
            ),
          },
          {
            title: 'Key',
            dataIndex: 'name',
            ellipsis: true,
            // 该搜索框喂给后端 Filter（key 或生效值、大小写不敏感）——
            // 标题叫"Key"，placeholder 必须说清也匹配值，否则口径误导
            fieldProps: { placeholder: '按 Key 或生效值模糊搜索' },
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
                record.baseValue || (
                  <span style={{ color: '#999' }}>(空)</span>
                )
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
        ]}
        toolbar={{
          filter: (
            <Space>
              {/* 基准/目标语言都是工具栏状态（不在搜索表单里，「重 置」不触及）。
                  目标语言的 reload 由 useEffect([targetCulture]) 统一触发——
                  onChange 里再调一次会与 effect 叠成重复控制路径（reload 有防抖，
                  实际只发一单请求，但双路径是没必要的复杂度） */}
              {renderCultureSelect('基准语言', baseCulture, (value) => {
                setBaseCulture(value);
                tableRef.current?.reload();
              })}
              {renderCultureSelect('目标语言', targetCulture, setTargetCulture)}
              <Switch
                checked={onlyEmpty}
                onChange={(checked) => {
                  setOnlyEmpty(checked);
                  tableRef.current?.reload();
                }}
              />
              <span>只显示空值</span>
            </Space>
          ),
        }}
        request={async (params) => {
          // 目标文化走工具栏状态（必选，默认选中启用语言），不进搜索表单；
          // 资源可空 = 跨全部注册资源（后端合并静态基线 + 覆盖行）。
          // 语言列表未就绪时先回空页，避免带空目标文化打出 400（就绪后由 effect 重载）
          if (!targetCulture) {
            return { data: [], total: 0, success: true };
          }
          const result = await getApiAppLanguageText({
            ResourceName: params.resourceName,
            CultureName: targetCulture,
            BaseCultureName: baseCulture,
            Filter: params.name,
            OnlyEmpty: onlyEmpty,
            SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
            MaxResultCount: params.pageSize ?? 10,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        search={{
          labelWidth: 'auto',
        }}
        pagination={{
          pageSize: 20,
        }}
      />
    </PageContainer>
  );
};

export default LanguageTextsPage;
