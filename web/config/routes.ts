/** 页面路由、访问权限和历史地址跳转。运行时侧栏分组由 /api/app/my-menu 提供。 */
export default [
  // 根路径先匹配工作台跳转，避免落到空路径的权限分组。
  {
    path: '/',
    layout: false,
    redirect: '/welcome',
  },
  {
    path: '/user',
    layout: false,
    routes: [
      {
        name: 'login',
        path: '/user/login',
        component: './user/login',
      },
      {
        name: 'callback',
        path: '/user/callback',
        component: './user/callback',
        hideInMenu: true,
      },
      {
        name: 'two-factor',
        path: '/user/two-factor',
        component: './user/two-factor',
        hideInMenu: true,
      },
    ],
  },
  {
    path: '/tenant-not-found',
    layout: false,
    component: './tenant-not-found',
  },
  {
    path: '/Error',
    layout: false,
    component: './exception/request-error',
  },
  {
    path: '/welcome',
    name: 'welcome',
    icon: 'smile',
    component: './Welcome',
  },
  {
    path: '/administration',
    name: 'administration',
    icon: 'appstore',
    routes: [
      {
        path: '/administration/saas',
        name: 'saas',
        routes: [
          {
            path: '/administration/saas/tenants',
            name: 'tenants',
            access: 'canOpenTenantWorkspace',
            component: './tenants',
          },
          {
            path: '/administration/saas/editions',
            name: 'editions',
            access: 'canManageEditions',
            component: './saas/editions',
          },
          {
            path: '/administration/saas/tenant-packages',
            name: 'tenant-packages',
            icon: 'cluster',
            access: 'canManageTenantPackages',
            component: './saas/tenant-packages',
          },
        ],
      },
      {
        path: '/administration/identity',
        name: 'identity',
        icon: 'team',
        routes: [
          {
            path: '/administration/identity/users',
            name: 'users',
            access: 'canManageUsers',
            component: './identity/users',
          },
          {
            path: '/administration/identity/roles',
            name: 'roles',
            access: 'canManageRoles',
            component: './identity/roles',
          },
          {
            path: '/administration/identity/organization-units',
            name: 'organization-units',
            access: 'canManageOrganizationUnits',
            component: './identity/organization-units',
          },
          {
            path: '/administration/identity/claim-types',
            name: 'claim-types',
            access: 'canManageClaimTypes',
            component: './identity/claim-types',
          },
          {
            path: '/administration/identity/security-logs',
            name: 'security-logs',
            access: 'canOpenLoginSecurityWorkspace',
            component: './identity/security-logs',
          },
          {
            path: '/administration/identity/sessions',
            name: 'sessions',
            access: 'canManageSessions',
            component: './identity/sessions',
          },
        ],
      },
      {
        path: '/administration/webhooks',
        name: 'webhooks',
        icon: 'api',
        access: 'canOpenWebhookWorkspace',
        component: './webhooks',
      },
      {
        path: '/administration/openiddict',
        name: 'openiddict',
        icon: 'safety',
        routes: [
          {
            path: '/administration/openiddict/applications',
            name: 'applications',
            access: 'canManageOpenIddictApplications',
            component: './openiddict/applications',
          },
          {
            path: '/administration/openiddict/scopes',
            name: 'scopes',
            access: 'canManageOpenIddictScopes',
            component: './openiddict/scopes',
          },
          {
            path: '/administration/openiddict/token-management',
            name: 'token-management',
            access: 'canManageOpenIddictTokens',
            component: './openiddict/token-management',
          },
        ],
      },
      {
        path: '/administration/features',
        name: 'features',
        icon: 'control',
        access: 'canManageHostFeatures',
        component: './features',
      },
      {
        path: '/administration/localization',
        name: 'localization',
        icon: 'global',
        routes: [
          {
            path: '/administration/localization/languages',
            name: 'languages',
            access: 'canOpenLanguageWorkspace',
            component: './languages',
          },
          {
            path: '/administration/localization/texts',
            name: 'language-texts',
            access: 'canManageLanguageTexts',
            component: './languages/texts',
          },
        ],
      },
      {
        path: '/administration/text-templates',
        name: 'text-templates',
        icon: 'fileText',
        access: 'canManageTextTemplates',
        component: './text-templates',
      },
      {
        path: '/administration/audit-logs',
        name: 'audit-logs',
        icon: 'fileSearch',
        access: 'canOpenAuditWorkspace',
        component: './audit-logs',
      },
      {
        path: '/administration/operation-logs',
        name: 'operation-logs',
        icon: 'history',
        access: 'canManageOperationLogs',
        component: './operation-logs',
      },
      {
        path: '/administration/files',
        name: 'files',
        icon: 'folder',
        access: 'canManageFiles',
        component: './files',
      },
      {
        path: '/administration/menus',
        name: 'menus',
        icon: 'menu',
        access: 'canManageMenus',
        component: './menus',
      },
      {
        path: '/administration/modules',
        name: 'modules',
        icon: 'appstore',
        access: 'canViewModuleManagement',
        component: './modules',
      },
      {
        path: '/administration/settings',
        name: 'settings',
        hideInMenu: true,
        icon: 'setting',
        access: 'canOpenCompatibilitySettings',
        component: './settings',
      },
    ],
  },
  {
    path: '/system',
    name: 'system',
    icon: 'desktop',
    routes: [
      {
        path: '/system/background-jobs',
        name: 'background-jobs',
        icon: 'thunderbolt',
        access: 'canManageBackgroundJobs',
        component: './background-jobs',
      },
      {
        path: '/system/scheduled-jobs',
        name: 'scheduled-jobs',
        icon: 'clockCircle',
        access: 'canManageScheduledJobs',
        component: './scheduled-jobs',
      },
      {
        path: '/system/notifications',
        name: 'notifications',
        icon: 'bell',
        access: 'canOpenNotificationWorkspace',
        component: './notifications',
      },
      {
        path: '/system/data-dictionary',
        name: 'data-dictionary',
        icon: 'table',
        access: 'canViewDataDictionary',
        component: './data-dictionary',
      },
      {
        path: '/system/virtual-file-explorer',
        name: 'virtual-file-explorer',
        icon: 'folderOpen',
        access: 'canManageVirtualFileExplorer',
        component: './virtual-file-explorer',
      },
      {
        path: '/system/server-monitor',
        name: 'server-monitor',
        icon: 'dashboard',
        access: 'canViewServerMonitor',
        component: './system/server-monitor',
      },
      {
        path: '/system/cache-monitor',
        name: 'cache-monitor',
        icon: 'hdd',
        access: 'canViewCacheMonitor',
        component: './system/cache-monitor',
      },
    ],
  },
  {
    // 业务数据范围扩展配置与 Biz.Template 示例。
    path: '/biz-template/data-scope',
    name: 'biz-template-data-scope',
    icon: 'apartment',
    access: 'canOpenDataScopeExtension',
    component: './biz-template/data-scope',
  },
  {
    // 基础财务层 A1–A7（定稿 §5 / §6）；权限码对齐 FinancePermissions
    path: '/finance',
    name: 'finance',
    icon: 'accountBook',
    access: 'canViewFinance',
    routes: [
      { path: '/finance', redirect: '/finance/overview' },
      {
        path: '/finance/overview',
        name: 'finance-overview',
        access: 'canOpenFinanceOverview',
        component: './finance/overview',
      },
      {
        path: '/finance/transactions',
        name: 'finance-transactions',
        access: 'canViewFinanceVouchers',
        hideInMenu: true,
        component: './finance/transactions',
      },
      {
        path: '/finance/balance-adjustments',
        name: 'finance-balance-adjustments',
        access: 'canViewBalanceAdjustments',
        component: './finance/balance-adjustments',
      },
      {
        path: '/finance/reconcile',
        name: 'finance-reconcile',
        access: 'canViewFinanceReconcile',
        component: './finance/reconcile',
      },
      {
        path: '/finance/vouchers',
        name: 'finance-vouchers',
        access: 'canOpenFinanceVouchersWorkspace',
        component: './finance/vouchers',
      },
      {
        path: '/finance/balances',
        name: 'finance-balances',
        access: 'canViewFinanceBalances',
        component: './finance/balances',
      },
      {
        path: '/finance/biz-docs',
        name: 'finance-biz-docs',
        access: 'canViewFinanceBizDocs',
        component: './finance/biz-docs',
      },
      {
        path: '',
        name: 'finance-config',
        access: 'canViewFinanceConfiguration',
        hideInMenu: true,
        routes: [
          { path: '/finance/configuration', component: './finance/overview/legacy-configuration' },
          {
            path: '/finance/accounts',
            name: 'finance-accounts',
            access: 'canViewFinanceAccounts',
            component: './finance/overview/legacy-configuration',
          },
          {
            path: '/finance/rules',
            name: 'finance-rules',
            access: 'canViewFinanceRules',
            component: './finance/overview/legacy-configuration',
          },
          {
            path: '/finance/settings',
            name: 'finance-settings',
            access: 'canManagePaymentSettings',
            component: './finance/overview/legacy-configuration',
          },
          {
            path: '/finance/period',
            name: 'finance-period',
            access: 'canViewFinancePeriod',
            component: './finance/overview/legacy-configuration',
          },
        ],
      },
    ],
  },
  {
    // 路由保留已有页面地址，侧栏按业务用途分组。
    path: '',
    name: 'collection',
    icon: 'payCircle',
    access: 'canViewTrade',
    routes: [
      { path: '/collection', redirect: '/collection/orders' },
      {
        path: '/collection/orders',
        name: 'collection-orders',
        access: 'canOpenOrderWorkspace',
        component: './ordering',
      },
      {
        path: '/collection/order-settings',
        component: './workspace/legacy-route', hideInMenu: true,
      },
      {
        path: '/operations/payments',
        name: 'operations-payments',
        access: 'canOpenPaymentWorkspace',
        component: './payments',
      },
      { path: '/administration/payments', component: './workspace/legacy-route', hideInMenu: true },
      {
        path: '/collection/member-funds',
        name: 'collection-member-funds',
        access: 'canViewMemberFunds',
        component: './collection/member-funds',
      },
      {
        path: '/collection/withdrawals',
        name: 'collection-withdrawals',
        access: 'canOpenWithdrawalWorkspace',
        component: './collection/withdrawals',
      },
      {
        path: '/collection/settings',
        component: './workspace/legacy-route', hideInMenu: true,
      },
    ],
  },
  {
    path: '',
    name: 'web3',
    icon: 'wallet',
    access: 'canViewWeb3',
    routes: [
      {
        path: '/web3/operations',
        name: 'web3-operations',
        access: 'canViewWeb3',
        component: './collection/operations',
      },
      {
        path: '/web3/config',
        name: 'web3-configuration-page',
        access: 'canManageWeb3Configuration',
        component: './collection/config',
      },
      {
        path: '/web3',
        component: './collection/web3-entry',
        hideInMenu: true,
      },
      {
        path: '/web3/rules',
        component: './collection/web3-entry',
        hideInMenu: true,
      },
      {
        path: '/collection/dashboard',
        component: './workspace/legacy-route', hideInMenu: true,
      },
      {
        path: '/collection/deposits',
        component: './workspace/legacy-route', hideInMenu: true,
      },
      { path: '/collection/sweeps', component: './workspace/legacy-route', hideInMenu: true },
      { path: '/collection/vault', component: './workspace/legacy-route', hideInMenu: true },
      {
        path: '/collection/hot-wallet',
        component: './workspace/legacy-route', hideInMenu: true,
      },
      { path: '/web3/configuration', component: './workspace/legacy-route', hideInMenu: true },
      { path: '/web3/settings', component: './workspace/legacy-route', hideInMenu: true },
      { path: '/collection/network', component: './workspace/legacy-route', hideInMenu: true },
      { path: '/collection/wallets', component: './workspace/legacy-route', hideInMenu: true },
    ],
  },
  {
    path: '/configuration/payments',
    name: 'configuration-payments',
    access: 'canManagePaymentSettings',
    component: './settings/channel',
    hideInMenu: true,
  },
  { path: '/alipay', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/alipay/payments', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/alipay/settings', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/wechat', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/wechat/payments', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/wechat/settings', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/stripe', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/stripe/payments', component: './workspace/legacy-route', hideInMenu: true },
  { path: '/stripe/settings', component: './workspace/legacy-route', hideInMenu: true },
  {
    path: '/account/center',
    name: 'account-center',
    icon: 'user',
    hideInMenu: true,
    component: './account/center',
  },
  {
    path: '/account/force-change-password',
    hideInMenu: true,
    component: './account/force-change-password',
  },
  {
    // 历史路径兼容：旧菜单层级下的地址重定向到新层级
    path: '/saas/tenants',
    redirect: '/administration/saas/tenants',
  },
  {
    path: '/saas/editions',
    redirect: '/administration/saas/editions',
  },
  {
    path: '/tenants',
    redirect: '/administration/saas/tenants',
  },
  {
    path: '/identity',
    redirect: '/administration/identity/users',
  },
  {
    path: '/identity/users',
    redirect: '/administration/identity/users',
  },
  {
    path: '/identity/roles',
    redirect: '/administration/identity/roles',
  },
  {
    path: '/identity/organization-units',
    redirect: '/administration/identity/organization-units',
  },
  {
    path: '/identity/claim-types',
    redirect: '/administration/identity/claim-types',
  },
  {
    path: '/identity/security-logs',
    redirect: '/administration/identity/security-logs',
  },
  {
    path: '/identity/sessions',
    redirect: '/administration/identity/sessions',
  },
  {
    path: '/openiddict',
    redirect: '/administration/openiddict/applications',
  },
  {
    path: '/openiddict/applications',
    redirect: '/administration/openiddict/applications',
  },
  {
    path: '/openiddict/scopes',
    redirect: '/administration/openiddict/scopes',
  },
  {
    path: '/features',
    redirect: '/administration/features',
  },
  {
    path: '/localization',
    redirect: '/administration/localization/languages',
  },
  {
    path: '/localization/languages',
    redirect: '/administration/localization/languages',
  },
  {
    path: '/localization/texts',
    redirect: '/administration/localization/texts',
  },
  {
    path: '/text-templates',
    redirect: '/administration/text-templates',
  },
  {
    path: '/audit-logs',
    redirect: '/administration/audit-logs',
  },
  {
    path: '/settings',
    component: './workspace/legacy-route', hideInMenu: true,
  },
  {
    path: '/background-jobs',
    redirect: '/system/background-jobs',
  },
  {
    path: '/scheduled-jobs',
    redirect: '/system/scheduled-jobs',
  },
  {
    path: '/notifications',
    redirect: '/system/notifications',
  },
  {
    path: '/data-dictionary',
    redirect: '/system/data-dictionary',
  },
  {
    path: '/virtual-file-explorer',
    redirect: '/system/virtual-file-explorer',
  },
  {
    path: '/files',
    redirect: '/administration/files',
  },
  {
    path: '/payments',
    component: './workspace/legacy-route', hideInMenu: true,
  },
  ...(process.env.NODE_ENV === 'production'
    ? []
    : [
        {
          path: '/framework/customer-errors-demo',
          hideInMenu: true,
          component: './framework/customer-errors-demo',
        },
      ]),
  {
    component: './exception/404',
    layout: false,
    path: '/*',
  },
];
