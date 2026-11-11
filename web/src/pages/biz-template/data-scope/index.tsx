import {
  DeleteOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Tag,
} from 'antd';
import React, { useEffect, useState } from 'react';
import {
  type BizDataScopeDemoDto,
  createBizDataScopeDemo,
  deleteBizDataScopeDemo,
  getBizDataScopeDemos,
} from '@/abp/bizDataScopeDemo';
import { getAllRoles, type IdentityRoleDto } from '@/abp/identity';
import { getOrganizationUnits } from '@/abp/identityAdmin';
import { ResizableTable } from '@/components/AutoHeightProTable/useResizableColumns';
import DataScopeModal from '@/components/DataScopeModal';

type OrganizationUnitItem = {
  id: string;
  displayName: string;
  code: string;
};

/**
 * 业务数据范围配置与 Biz.Template 示例，列表展示当前用户范围内的数据。
 */
const BizDataScopeDemoPage: React.FC = () => {
  const access = useAccess();
  const { message, modal } = App.useApp();
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [scopeRoleName, setScopeRoleName] = useState<string>();
  const [scopeOpen, setScopeOpen] = useState(false);
  const canConfigureScope =
    access.canManageRoleDataScopes && access.canManageRoles;
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<BizDataScopeDemoDto[]>([]);
  const [total, setTotal] = useState(0);
  const [ouList, setOuList] = useState<OrganizationUnitItem[]>([]);
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [form] = Form.useForm();

  const fetchData = async () => {
    if (!access.canManageBizDataScopeDemo) return;
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
    } catch {
      // ignore
    }
  };

  useEffect(() => {
    if (!access.canManageBizDataScopeDemo) return;
    fetchData();
    fetchOuList();
  }, [access.canManageBizDataScopeDemo]);

  useEffect(() => {
    if (!canConfigureScope) return;
    let cancelled = false;
    getAllRoles()
      .then((result) => {
        if (!cancelled) {
          setRoles([
            ...new Map(result.items.map((role) => [role.name, role])).values(),
          ]);
        }
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [canConfigureScope]);

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

  // 组织名映射依赖框架组织单元接口（getOrganizationUnits）的返回；受限角色可能未授该接口
  // 权限（fetchOuList 静默失败，ouList 为空），此时回退显示原始 GUID——数据可见性不受影响，
  // 仅该列展示降级。
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
      width: 200,
      render: (ouId: string) => getOuName(ouId),
    },
    {
      title: '创建时间',
      dataIndex: 'creationTime',
      key: 'creationTime',
      width: 180,
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
          title="业务数据范围扩展示例"
          description="数据范围只影响本示例等已接入的业务数据，不是创建角色的必配项。日常功能授权请到角色页配置。"
        />
        {canConfigureScope && (
          <Space wrap style={{ marginBottom: 16 }}>
            <Select
              aria-label="示例角色"
              placeholder="选择要配置的角色"
              value={scopeRoleName}
              onChange={setScopeRoleName}
              showSearch={{ optionFilterProp: 'label' }}
              options={roles.map((role) => ({
                label: role.name,
                value: role.name,
              }))}
              style={{ minWidth: 220 }}
            />
            <Button
              disabled={!scopeRoleName}
              onClick={() => setScopeOpen(true)}
            >
              配置业务数据范围
            </Button>
          </Space>
        )}
        {access.canManageBizDataScopeDemo && (
          <>
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
                <Button
                  key="refresh"
                  icon={<ReloadOutlined />}
                  onClick={fetchData}
                >
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
            <ResizableTable
              rowKey="id"
              loading={loading}
              columns={columns}
              dataSource={data}
              pagination={false}
            />
          </>
        )}
      </Card>

      <Modal
        title="新建示例数据"
        open={createModalOpen && access.canManageBizDataScopeDemo}
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
      <DataScopeModal
        open={scopeOpen && canConfigureScope}
        title={`业务数据范围 - ${scopeRoleName || ''}`}
        roleName={scopeRoleName}
        onClose={() => setScopeOpen(false)}
      />
    </PageContainer>
  );
};

export default BizDataScopeDemoPage;
