import {
  type ActionType,
  ModalForm,
  PageContainer,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Button, Popconfirm, Switch, Tag } from 'antd';
import React, { useRef, useState } from 'react';
import { Access, useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  type FinAccountDto,
  createAccount,
  deleteAccount,
  financeErrorText,
  FIN_ACCOUNT_WRITE_ENABLED,
  canDeleteAccount,
  getAccounts,
  updateAccount,
} from '@/abp/finance';

const AccountsPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  // v1 read-only: create/update/delete gated by FIN_ACCOUNT_WRITE_ENABLED (finance.ts)
  const canWrite = FIN_ACCOUNT_WRITE_ENABLED && !!access.canManageFinanceAccounts;
  // Live DTO: isSystem / isActive / hasEntries drive button visibility when write is re-enabled.
  const actionRef = useRef<ActionType>(undefined);
  const load = useFinanceLoad();
  const [editing, setEditing] = useState<FinAccountDto | null>(null);
  const [open, setOpen] = useState(false);

  return (
    <PageContainer
      title="A2 科目表"
      extra={
        !load.ok ? null : (
        <Access accessible={canWrite}>
          <Button
            type="primary"
            onClick={() => {
              setEditing(null);
              setOpen(true);
            }}
          >
            新建科目
          </Button>
        </Access>
        )
      }
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <AutoHeightProTable<FinAccountDto>
        key={load.nonce}
        rowKey="id"
        locale={{ emptyText: <FinanceEmpty text={'暂无科目'} /> }}
        actionRef={actionRef}
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          const maxResultCount = params.pageSize ?? 20;
          const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
          const result = await load.guard(() =>
            getAccounts({
            skipCount,
            maxResultCount,
            filter: (params as { keyword?: string }).keyword || params.code || params.name,
          }),
          );
          if (!result) return { data: [], total: 0, success: false };
          return { data: result.items, total: result.totalCount, success: true };
        }}
        columns={[
          { title: '编码', dataIndex: 'code', width: 100 },
          { title: '名称', dataIndex: 'name', width: 160 },
          {
            title: '类型',
            dataIndex: 'type',
            width: 100,
            valueEnum: {
              Asset: { text: '资产' },
              Liability: { text: '负债' },
              Equity: { text: '权益' },
              Income: { text: '收入' },
              Expense: { text: '费用' },
            },
          },
          { title: '方向', dataIndex: 'normalSide', width: 80, search: false },
          {
            title: '系统',
            dataIndex: 'isSystem',
            width: 80,
            search: false,
            render: (_, r) => (r.isSystem ? <Tag>系统</Tag> : '-'),
          },
          {
            title: '启用',
            dataIndex: 'enabled',
            width: 80,
            search: false,
            render: (_, r) =>
              // 无 Account.Manage 或系统科目：只读展示，不出禁用态开关（v1 §1.1）
              !canWrite || r.isSystem ? (
                <Tag color={(r.enabled ?? r.isActive) ? 'success' : 'default'}>{(r.enabled ?? r.isActive) ? '启用' : '停用'}</Tag>
              ) : (
              <Switch
                checked={r.enabled ?? !!r.isActive}
                onChange={async (v) => {
                  try {
                    await updateAccount(r.id, { enabled: v });
                    message.success('已更新');
                    actionRef.current?.reload();
                  } catch (e) {
                    message.error(financeErrorText(e));
                  }
                }}
              />
            ),
          },
          {
            title: '操作',
            valueType: 'option',
            width: 160,
            render: (_, r) => {
              if (!canWrite) return [];
              // future rule: system accounts are read-only
              if (r.isSystem) return [];
              const acts = [
                <a
                  key="edit"
                  onClick={() => {
                    setEditing(r);
                    setOpen(true);
                  }}
                >
                  编辑
                </a>,
              ];
              if (canDeleteAccount(r)) {
                acts.push(
                  <Popconfirm
                    key="del"
                    title="确认删除该科目？"
                    onConfirm={async () => {
                      try {
                        await deleteAccount(r.id);
                        message.success('已删除');
                        actionRef.current?.reload();
                      } catch (e) {
                        message.error(financeErrorText(e));
                      }
                    }}
                  >
                    <a>删除</a>
                  </Popconfirm>,
                );
              }
              return acts;
            },
          },
        ]}
      />
      <ModalForm
        title={editing ? '编辑科目' : '新建科目'}
        open={open}
        onOpenChange={setOpen}
        initialValues={editing ?? { type: 'Asset', normalSide: 'Debit' }}
        modalProps={{ destroyOnHidden: true }}
        onFinish={async (values) => {
          try {
            if (editing) {
              await updateAccount(editing.id, values);
              message.success('已保存');
            } else {
              await createAccount(values);
              message.success('已创建');
            }
            actionRef.current?.reload();
            return true;
          } catch (e) {
            message.error(financeErrorText(e));
            return false;
          }
        }}
      >
        <ProFormText
          name="code"
          label="编码"
          rules={[{ required: true }]}
          disabled={!!editing?.isSystem}
        />
        <ProFormText name="name" label="名称" rules={[{ required: true }]} />
        <ProFormSelect
          name="type"
          label="类型"
          options={[
            { value: 'Asset', label: '资产' },
            { value: 'Liability', label: '负债' },
            { value: 'Equity', label: '权益' },
            { value: 'Income', label: '收入' },
            { value: 'Expense', label: '费用' },
          ]}
          disabled={!!editing?.isSystem}
        />
        <ProFormSelect
          name="normalSide"
          label="方向"
          options={[
            { value: 'Debit', label: '借' },
            { value: 'Credit', label: '贷' },
          ]}
          disabled={!!editing?.isSystem}
        />
      </ModalForm>
      </FinanceStateView>
    </PageContainer>
  );
};

export default AccountsPage;
