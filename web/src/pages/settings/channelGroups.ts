export type SettingPanelDefinition = {
  groupName: string;
  title?: string;
  settingNames?: readonly string[];
};

type SettingPageDefinition = {
  title: string;
  description?: string;
  columns?: 1 | 2;
  panels: readonly SettingPanelDefinition[];
  recordsPath?: string;
};

const quoteFields = [
  'PercentRate',
  'FixedFee',
  'SettlementFloor',
  'FixedFeeRate',
];

function quoteSettingNames(channel: string) {
  return quoteFields.map((field) => `Ordering.Quote.${channel}.${field}`);
}

function channelPanels(
  groupName: string,
  quoteChannel: string,
): readonly SettingPanelDefinition[] {
  return [
    { groupName, title: '单笔收款限额' },
    {
      groupName: 'Ordering',
      title: '费率与结算规则',
      settingNames: quoteSettingNames(quoteChannel),
    },
  ];
}

export const dedicatedSettingPages: Record<string, SettingPageDefinition> = {
  '/alipay/settings': {
    title: '支付宝收款设置',
    recordsPath: '/operations/payments?view=alipay',
    panels: channelPanels('Alipay', 'Alipay'),
  },
  '/wechat/settings': {
    title: '微信支付收款设置',
    recordsPath: '/operations/payments?view=wechat',
    panels: channelPanels('WechatPay', 'Wechat'),
  },
  '/stripe/settings': {
    title: 'Stripe 收款设置',
    recordsPath: '/operations/payments?view=stripe',
    panels: channelPanels('Stripe', 'Stripe'),
  },
  '/collection/order-settings': {
    title: '订单与报价设置',
    panels: [
      {
        groupName: 'Ordering',
        title: '汇率与有效期',
        settingNames: [
          'Ordering.Quote.UsdCnyRate',
          'Ordering.Quote.ValidityMinutes',
          'Ordering.Order.ExpiryHours',
        ],
      },
    ],
  },
  '/collection/settings': {
    title: '余额支付设置',
    description: '用户使用站内 USDT 或 USDC 余额支付订单时，按这里的费率和结算规则计算。',
    columns: 2,
    panels: [
      {
        groupName: 'Ordering',
        title: 'USDT 余额支付',
        settingNames: quoteSettingNames('BalanceUsdt'),
      },
      {
        groupName: 'Ordering',
        title: 'USDC 余额支付',
        settingNames: quoteSettingNames('BalanceUsdc'),
      },
      {
        groupName: 'Collection',
        title: '余额操作',
        settingNames: ['Collection.Balance.DebitCommandTimeoutSeconds'],
      },
    ],
  },
  '/finance/settings': {
    title: '记账设置',
    panels: [
      {
        groupName: 'Finance',
        title: '总账与余额',
        settingNames: [
          'Finance.Ledger.Enabled',
          'Finance.Wallet.UseLedgerProjection',
        ],
      },
    ],
  },
};

export const orderAndPaymentSettings = [
  { key: 'order', label: '订单与报价', path: '/collection/order-settings' },
  { key: 'balance', label: '余额支付', path: '/collection/settings' },
  { key: 'alipay', label: '支付宝', path: '/alipay/settings' },
  { key: 'wechat', label: '微信支付', path: '/wechat/settings' },
  { key: 'stripe', label: 'Stripe', path: '/stripe/settings' },
] as const;

export const dedicatedSettingGroupNames = new Set([
  'Web3',
  ...Object.values(dedicatedSettingPages).flatMap((page) =>
    page.panels.map((panel) => panel.groupName),
  ),
]);

// 租户菜单来自各自的套餐快照。保留原有通用配置入口，宿主使用专用设置页。
export const tenantGeneralSettingGroupNames = new Set(['Ordering', 'Finance']);

/** 保留服务端字段名和元数据，按页面白名单收窄表单提交、重置范围。 */
export function selectSettingGroup(
  groups: API.SettingGroup[],
  panel: SettingPanelDefinition,
): API.SettingGroup | undefined {
  const group = groups.find((item) => item.groupName === panel.groupName);
  const settingInfos = group?.settingInfos?.filter(
    (info) =>
      info.name &&
      (!panel.settingNames || panel.settingNames.includes(info.name)),
  );
  if (!settingInfos?.length) return undefined;
  return {
    ...group,
    groupDisplayName: panel.title ?? group?.groupDisplayName,
    settingInfos,
  };
}
