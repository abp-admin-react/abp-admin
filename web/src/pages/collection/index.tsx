import { PageContainer } from '@ant-design/pro-components';
import { Card, Timeline } from 'antd';
import React from 'react';

/**
 * 收款页（/collection，计划第 5.7 节）：收款单、余额流水、渠道设置、链上监控。
 * M1 骨架——这些面板随里程碑落地（M2 余额与流水、M3 微信/支付宝、M4 Stripe、M5 链上），
 * 每块就绪后在此页接入对应端点（镜像见 src/abp/collection.ts）。
 * 本页与订单页都只给 Host 用户，后端应用服务有 Host 守卫。
 */
export default function CollectionPage() {
  return (
    <PageContainer>
      <Card title="收款模块">
        <Timeline
          items={[
            { children: 'M1 契约与订单状态机（已完成）：收款单表与集成服务已接线' },
            { children: 'M2 余额：USDT/USDC 记账、扣款/回冲、流水合计作业' },
            { children: 'M3 微信 Native / 支付宝当面付：渠道网关与补单作业、渠道设置' },
            { children: 'M4 Stripe Checkout：验签、去重、金额核对' },
            { children: 'M5 链上：BSC 充值地址、扫链、归集、金库核对、链上监控' },
          ]}
        />
      </Card>
    </PageContainer>
  );
}
