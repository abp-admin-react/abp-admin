/**
 * 可绑定到动态菜单的前端路由注册表。
 * 组件在 config/routes.ts 静态注册（页面代码固定），动态菜单只允许引用这里的 path，
 * 防止菜单管理里配出无法渲染的地址。label 与后端模板 Title 一致，仅作下拉提示。
 */
export type RouteRegistryItem = {
  path: string;
  label: string;
};

export const routeRegistry: RouteRegistryItem[] = [
  { path: '/welcome', label: '欢迎' },
  { path: '/administration/saas/tenants', label: 'SaaS / 租户' },
  { path: '/administration/saas/editions', label: 'SaaS / 版本' },
  { path: '/administration/saas/tenant-packages', label: 'SaaS / 租户套餐' },
  { path: '/administration/identity/users', label: '身份管理 / 用户' },
  { path: '/administration/identity/roles', label: '身份管理 / 角色' },
  {
    path: '/administration/identity/organization-units',
    label: '身份管理 / 组织单元',
  },
  {
    path: '/administration/identity/claim-types',
    label: '身份管理 / 声明类型',
  },
  {
    path: '/administration/identity/security-logs',
    label: '身份管理 / 安全日志',
  },
  { path: '/administration/identity/sessions', label: '身份管理 / 会话' },
  {
    path: '/administration/openiddict/applications',
    label: 'OpenIddict / 应用程序',
  },
  { path: '/administration/openiddict/scopes', label: 'OpenIddict / 范围' },
  { path: '/administration/openiddict/token-management', label: 'OpenIddict / 令牌管理' },
  { path: '/administration/features', label: '功能管理' },
  {
    path: '/administration/localization/languages',
    label: '本地化 / 语言管理',
  },
  { path: '/administration/localization/texts', label: '本地化 / 本地化文本' },
  { path: '/administration/text-templates', label: '文本模板' },
  { path: '/administration/audit-logs', label: '审计日志' },
  { path: '/administration/files', label: '文件管理' },
  { path: '/administration/payments', label: '支付' },
  { path: '/administration/menus', label: '菜单管理' },
  { path: '/administration/settings', label: '设置管理' },
  { path: '/system/background-jobs', label: '系统 / 后台作业' },
  { path: '/system/scheduled-jobs', label: '系统 / 定时作业' },
  { path: '/system/notifications', label: '系统 / 通知' },
  { path: '/system/data-dictionary', label: '系统 / 数据字典' },
  { path: '/system/virtual-file-explorer', label: '系统 / 虚拟文件浏览' },
  { path: '/data-scope-demo', label: '数据权限演示' },
  { path: '/current-session', label: '当前会话' },
];

/** 目录类型的挂载点（只用作上级选择提示）。 */
export const catalogRegistry: RouteRegistryItem[] = [
  { path: '', label: '顶级' },
  { path: '/administration', label: '管理' },
  { path: '/administration/saas', label: '管理 / SaaS' },
  { path: '/administration/identity', label: '管理 / 身份管理' },
  { path: '/administration/openiddict', label: '管理 / OpenIddict' },
  { path: '/administration/localization', label: '管理 / 本地化管理' },
  { path: '/system', label: '系统' },
];
