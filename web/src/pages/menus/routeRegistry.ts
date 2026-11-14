/** 菜单编辑器的页面地址选项。path 对应 config/routes.ts，label 按宿主菜单分组。 */
type RouteRegistryItem = {
  path: string;
  label: string;
};

export const routeRegistry: RouteRegistryItem[] = [
  { path: '/welcome', label: '工作台' },
  { path: '/collection/orders', label: '交易运营 / 订单管理' },
  { path: '/operations/payments', label: '交易运营 / 收款与退款' },
  { path: '/web3/operations', label: '交易运营 / 链上充提' },
  { path: '/configuration/payments', label: '设置中心 / 订单与收款' },
  { path: '/web3/config', label: '设置中心 / 链上充提' },
  { path: '/collection/member-funds', label: '用户资金 / 用户余额与流水' },
  { path: '/finance/balance-adjustments', label: '用户资金 / 余额调整审批' },
  { path: '/finance/overview', label: '会计与对账 / 会计概览' },
  { path: '/finance/vouchers', label: '会计与对账 / 会计凭证' },
  { path: '/finance/transactions', label: '会计与对账 / 分录查询' },
  { path: '/finance/reconcile', label: '会计与对账 / 账务对账' },
  { path: '/finance/balances', label: '会计与对账 / 用户余额核对' },
  { path: '/finance/biz-docs', label: '会计与对账 / 凭证来源查询' },
  { path: '/finance/accounts', label: '会计设置 / 会计科目' },
  { path: '/finance/rules', label: '会计设置 / 自动记账规则' },
  { path: '/finance/period', label: '会计设置 / 会计期间' },
  { path: '/finance/settings', label: '会计设置 / 记账设置' },
  { path: '/administration/identity/users', label: '用户与权限 / 用户' },
  { path: '/administration/identity/roles', label: '用户与权限 / 角色' },
  {
    path: '/administration/identity/organization-units',
    label: '用户与权限 / 部门',
  },
  {
    path: '/administration/identity/claim-types',
    label: '用户与权限 / 用户身份属性',
  },
  { path: '/administration/saas/tenants', label: '租户管理 / 租户' },
  { path: '/administration/saas/editions', label: '租户管理 / 租户功能版本' },
  {
    path: '/administration/saas/tenant-packages',
    label: '租户管理 / 租户菜单套餐',
  },
  { path: '/administration/features', label: '平台设置 / 平台功能开关' },
  { path: '/administration/files', label: '内容与通知 / 文件管理' },
  { path: '/system/notifications', label: '内容与通知 / 通知发送记录' },
  { path: '/administration/text-templates', label: '语言与模板 / 文本模板' },
  {
    path: '/administration/localization/languages',
    label: '语言与模板 / 语言管理',
  },
  {
    path: '/administration/localization/texts',
    label: '语言与模板 / 界面翻译',
  },
  { path: '/administration/settings', label: '平台设置 / 通用设置' },
  { path: '/administration/menus', label: '平台设置 / 后台菜单管理' },
  { path: '/system/data-dictionary', label: '平台设置 / 数据字典' },
  {
    path: '/administration/identity/sessions',
    label: '登录与令牌 / 登录会话',
  },
  {
    path: '/administration/identity/security-logs',
    label: '登录与令牌 / 登录安全日志',
  },
  {
    path: '/administration/openiddict/applications',
    label: '应用接入 / 接入应用',
  },
  {
    path: '/administration/openiddict/scopes',
    label: '应用接入 / 授权范围',
  },
  {
    path: '/administration/openiddict/token-management',
    label: '登录与令牌 / 令牌管理',
  },
  { path: '/administration/webhooks', label: '应用接入 / 事件回调订阅' },
  { path: '/administration/audit-logs', label: '审计与日志 / 接口审计日志' },
  {
    path: '/administration/operation-logs',
    label: '审计与日志 / 业务操作日志',
  },
  { path: '/system/background-jobs', label: '任务调度 / 后台任务' },
  { path: '/system/scheduled-jobs', label: '任务调度 / 定时任务' },
  { path: '/system/server-monitor', label: '运行监控 / 服务监控' },
  { path: '/system/cache-monitor', label: '运行监控 / 缓存监控' },
  { path: '/system/virtual-file-explorer', label: '开发工具 / 虚拟文件浏览' },
  { path: '/biz-template/data-scope', label: '开发工具 / 数据权限示例' },
];
