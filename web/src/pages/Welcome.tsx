import {
  AuditOutlined,
  BankOutlined,
  DashboardOutlined,
  MoneyCollectOutlined,
  SettingOutlined,
  TeamOutlined,
  ThunderboltOutlined,
  WalletOutlined,
} from '@ant-design/icons';
import { PageContainer, ProCard } from '@ant-design/pro-components';
import { getAuditLogAverageDuration, getAuditLogErrorRate } from '@/abp/proModules';
import { history, useAccess, useModel } from '@umijs/max';
import { Card, Col, Empty, Progress, Row, Statistic, Typography } from 'antd';
import React, { useEffect, useState } from 'react';

/* 框架工作台（/welcome，菜单「首页」的落地页）。
 * 脚手架时代的 Cheatsheet 演示页已移除——基础框架的首页应是权限感知的运营入口：
 * 按新菜单序给高频入口（无权限的自动隐藏，全部无权时给空态引导），右侧是框架级
 * 健康面（审计耗时/错误率，复用既有 API，零新依赖）。入口组序与 MenuTemplateDefinition
 * 的顶层组序保持同构（收付款 → 财务会计 → 管理 → SaaS）。 */

const { Text } = Typography;

interface WorkbenchEntry {
  title: string;
  desc: string;
  path: string;
  icon: React.ReactNode;
  access?: boolean;
}

const WorkbenchPage: React.FC = () => {
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const user = initialState?.currentUser;
  const tenant = initialState?.currentTenant;
  const [avgDuration, setAvgDuration] = useState<number>();
  const [errorRate, setErrorRate] = useState<number>();
  const [healthLoading, setHealthLoading] = useState(true);

  useEffect(() => {
    // 审计统计接口需要审计权限；无权账号直接隐藏健康卡，不发起注定 403 的请求
    if (!access.canManageAuditLogs) {
      setHealthLoading(false);
      return;
    }
    let alive = true;
    setHealthLoading(true);
    Promise.all([
      getAuditLogAverageDuration().catch(() => undefined),
      getAuditLogErrorRate().catch(() => undefined),
    ]).then(([duration, rate]) => {
      if (!alive) return;
      if (Array.isArray(duration) && duration.length > 0) {
        const avg =
          duration.reduce(
            (sum: number, d: { avgExecutionDuration?: number }) =>
              sum + (d.avgExecutionDuration ?? 0),
            0,
          ) / duration.length;
        setAvgDuration(Math.round(avg));
      }
      setErrorRate((rate as { errorRate?: number } | undefined)?.errorRate);
      setHealthLoading(false);
    });
    return () => {
      alive = false;
    };
  }, [access.canManageAuditLogs]);

  const groups: Array<{ group: string; entries: WorkbenchEntry[] }> = [
    {
      group: '收付款',
      entries: [
        {
          title: '资金看板',
          desc: '充值/提现/负债/金库总览',
          path: '/collection/dashboard',
          icon: <DashboardOutlined />,
          access: access.canViewCollections,
        },
        {
          title: '充值单',
          desc: '入金流水与大额放行',
          path: '/collection/deposits',
          icon: <WalletOutlined />,
          access: access.canViewCollections,
        },
        {
          title: '提现管理',
          desc: '出金审核与广播',
          path: '/collection/withdrawals',
          icon: <MoneyCollectOutlined />,
          access: access.canAdminWithdrawals,
        },
      ],
    },
    {
      group: '财务会计',
      entries: [
        {
          title: '资金总览',
          desc: '科目、凭证与余额投影',
          path: '/finance/overview',
          icon: <BankOutlined />,
          access: access.canViewFinance,
        },
      ],
    },
    {
      group: '管理',
      entries: [
        {
          title: '用户',
          desc: '账号、角色与组织',
          path: '/administration/identity/users',
          icon: <TeamOutlined />,
          access: access.canManageUsers,
        },
        {
          title: '审计日志',
          desc: '操作留痕与错误处置',
          path: '/administration/audit-logs',
          icon: <AuditOutlined />,
          access: access.canManageAuditLogs,
        },
        {
          title: '设置管理',
          desc: '参数与功能配置',
          path: '/administration/settings',
          icon: <SettingOutlined />,
          access: access.canManageSettings,
        },
      ],
    },
    {
      group: 'SaaS',
      entries: [
        {
          title: '租户',
          desc: '多租户与套餐',
          path: '/administration/saas/tenants',
          icon: <ThunderboltOutlined />,
          access: access.canManageTenants,
        },
      ],
    },
  ];

  const visibleGroups = groups
    .map((g) => ({ ...g, entries: g.entries.filter((e) => e.access !== false) }))
    .filter((g) => g.entries.length > 0);

  return (
    <PageContainer title={false} subTitle={false}>
      <ProCard gutter={[16, 16]} ghost style={{ minHeight: '60vh' }}>
        <ProCard colSpan={{ xs: 24, lg: 16 }} direction="column" gutter={[16, 16]}>
          <Card>
            <div className="flex flex-col gap-1">
              <Text strong style={{ fontSize: 18 }}>
                你好，{user?.name || user?.userName || '管理员'}
              </Text>
              <Text type="secondary">
                欢迎使用 AbpAdmin 基础框架 · {new Date().toLocaleDateString()} · 当前环境：
                {tenant?.isAvailable ? `租户 ${tenant.name ?? ''}` : 'Host'}
              </Text>
            </div>
          </Card>
          {visibleGroups.length === 0 ? (
            <Card>
              <Empty description="当前账号暂无可用的功能入口，请联系管理员分配角色权限" />
            </Card>
          ) : (
            visibleGroups.map((g) => (
              <Card key={g.group} title={g.group} size="small">
                <Row gutter={[12, 12]}>
                  {g.entries.map((e) => (
                    <Col key={e.path} xs={24} sm={12} md={8}>
                      <Card hoverable size="small" onClick={() => history.push(e.path)}>
                        <div className="flex items-start gap-3">
                          <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-[#1677ff]/10 text-lg text-[#1677ff]">
                            {e.icon}
                          </div>
                          <div className="min-w-0 flex-1">
                            <h4 className="mb-1 mt-0 text-sm font-semibold">{e.title}</h4>
                            <p className="mb-0 truncate text-xs text-zinc-500">{e.desc}</p>
                          </div>
                        </div>
                      </Card>
                    </Col>
                  ))}
                </Row>
              </Card>
            ))
          )}
        </ProCard>
        <ProCard colSpan={{ xs: 24, lg: 8 }} direction="column" gutter={[16, 16]}>
          {access.canManageAuditLogs && (
            <Card title="框架健康" size="small" loading={healthLoading}>
              <Row gutter={[12, 24]}>
                <Col span={12}>
                  <Statistic title="接口平均耗时" value={avgDuration ?? 0} suffix="ms" />
                </Col>
                <Col span={12}>
                  <Statistic
                    title="近期错误率"
                    value={errorRate ?? 0}
                    precision={errorRate != null && errorRate < 10 ? 2 : 0}
                    suffix="%"
                  />
                </Col>
              </Row>
            </Card>
          )}
          <Card size="small">
            <Progress type="circle" percent={100} size={72} format={() => '运行中'} />
            <div className="mt-3 text-xs text-zinc-500">
              后台作业、链上 worker 与通知中心在「系统」菜单下监控。
            </div>
          </Card>
        </ProCard>
      </ProCard>
    </PageContainer>
  );
};

export default WorkbenchPage;
