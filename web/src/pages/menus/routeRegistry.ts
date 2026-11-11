/** 菜单编辑器的页面地址选项。path 对应 config/routes.ts，label 按宿主菜单分组。 */
type RouteRegistryItem = {
  path: string;
  label: string;
};

export const routeRegistry: RouteRegistryItem[] = [
  { path: '/welcome', label: '首页' },
  { path: '/administration/identity/users', label: '身份管理 / 用户' },
  { path: '/administration/identity/roles', label: '身份管理 / 角色' },
  {
    path: '/administration/identity/organization-units',
    label: '身份管理 / 部门',
  },
  {
    path: '/administration/identity/claim-types',
    label: '身份管理 / 声明类型',
  },
  { path: '/administration/saas/tenants', label: '租户与功能 / 租户' },
  { path: '/administration/saas/editions', label: '租户与功能 / 版本' },
  {
    path: '/administration/saas/tenant-packages',
    label: '租户与功能 / 租户套餐',
  },
  { path: '/administration/features', label: '租户与功能 / 功能管理' },
  { path: '/administration/files', label: '内容与语言 / 文件管理' },
  { path: '/system/notifications', label: '内容与语言 / 通知' },
  { path: '/administration/text-templates', label: '内容与语言 / 文本模板' },
  {
    path: '/administration/localization/languages',
    label: '内容与语言 / 语言管理',
  },
  {
    path: '/administration/localization/texts',
    label: '内容与语言 / 本地化文本',
  },
  { path: '/administration/settings', label: '基础设置 / 系统设置' },
  { path: '/administration/menus', label: '基础设置 / 菜单管理' },
  { path: '/system/data-dictionary', label: '基础设置 / 数据字典' },
  { path: '/administration/identity/sessions', label: '登录与接入 / 登录会话' },
  {
    path: '/administration/identity/security-logs',
    label: '登录与接入 / 登录日志',
  },
  {
    path: '/administration/openiddict/applications',
    label: '登录与接入 / 接入应用',
  },
  { path: '/administration/openiddict/scopes', label: '登录与接入 / 授权范围' },
  {
    path: '/administration/openiddict/token-management',
    label: '登录与接入 / 令牌管理',
  },
  { path: '/administration/audit-logs', label: '审计与日志 / 审计日志' },
  { path: '/administration/operation-logs', label: '审计与日志 / 操作日志' },
  { path: '/system/background-jobs', label: '作业调度 / 后台作业' },
  { path: '/system/scheduled-jobs', label: '作业调度 / 定时作业' },
  { path: '/system/server-monitor', label: '运行监控 / 服务监控' },
  { path: '/system/cache-monitor', label: '运行监控 / 缓存监控' },
  { path: '/system/virtual-file-explorer', label: '开发工具 / 虚拟文件浏览' },
  { path: '/biz-template/data-scope', label: '开发工具 / 数据范围示例' },
  { path: '/collection/dashboard', label: '收付款 / 资金看板' },
  { path: '/collection/orders', label: '收付款 / 订单' },
  { path: '/administration/payments', label: '收付款 / 支付记录' },
  { path: '/collection/deposits', label: '收付款 / 充值单' },
  { path: '/collection/withdrawals', label: '收付款 / 提现管理' },
  { path: '/collection/sweeps', label: '收付款 / 归集任务' },
  { path: '/collection/vault', label: '收付款 / 金库核对' },
  { path: '/collection/member-funds', label: '收付款 / 会员资金' },
  { path: '/collection/hot-wallet', label: '收付款 / 热钱包监控' },
  { path: '/collection/network', label: '收付款 / 链网配置' },
  { path: '/finance/overview', label: '财务会计 / 资金总览' },
  { path: '/finance/accounts', label: '财务会计 / 科目表' },
  { path: '/finance/rules', label: '财务会计 / 过账规则' },
  { path: '/finance/vouchers', label: '财务会计 / 凭证与调账' },
  { path: '/finance/period', label: '财务会计 / 过账与期间' },
  { path: '/finance/reconcile', label: '财务会计 / 对账' },
  { path: '/finance/biz-docs', label: '财务会计 / 业务单据' },
  { path: '/finance/balances', label: '财务会计 / 余额投影' },
];
