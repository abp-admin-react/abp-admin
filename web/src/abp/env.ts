export const abpEnv = {
  // 开发环境使用相对路径，通过 Umi 代理访问后端，避免证书问题
  authority: window.location.origin,
  clientId: 'AbpAdmin_App',
  scope: 'openid profile email offline_access roles AbpAdmin',
  appName: 'AbpAdmin',
};
