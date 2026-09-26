import {
  DeleteOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  App,
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
  deleteApiAppDataScopeDemoId,
  getApiAppDataScopeDemo,
  postApiAppDataScopeDemo,
} from '@/services/abpadmin/dataScopeDemo';

type DataScopeDemoItem = {
  id: string;
  name: string;
  organizationUnitId?: string;
  organizationUnitName?: string;
  creationTime: string;
  creatorId?: string;
};

type OrganizationUnitItem = {
  id: string;
  displayName: string;
  code: string;
};

const DataScopeDemoPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<DataScopeDemoItem[]>([]);
  const [total, setTotal] = useState(0);
  const [ouList, setOuList] = useState<OrganizationUnitItem[]>([]);
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [form] = Form.useForm();

  const fetchData = async () => {
    setLoading(true);
    try {
      const res = await getApiAppDataScopeDemo({
        SkipCount: 0,
        MaxResultCount: 100,
      });
      setData((res.items || []) as DataScopeDemoItem[]);
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
    } catch (e) {
      // ignore
    }
  };

  useEffect(() => {
    fetchData();
    fetchOuList();
  }, []);

  const handleCreate = async (values: any) => {
    try {
      await postApiAppDataScopeDemo({
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
          await deleteApiAppDataScopeDemoId({ id });
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
      render: (_: any, record: DataScopeDemoItem) => (
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
          {/* 页头标题行已全局隐藏，操作按钮挪到列表卡片工具行 */}
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
        title="新建演示数据"
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
            label="所属组织（留空表示无组织）"
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

export default DataScopeDemoPage;
