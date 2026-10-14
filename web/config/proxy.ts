/**
 * @name 代理的配置
 * @doc https://umijs.org/docs/guides/proxy
 */

// 后端 HTTPS 端口须在 IIS Express 证书段 44300–44399 内；默认对齐 launchSettings 的 44300。
// 后端换端口时用 ABP_TARGET=https://localhost:xxxxx 覆盖，不改这里。
const target = process.env.ABP_TARGET || 'https://localhost:44300';

function rewriteLocation(proxyRes: { headers: Record<string, unknown> }) {
  const location = proxyRes.headers.location;
  if (typeof location !== 'string') {
    return;
  }
  // 443XX 通配：后端换端口无需改这里（issuer 绝对地址一律改写成相对路径）
  proxyRes.headers.location = location.replace(
    /https?:\/\/localhost:443\d+/gi,
    '',
  );
}

const abpProxy = {
  target,
  changeOrigin: true,
  secure: false,
  onProxyRes: rewriteLocation,
};

// SignalR Hub 需要代理 WebSocket Upgrade，ws: true 只对 upgrade 请求生效
const abpWsProxy = {
  ...abpProxy,
  ws: true,
};

export default {
  dev: {
    '/api/': abpProxy,
    '/connect/': abpProxy,
    '/.well-known/': abpProxy,
    '/Account/': abpProxy,
    '/Abp/': abpProxy,
    '/libs/': abpProxy,
    '/Themes/': abpProxy,
    '/global-styles.css': abpProxy,
    '/global-scripts.js': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
  test: {
    '/api/': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
  pre: {
    '/api/': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
};
