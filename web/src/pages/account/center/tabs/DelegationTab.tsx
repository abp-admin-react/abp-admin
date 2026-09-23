import { App, Button, Select, Space, Table, Typography } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import {
  createDelegation,
  deleteDelegation,
  getDelegatedToMe,
  getDelegatedToOthers,
  type IdentityUserDelegationDto,
  startDelegation,
} from '@/abp/accountSecurity';
import { getUsers } from '@/abp/identity';
import { applyImpersonatedTokens } from '@/abp/oidc';
import { useAsyncData } from '@/hooks/useAsyncData';

/**
 * 默认委托时长（天），与后端 AbpAdminAccountConsts.DefaultDelegationDays 对齐
 * （重构报告问题 22/31：原先 7 * 24 * 60 * 60 * 1000 魔法数字散落）。
 */
const DEFAULT_DELEGATION_DAYS = 7;
const DEFAULT_DELEGATION_MS = DEFAULT_DELEGATION_DAYS * 24 * 60 * 60 * 1000;

/** 权限委托 Tab：委托管理（自 index.tsx 拆分，见重构报告问题 4） */
const DelegationTab: React.FC = () => {
  const { message } = App.useApp();
  const { data, loading, refresh } = useAsyncData(async () => {
    const [mine, toMe] = await Promise.all([
      getDelegatedToOthers(),
      getDelegatedToMe(),
    ]);
    return { mine, toMe };
  });
  const [targetUserId, setTargetUserId] = useState<string>();

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Title level={5}>委托给别人</Typography.Title>
      <Space wrap>
        <Button
          type="primary"
          onClick={async () => {
            if (!targetUserId) {
              message.warning('请选择目标用户');
              return;
            }
            const start = new Date();
            const end = new Date(start.getTime() + DEFAULT_DELEGATION_MS);
            await createDelegation({
              targetUserId,
              startTime: start.toISOString(),
              endTime: end.toISOString(),
            });
            message.success(`已创建 ${DEFAULT_DELEGATION_DAYS} 天委托`);
            await refresh();
          }}
        >
          委托 {DEFAULT_DELEGATION_DAYS} 天
        </Button>
        <UserPicker value={targetUserId} onChange={setTargetUserId} />
      </Space>
      <Table
        size="small"
        loading={loading}
        rowKey="id"
        dataSource={data?.mine ?? []}
        pagination={false}
        columns={[
          { title: '目标用户', dataIndex: 'targetUserName' },
          {
            title: '开始',
            dataIndex: 'startTime',
            render: (v: string) => new Date(v).toLocaleString(),
          },
          {
            title: '结束',
            dataIndex: 'endTime',
            render: (v: string) => new Date(v).toLocaleString(),
          },
          {
            title: '操作',
            render: (_, record: IdentityUserDelegationDto) => (
              <Button
                type="link"
                danger
                size="small"
                onClick={async () => {
                  await deleteDelegation(record.id);
                  message.success('已删除');
                  await refresh();
                }}
              >
                删除
              </Button>
            ),
          },
        ]}
      />
      <Typography.Title level={5}>别人委托给我</Typography.Title>
      <Table
        size="small"
        loading={loading}
        rowKey="id"
        dataSource={data?.toMe ?? []}
        pagination={false}
        columns={[
          { title: '来源用户', dataIndex: 'sourceUserName' },
          {
            title: '结束',
            dataIndex: 'endTime',
            render: (v: string) => new Date(v).toLocaleString(),
          },
          {
            title: '操作',
            render: (_, record: IdentityUserDelegationDto) =>
              record.isActive ? (
                <Button
                  type="link"
                  size="small"
                  onClick={async () => {
                    const tokens = await startDelegation(record.id);
                    await applyImpersonatedTokens(tokens);
                    message.success('已进入委托身份，即将刷新');
                    window.location.reload();
                  }}
                >
                  进入委托
                </Button>
              ) : null,
          },
        ]}
      />
    </Space>
  );
};

/**
 * 委托目标选择器（重构报告问题 30：原先 Input.Search 搜索后自动选中第一个结果，
 * 用户未做选择就被设为委托目标，误委托风险；现改为 Select 点选，选择权在用户）。
 * 搜索防抖 300ms + 响应序号：连续输入只发最后一次，慢的旧响应直接丢弃
 * （否则旧结果覆盖新结果，候选列表与输入不匹配同样有误选风险）。
 */
const SEARCH_DEBOUNCE_MS = 300;

const UserPicker: React.FC<{
  value?: string;
  onChange: (id?: string) => void;
}> = ({ value, onChange }) => {
  const [options, setOptions] = useState<{ label: string; value: string }[]>(
    [],
  );
  const debounceRef = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );
  const searchSeqRef = useRef(0);

  // 卸载时清掉在途定时器，防止 setState 到已卸载组件
  useEffect(() => () => clearTimeout(debounceRef.current), []);

  const handleSearch = (keyword: string) => {
    clearTimeout(debounceRef.current);
    const seq = ++searchSeqRef.current;
    debounceRef.current = setTimeout(async () => {
      try {
        const result = await getUsers({
          current: 1,
          pageSize: 10,
          filter: keyword,
        });
        if (seq !== searchSeqRef.current) return; // 已有更新的搜索，丢弃过期响应
        setOptions(
          (result.items ?? []).map((u) => ({
            label: `${u.userName} (${u.email})`,
            value: u.id,
          })),
        );
      } catch {
        // 错误由全局 errorHandler 统一提示
      }
    }, SEARCH_DEBOUNCE_MS);
  };

  return (
    <Select
      showSearch
      placeholder="搜索并选择被委托人"
      style={{ width: 280 }}
      value={value}
      filterOption={false}
      options={options}
      onSearch={handleSearch}
      onChange={(v) => onChange(v)}
      allowClear
    />
  );
};

export default DelegationTab;
