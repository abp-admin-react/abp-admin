import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { Alert, Button, Input, Modal, message, Popconfirm, Select } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import {
  type EnabledLanguage,
  getEnabledLanguages,
  getLanguages,
} from '@/abp/localizationAdmin';
import {
  getTextTemplateContent,
  getTextTemplates,
  restoreTextTemplate,
  type TextTemplate,
  updateTextTemplate,
} from '@/abp/textTemplates';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { firstFilterValue, textFilter } from '@/components/tableColumnFilters';

const TextTemplatesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  const access = useAccess();
  const [languages, setLanguages] = useState<EnabledLanguage[]>([]);
  const [selectedCulture, setSelectedCulture] = useState<string | undefined>(
    undefined,
  );
  const [editingTemplate, setEditingTemplate] = useState<TextTemplate | null>(
    null,
  );
  const [editModalVisible, setEditModalVisible] = useState(false);
  const [editContent, setEditContent] = useState('');
  const [loading, setLoading] = useState(false);
  const contentRequest = useRef(0);
  const canEdit = (template: TextTemplate | null) =>
    access.canUpdateTextTemplates &&
    !!template &&
    (template.isSandboxed || access.canEditNonSandboxedTemplates);

  // 加载语言列表
  useEffect(() => {
    const load = access.canManageLanguages
      ? getLanguages().then(({ items }) => items)
      : getEnabledLanguages();
    load
      .then(setLanguages)
      .catch(() => message.error('语言列表加载失败'));
  }, [access.canManageLanguages]);

  // 打开编辑模态框
  const openEditModal = async (template: TextTemplate, culture?: string) => {
    const request = ++contentRequest.current;
    setLoading(true);
    try {
      const data = await getTextTemplateContent(template.name, culture);
      if (request !== contentRequest.current) return;
      setEditingTemplate({ ...template, ...data });
      setEditContent(data.content || '');
      setSelectedCulture(culture);
      setEditModalVisible(true);
    } catch {
      if (request === contentRequest.current) message.error('加载模板内容失败');
    } finally {
      if (request === contentRequest.current) setLoading(false);
    }
  };

  const closeTemplate = () => {
    contentRequest.current++;
    setLoading(false);
    setEditModalVisible(false);
  };

  // 保存模板
  const handleSave = async () => {
    if (!editingTemplate || !canEdit(editingTemplate) || loading) return;

    setLoading(true);
    try {
      await updateTextTemplate({
        name: editingTemplate.name,
        content: editContent,
        cultureName: selectedCulture,
      });
      message.success('已保存');
      setEditModalVisible(false);
      actionRef.current?.reload();
    } catch (error: any) {
      message.error(error?.data?.error?.message || '保存失败');
    } finally {
      setLoading(false);
    }
  };

  // 恢复默认
  const handleRestore = async (template: TextTemplate, culture?: string) => {
    if (loading || !access.canUpdateTextTemplates) return;
    setLoading(true);
    try {
      await restoreTextTemplate({
        name: template.name,
        cultureName: culture,
      });
      message.success('已恢复默认');
      setEditModalVisible(false);
      actionRef.current?.reload();
    } catch (error: any) {
      message.error(error?.data?.error?.message || '恢复默认失败');
    } finally {
      setLoading(false);
    }
  };

  // 文化选项
  const cultureOptions = [
    { label: '默认（文化无关）', value: '' },
    ...languages.map((lang) => ({
      label: lang.displayName || lang.cultureName,
      value: lang.cultureName,
    })),
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<TextTemplate>
        rowKey="name"
        actionRef={actionRef}
        // 列表过滤走后端（filter 参数：按名称/显示名模糊匹配），筛选 UI 在列头
        search={false}
        loading={loading}
        columns={[
          {
            title: '名称',
            dataIndex: 'name',
            width: 300,
            ...textFilter('按模板名称筛选'),
          },
          {
            title: '显示名',
            dataIndex: 'displayName',
            ellipsis: true,
            ...textFilter('按显示名筛选'),
          },
          {
            title: '布局',
            dataIndex: 'isLayout',
            width: 80,
            search: false,
            valueEnum: { true: { text: '是' }, false: { text: '否' } },
          },
          {
            title: '沙箱',
            dataIndex: 'isSandboxed',
            width: 80,
            search: false,
            render: (_, record) => (
              <span style={{ color: record.isSandboxed ? 'green' : 'red' }}>
                {record.isSandboxed ? '是' : '否'}
              </span>
            ),
          },
          {
            title: '操作',
            valueType: 'option',
            width: 200,
            render: (_, record) => [
              <a key="edit" onClick={() => openEditModal(record)}>
                {canEdit(record) ? '编辑' : '查看'}
              </a>,
              access.canUpdateTextTemplates && (
                <Popconfirm
                  key="restore"
                  title="确定要恢复默认内容吗？"
                  description="将删除当前租户对该模板的所有覆盖记录。"
                  onConfirm={() => handleRestore(record)}
                >
                  <a style={{ color: 'orange' }}>恢复默认</a>
                </Popconfirm>
              ),
            ],
          },
        ]}
        request={async (_params, _sorter, filter) => {
          const items = await getTextTemplates(
            firstFilterValue(filter, 'name') ||
              firstFilterValue(filter, 'displayName'),
          );
          return { data: items, total: items.length, success: true };
        }}
      />

      {/* 编辑模态框 */}
      <Modal
        title={`${canEdit(editingTemplate) ? '编辑模板' : '查看模板'} - ${editingTemplate?.name ?? ''}`}
        open={editModalVisible}
        onCancel={closeTemplate}
        width={800}
        footer={[
          <Button key="cancel" onClick={closeTemplate}>
            关闭
          </Button>,
          canEdit(editingTemplate) && (
            <Button
              key="save"
              type="primary"
              loading={loading}
              onClick={handleSave}
            >
              保存
            </Button>
          ),
        ]}
      >
        {/* 非沙箱警告 */}
        {editingTemplate?.isSandboxed === false && (
          <Alert
            type="warning"
            showIcon
            message="非沙箱模板警告"
            description="此模板使用非沙箱渲染引擎，可以执行任意代码。编辑此模板需要特殊权限，请谨慎操作。"
            style={{ marginBottom: 16 }}
          />
        )}

        {/* 文化选择 */}
        <div style={{ marginBottom: 16 }}>
          <label htmlFor="template-culture" style={{ marginRight: 8 }}>
            文化：
          </label>
          <Select
            id="template-culture"
            style={{ width: 200 }}
            disabled={loading}
            value={selectedCulture || ''}
            onChange={(value) => {
              const culture = value || undefined;
              if (editingTemplate) {
                openEditModal(editingTemplate, culture);
              }
            }}
            options={cultureOptions}
          />
        </div>

        {/* 内容编辑器（Modal 内无 ProForm 上下文，直接用受控 Input.TextArea） */}
        <Input.TextArea
          aria-label="模板内容"
          rows={16}
          value={editContent}
          onChange={(e) => setEditContent(e.target.value)}
          style={{ fontFamily: 'monospace' }}
          readOnly={loading || !canEdit(editingTemplate)}
        />

        {/* 恢复默认按钮 */}
        {access.canUpdateTextTemplates && (
          <div style={{ marginTop: 16, textAlign: 'right' }}>
            <Popconfirm
              title="确定要恢复默认内容吗？"
              description={`将删除当前租户对该模板${selectedCulture ? ` (${selectedCulture})` : ''}的覆盖记录。`}
              onConfirm={() => editingTemplate && handleRestore(editingTemplate, selectedCulture)}
            >
              <Button danger disabled={loading}>
                恢复默认
              </Button>
            </Popconfirm>
          </div>
        )}
      </Modal>
    </PageContainer>
  );
};

export default TextTemplatesPage;
