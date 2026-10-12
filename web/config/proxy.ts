/**
 * @name 代理的配置
 * @doc https://umijs.org/docs/guides/proxy
 */

// 后端端口：44395 被同框架的 MobileDot.ai 项目占用（同为 launchSettings 默认口），
// 本项目固定走 44396（后端用 --urls https://localhost:44396 启动），避免端口互抢
const target = 'https://localhost:44396';

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
