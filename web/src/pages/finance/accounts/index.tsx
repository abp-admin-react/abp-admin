import { financeHelpHeader } from '@/components/FinanceHelp';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProFormInstance,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Button, Popconfirm, Switch, Tag, Tooltip } from 'antd';
import React, { useRef, useState } from 'react';
import { useAccess } from '@umijs/max';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import {
  type AccountOp,
  type FinAccountDto,
  type FinAccountInput,
  accountErrorText,
  accountInvalidFields,
  canDeleteAccount,
  canEditAccount,
  createAccount,
  deleteAccount,
  getAccounts,
  setAccountActive,
  updateAccount,
} from '@/abp/finance';

const TYPE_OPTIONS = [
  { value: 'Asset', label: '资产' },
  { value: 'Liability', label: '负债' },
  { value: 'Equity', label: '权益' },
  { value: 'Income', label: '收入' },
  { value: 'Expense', label: '费用' },
];
const SIDE_OPTIONS = [
  { value: 'Debit', label: '借' },
  { value: 'Credit', label: '贷' },
];

const AccountsPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  // 写按钮只看 Finance.Account.Manage；GET 列表 Ledger.View 也能看。后端写接口同样要 Manage。
  const canWrite = !!access.canManageFinanceAccounts;
  const actionRef = useRef<ActionType>(undefined);
  const formRef = useRef<ProFormInstance>(undefined);
  const load = useFinanceLoad();
  const [editing, setEditing] = useState<FinAccountDto | null>(null);
  const [open, setOpen] = useState(false);

  /** 统一报错：InvalidEvent 在对应输入框下标红「请填写此项」，其它走中文 toast，不露错误码 */
  const showError = (e: unknown, op: AccountOp, selfId?: string) => {
    const fields = accountInvalidFields(e);
    if (fields.length && formRef.current) {
      formRef.current.setFields(fields.map((name) => ({ name, errors: ['请填写此项'] })));
      return;
    }
    message.error(accountErrorText(e, op, selfId) ?? '请填写必填项');
  };

  return (
    <PageContainer
      {...financeHelpHeader('A2', '科目表')}
    >
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
        <AutoHeightProTable<FinAccountDto>
          key={load.nonce}
          rowKey="id"
          locale={{ emptyText: <FinanceEmpty text={'暂无科目'} /> }}
          actionRef={actionRef}
          search={{ labelWidth: 'auto' }}
          // 新建放表格工具栏：PageContainer header 在部分布局下不渲染，按钮会丢
          toolBarRender={() =>
            canWrite
              ? [
                  <Button
                    key="create"
                    type="primary"
                    onClick={() => {
                      setEditing(null);
                      setOpen(true);
                    }}
                  >
                    新建科目
                  </Button>,
                ]
              : []
          }
          request={async (params) => {
            const maxResultCount = params.pageSize ?? 20;
            const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
            // 后端列表按 code/name 分别模糊查（FinAccountListInput），不认 filter
            const result = await load.guard(() =>
              getAccounts({ skipCount, maxResultCount, code: params.code, name: params.name } as never),
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
              search: false,
              valueEnum: Object.fromEntries(TYPE_OPTIONS.map((o) => [o.value, { text: o.label }])),
            },
            {
              title: '方向',
              dataIndex: 'normalSide',
              width: 80,
              search: false,
              render: (_, r) => (r.normalSide === 'Credit' ? '贷' : '借'),
            },
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
                // 无 Manage 或系统科目：只读标签（系统科目停用/启用后端也拒 SystemReadOnly）
                !canWrite || r.isSystem ? (
                  <Tag color={r.enabled ? 'success' : 'default'}>{r.enabled ? '启用' : '停用'}</Tag>
                ) : (
                  <Switch
                    checked={!!r.enabled}
                    checkedChildren="启用"
                    unCheckedChildren="停用"
                    onChange={async (v) => {
                      try {
                        await setAccountActive(r.id, v);
                        message.success(v ? '已启用' : '已停用');
                        actionRef.current?.reload();
                      } catch (e) {
                        showError(e, 'toggle');
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
                if (!canEditAccount(r)) {
                  return [<Tooltip key="ro" title="系统科目不能修改或删除"><span style={{ color: '#999' }}>只读</span></Tooltip>];
                }
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
                acts.push(
                  canDeleteAccount(r) ? (
                    <Popconfirm
                      key="del"
                      title={`确认删除科目 ${r.code}？`}
                      onConfirm={async () => {
                        try {
                          await deleteAccount(r.id);
                          message.success('已删除');
                          actionRef.current?.reload();
                        } catch (e) {
                          showError(e, 'delete');
                        }
                      }}
                    >
                      <a>删除</a>
                    </Popconfirm>
                  ) : (
                    <Tooltip key="del" title="已有记账记录，不能删除，可以停用">
                      <span style={{ color: '#999' }}>删除</span>
                    </Tooltip>
                  ),
                );
                return acts;
              },
            },
          ]}
        />
        <ModalForm<FinAccountInput>
          title={editing ? '编辑科目' : '新建科目'}
          open={open}
          onOpenChange={setOpen}
          formRef={formRef}
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
              showError(e, editing ? 'update' : 'create', editing?.id);
              return false;
            }
          }}
        >
          {/* 必填校验先放后端：缺项时 InvalidEvent + data.Field 回标，保证前后端口径同一处 */}
          <ProFormText name="code" label="编码" disabled={!!editing?.hasEntries} />
          <ProFormText name="name" label="名称" />
          {/* 有分录：类型/方向锁定（后端 HasEntries） */}
          <ProFormSelect name="type" label="类型" options={TYPE_OPTIONS} disabled={!!editing?.hasEntries} />
          <ProFormSelect name="normalSide" label="方向" options={SIDE_OPTIONS} disabled={!!editing?.hasEntries} />
          <ProFormSelect
            name="parentId"
            label="上级科目（可空）"
            allowClear
            showSearch
            request={async () => {
              const res = await getAccounts({ maxResultCount: 1000 });
              return res.items.map((a) => ({ value: a.id, label: `${a.code} ${a.name}` }));
            }}
          />
        </ModalForm>
      </FinanceStateView>
    </PageContainer>
  );
};

export default AccountsPage;
