import {
  type ActionType,
  PageContainer,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { Alert, Button, Input, Modal, message, Popconfirm, Select } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { getApiAppLanguage } from '@/services/abpadmin/language';
import {
  getApiAppTextTemplate,
  getApiAppTextTemplateContent,
  postApiAppTextTemplateRestoreToDefault,
  putApiAppTextTemplate,
} from '@/services/abpadmin/textTemplate';

type TextTemplate = API.TextTemplateDto;

interface Language {
  cultureName?: string;
  displayName?: string;
}

const TextTemplatesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  const access = useAccess();
  const [languages, setLanguages] = useState<Language[]>([]);
  const [selectedCulture, setSelectedCulture] = useState<string | undefined>(
    undefined,
  );
  const [editingTemplate, setEditingTemplate] = useState<TextTemplate | null>(
    null,
  );
  const [editModalVisible, setEditModalVisible] = useState(false);
  const [editContent, setEditContent] = useState('');
  const [loading, setLoading] = useState(false);

  // 加载语言列表
  useEffect(() => {
    getApiAppLanguage().then((res) => {
      setLanguages(res.items || []);
    });
  }, []);

  // 打开编辑模态框
  const openEditModal = async (template: TextTemplate, culture?: string) => {
    setLoading(true);
    try {
      const data = await getApiAppTextTemplateContent({
        name: template.name,
        cultureName: culture,
      });
      setEditingTemplate({ ...template, ...data });
      setEditContent(data.content || '');
      setSelectedCulture(culture);
      setEditModalVisible(true);
    } catch (error) {
      message.error('加载模板内容失败');
    } finally {
      setLoading(false);
    }
  };

  // 保存模板
  const handleSave = async () => {
    if (!editingTemplate) return;

    setLoading(true);
    try {
      await putApiAppTextTemplate({
        name: editingTemplate.name ?? '',
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
    setLoading(true);
    try {
      await postApiAppTextTemplateRestoreToDefault({
        name: template.name ?? '',
        cultureName: culture,
      });
      message.success('已恢复默认');
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
      <ProTable<TextTemplate>
        rowKey="name"
        actionRef={actionRef}
        // 列表过滤走后端（filter 参数：按名称/显示名模糊匹配）
        search={{ labelWidth: 'auto', defaultColsNumber: 1 }}
        loading={loading}
        columns={[
          { title: '名称', dataIndex: 'name', width: 300 },
          { title: '显示名', dataIndex: 'displayName', ellipsis: true },
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
                编辑
              </a>,
              <Popconfirm
                key="restore"
                title="确定要恢复默认内容吗？"
                description="将删除当前租户对该模板的所有覆盖记录。"
                onConfirm={() => handleRestore(record)}
              >
                <a style={{ color: 'orange' }}>恢复默认</a>
              </Popconfirm>,
            ],
          },
        ]}
        request={async (params) => {
          // 生成的客户端已带类型化参数对象（getApiAppTextTemplateParams），filter 直接透传
          const items = await getApiAppTextTemplate({
            filter: params.name || params.displayName,
          });
          return { data: items, total: items.length, success: true };
        }}
      />

      {/* 编辑模态框 */}
      <Modal
        title={`编辑模板 - ${editingTemplate?.name}`}
        open={editModalVisible}
        onCancel={() => setEditModalVisible(false)}
        width={800}
        footer={[
          <Button key="cancel" onClick={() => setEditModalVisible(false)}>
            取消
          </Button>,
          <Button
            key="save"
            type="primary"
            loading={loading}
            onClick={handleSave}
            disabled={
              editingTemplate?.isSandboxed === false &&
              !access.canEditNonSandboxedTemplates
            }
          >
            保存
          </Button>,
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
          <span style={{ marginRight: 8 }}>文化：</span>
          <Select
            style={{ width: 200 }}
            value={selectedCulture || ''}
            onChange={(value) => {
              const culture = value || undefined;
              setSelectedCulture(culture);
              if (editingTemplate) {
                openEditModal(editingTemplate, culture);
              }
            }}
            options={cultureOptions}
          />
        </div>

        {/* 内容编辑器（Modal 内无 ProForm 上下文，直接用受控 Input.TextArea） */}
        <Input.TextArea
          rows={16}
          value={editContent}
          onChange={(e) => setEditContent(e.target.value)}
          style={{ fontFamily: 'monospace' }}
          readOnly={
            editingTemplate?.isSandboxed === false &&
            !access.canEditNonSandboxedTemplates
          }
        />

        {/* 恢复默认按钮 */}
        <div style={{ marginTop: 16, textAlign: 'right' }}>
          <Popconfirm
            title="确定要恢复默认内容吗？"
            description={`将删除当前租户对该模板${selectedCulture ? ` (${selectedCulture})` : ''}的覆盖记录。`}
            onConfirm={() => {
              if (editingTemplate) {
                handleRestore(editingTemplate, selectedCulture);
                setEditModalVisible(false);
              }
            }}
          >
            <Button danger>恢复默认</Button>
          </Popconfirm>
        </div>
      </Modal>
    </PageContainer>
  );
};

export default TextTemplatesPage;
