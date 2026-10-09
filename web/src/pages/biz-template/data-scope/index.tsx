import {
  DeleteOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  App,
  Alert,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Table,
  Tag,
} from 'antd';
import React, { useEffect, useState } from 'react';
import { getOrganizationUnits } from '@/abp/identityAdmin';
import {
  BizDataScopeDemoDto,
  createBizDataScopeDemo,
  deleteBizDataScopeDemo,
  getBizDataScopeDemos,
} from '@/abp/bizDataScopeDemo';

type OrganizationUnitItem = {
  id: string;
  displayName: string;
  code: string;
};

/**
 * 业务模块数据权限示例（AbpAdmin.Biz.Template）：后端实体实现 IHasDataScope +
 * 模块 DbContext 数据范围筛选器，列表接口零过滤代码——这里看到的行数/行集
 * 就是当前登录用户数据范围内的可见集。与框架 data-scope-demo 演示同一能力，
 * 差别只在实现落在自包含业务模块里（复制模块即带走数据权限姿势）。
 */
const BizDataScopeDemoPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<BizDataScopeDemoDto[]>([]);
  const [total, setTotal] = useState(0);
  const [ouList, setOuList] = useState<OrganizationUnitItem[]>([]);
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [form] = Form.useForm();

  const fetchData = async () => {
    setLoading(true);
    try {
      const res = await getBizDataScopeDemos({
        skipCount: 0,
        maxResultCount: 100,
      });
      setData(res.items || []);
      setTotal(res.totalCount || 0);
    } catch (e: any) {
      message.error(e?.message || '加载失败');
    } finally {
      setLoading(false);
    }
  };

  const fetchOuList = async () => {
    try {
      const res = await getOrganizationUnits();
      setOuList((res || []) as OrganizationUnitItem[]);
    } catch (e: any) {
      // ignore
    }
  };

  useEffect(() => {
    fetchData();
    fetchOuList();
  }, []);

  const handleCreate = async (values: any) => {
    try {
      await createBizDataScopeDemo({
        name: values.name,
        organizationUnitId: values.organizationUnitId || undefined,
      });
      message.success('创建成功');
      setCreateModalOpen(false);
      form.resetFields();
      fetchData();
    } catch (e: any) {
      message.error(e?.message || '创建失败');
    }
  };

  const handleDelete = async (id: string) => {
    modal.confirm({
      title: '确认删除',
      content: '确定要删除这条记录吗？',
      onOk: async () => {
        try {
          await deleteBizDataScopeDemo(id);
          message.success('删除成功');
          fetchData();
        } catch (e: any) {
          message.error(e?.message || '删除失败');
        }
      },
    });
  };

  const getOuName = (ouId?: string) => {
    if (!ouId) return <Tag>无组织</Tag>;
    const ou = ouList.find((o) => o.id === ouId);
    return ou ? <Tag color="blue">{ou.displayName}</Tag> : <Tag>{ouId}</Tag>;
  };

  const columns = [
    {
      title: '名称',
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: '所属组织',
      dataIndex: 'organizationUnitId',
      key: 'organizationUnitId',
      render: (ouId: string) => getOuName(ouId),
    },
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      key: 'creationTime',
      render: (t: string) => new Date(t).toLocaleString(),
    },
    {
      title: '操作',
      key: 'action',
      render: (_: any, record: BizDataScopeDemoDto) => (
        <Button
          type="text"
          danger
          icon={<DeleteOutlined />}
          onClick={() => handleDelete(record.id)}
        />
      ),
    },
  ];

  return (
    <PageContainer>
      <Card>
        <Alert
          style={{ marginBottom: 16 }}
          type="info"
          showIcon
          message="业务模块数据权限示例"
          description="本页数据来自 AbpAdmin.Biz.Template 模块（后端零过滤代码，可见性由角色的数据范围决定）。要体验不同范围的过滤效果：到「身份管理 → 角色 → 数据权限」调整当前账号所属角色的数据范围，再回本页刷新。"
        />
        <div
          style={{
            marginBottom: 16,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: 8,
          }}
        >
          <Space>
            <span>当前可见数据条数：</span>
            <Tag color="green" style={{ fontSize: 16 }}>
              {total}
            </Tag>
          </Space>
          <Space>
            <Button key="refresh" icon={<ReloadOutlined />} onClick={fetchData}>
              刷新
            </Button>
            <Button
              key="create"
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => setCreateModalOpen(true)}
            >
              新建
            </Button>
          </Space>
        </div>
        <Table
          rowKey="id"
          loading={loading}
          columns={columns}
          dataSource={data}
          pagination={false}
        />
      </Card>

      <Modal
        title="新建示例数据"
        open={createModalOpen}
        onCancel={() => setCreateModalOpen(false)}
        onOk={() => form.submit()}
      >
        <Form form={form} layout="vertical" onFinish={handleCreate}>
          <Form.Item
            name="name"
            label="名称"
            rules={[{ required: true, message: '请输入名称' }]}
          >
            <Input placeholder="请输入名称" />
          </Form.Item>
          <Form.Item
            name="organizationUnitId"
            label="所属组织（留空表示按当前数据范围自动归属）"
          >
            <Select
              allowClear
              placeholder="选择组织单元"
              options={ouList.map((ou) => ({
                label: ou.displayName,
                value: ou.id,
              }))}
            />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  );
};

export default BizDataScopeDemoPage;
