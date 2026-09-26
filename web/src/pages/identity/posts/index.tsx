import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormDigit,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  App,
  Button,
  Drawer,
  Input,
  Popconfirm,
  Select,
  Space,
  Tag,
} from 'antd';
import React, { useRef, useState } from 'react';
import { getUsers } from '@/abp/identity';
import {
  addPostMembers,
  createPost,
  deletePost,
  getPostMembers,
  getPosts,
  type PostDto,
  removePostMember,
  updatePost,
} from '@/abp/proModules';
import AutoHeightProTable from '@/components/AutoHeightProTable';

type PostFormValues = {
  name: string;
  code: string;
  sortOrder: number;
  status: number;
  remark?: string;
};

const statusOptions = [
  { label: '启用', value: 0 },
  { label: '停用', value: 1 },
];

const PostsPage: React.FC = () => {
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const { message } = App.useApp();
  const [memberTarget, setMemberTarget] = useState<PostDto>();

  const columns: ProColumns<PostDto>[] = [
    { title: '岗位名称', dataIndex: 'name' },
    { title: '岗位编码', dataIndex: 'code' },
    { title: '排序', dataIndex: 'sortOrder', width: 80 },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_, record) =>
        record.status === 0 ? <Tag color="success">启用</Tag> : <Tag>停用</Tag>,
    },
    { title: '成员数', dataIndex: 'memberCount', width: 90 },
    {
      title: '备注',
      dataIndex: 'remark',
      ellipsis: true,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 180,
      render: (_, record) => [
        access.canUpdatePosts ? (
          <ModalForm
            key="edit"
            title="编辑岗位"
            trigger={<a>编辑</a>}
            initialValues={record}
            modalProps={{ destroyOnHidden: true }}
            onFinish={async (values) => {
              await updatePost(record.id, values as PostFormValues);
              message.success('已更新');
              actionRef.current?.reload();
              return true;
            }}
          >
            <PostFormFields />
          </ModalForm>
        ) : null,
        access.canManagePostMembers ? (
          <a key="members" onClick={() => setMemberTarget(record)}>
            成员
          </a>
        ) : null,
        access.canDeletePosts ? (
          <Popconfirm
            key="delete"
            title={`确认删除岗位「${record.name}」？`}
            description="已关联的用户将同时解除关联。"
            onConfirm={async () => {
              await deletePost(record.id);
              message.success('已删除');
              actionRef.current?.reload();
            }}
          >
            <a>删除</a>
          </Popconfirm>
        ) : null,
      ],
    },
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<PostDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={false}
        request={async (params) => {
          const result = await getPosts({
            current: params.current,
            pageSize: params.pageSize,
            filter: params.name || params.code || undefined,
            status: params.status,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() =>
          access.canCreatePosts
            ? [
                <ModalForm<PostFormValues>
                  key="create"
                  title="新建岗位"
                  trigger={<Button type="primary">新建</Button>}
                  modalProps={{ destroyOnHidden: true }}
                  onFinish={async (values) => {
                    await createPost(values);
                    message.success('已创建');
                    actionRef.current?.reload();
                    return true;
                  }}
                >
                  <PostFormFields />
                </ModalForm>,
              ]
            : []
        }
      />
      {memberTarget && (
        <PostMembersDrawer
          post={memberTarget}
          onClose={() => {
            setMemberTarget(undefined);
            actionRef.current?.reload();
          }}
        />
      )}
    </PageContainer>
  );
};

const PostFormFields: React.FC = () => (
  <>
    <ProFormText
      name="name"
      label="岗位名称"
      rules={[{ required: true, max: 64 }]}
      placeholder="如：人力资源经理"
    />
    <ProFormText
      name="code"
      label="岗位编码"
      rules={[{ required: true, max: 64 }]}
      placeholder="如：HR"
    />
    <ProFormDigit
      name="sortOrder"
      label="显示顺序"
      min={0}
      fieldProps={{ precision: 0 }}
    />
    <ProFormSelect
      name="status"
      label="状态"
      options={statusOptions}
      initialValue={0}
    />
    <ProFormTextArea
      name="remark"
      label="备注"
      fieldProps={{ maxLength: 512, showCount: true }}
    />
  </>
);

type MemberItem = {
  id: string;
  userName: string;
  name?: string;
  email?: string;
  isActive: boolean;
};

const PostMembersDrawer: React.FC<{
  post: PostDto;
  onClose: () => void;
}> = ({ post, onClose }) => {
  const actionRef = useRef<ActionType>(undefined);
  const { message } = App.useApp();
  const [filter, setFilter] = useState<string>();
  const [picking, setPicking] = useState(false);
  const [userOptions, setUserOptions] = useState<
    { label: string; value: string }[]
  >([]);
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>([]);

  const searchUsers = async (keyword: string) => {
    const result = await getUsers({
      filter: keyword || undefined,
      pageSize: 20,
    });
    setUserOptions(
      result.items.map((user) => ({
        label: user.name ? `${user.userName}（${user.name}）` : user.userName,
        value: user.id,
      })),
    );
  };

  const columns: ProColumns<MemberItem>[] = [
    { title: '用户名', dataIndex: 'userName' },
    { title: '姓名', dataIndex: 'name' },
    { title: '邮箱', dataIndex: 'email', ellipsis: true },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 80,
      render: (_, record) =>
        record.isActive ? <Tag color="success">启用</Tag> : <Tag>禁用</Tag>,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 80,
      render: (_, record) => [
        <Popconfirm
          key="remove"
          title="确认移除该成员？"
          onConfirm={async () => {
            await removePostMember(post.id, record.id);
            message.success('已移除');
            actionRef.current?.reload();
          }}
        >
          <a>移除</a>
        </Popconfirm>,
      ],
    },
  ];

  return (
    <Drawer
      title={`岗位成员 - ${post.name}`}
      size={720}
      open
      onClose={onClose}
      destroyOnHidden
    >
      {picking ? (
        <Space.Compact style={{ width: '100%', marginBottom: 16 }} block>
          <Select
            mode="multiple"
            showSearch
            placeholder="输入用户名/姓名/邮箱搜索"
            filterOption={false}
            style={{ width: '100%' }}
            onSearch={searchUsers}
            options={userOptions}
            value={selectedUserIds}
            onChange={setSelectedUserIds}
            onDropdownVisibleChange={(open) => {
              if (open && userOptions.length === 0) {
                searchUsers('');
              }
            }}
          />
          <Button
            type="primary"
            disabled={selectedUserIds.length === 0}
            onClick={async () => {
              await addPostMembers(post.id, selectedUserIds);
              message.success('已添加');
              setSelectedUserIds([]);
              setPicking(false);
              actionRef.current?.reload();
            }}
          >
            添加
          </Button>
        </Space.Compact>
      ) : (
        <Button
          type="primary"
          style={{ marginBottom: 16 }}
          onClick={() => {
            setPicking(true);
            searchUsers('');
          }}
        >
          添加成员
        </Button>
      )}
      <Input.Search
        placeholder="按用户名/姓名/邮箱过滤"
        allowClear
        style={{ width: 280, marginBottom: 16, display: 'block' }}
        onSearch={(value) => {
          setFilter(value || undefined);
          actionRef.current?.reload();
        }}
      />
      <AutoHeightProTable<MemberItem>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={false}
        /* 抽屉内嵌套布局，不撑满视口 */
        fillViewport={false}
        pagination={{ pageSize: 10 }}
        params={{ filter }}
        request={async (params) => {
          const result = await getPostMembers(post.id, {
            current: params.current,
            pageSize: params.pageSize,
            filter: params.filter,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
      />
    </Drawer>
  );
};

export default PostsPage;
