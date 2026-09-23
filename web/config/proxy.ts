/**
 * @name 代理的配置
 * @doc https://umijs.org/docs/guides/proxy
 */

const target = 'https://localhost:44395';

function rewriteLocation(proxyRes: { headers: Record<string, unknown> }) {
  const location = proxyRes.headers.location;
  if (typeof location !== 'string') {
    return;
  }
  proxyRes.headers.location = location.replace(
    /https?:\/\/localhost:44395/gi,
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
