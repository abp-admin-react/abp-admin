// https://umijs.org/config/
//
// 本文件是 Umi Max 工程配置真源（defineConfig）。
// 与后端端口/OIDC 相关的「可变运维项」不在这里写死：
// - 代理上游 → config/proxy.ts + 环境变量 ABP_TARGET
// - OIDC 客户端 → src/abp/env.ts
// - 财务本地开关 → 浏览器 localStorage（见 src/abp/finance.ts 文件头）
//
import { join } from 'node:path';
import { defineConfig } from '@umijs/max';
import defaultSettings from './defaultSettings';
import proxy from './proxy';

import routes from './routes';

/**
 * Umi 环境档，决定取 `proxy.ts` 里哪一套代理。
 * - 干什么：`proxy: proxy[UMI_ENV]`；也影响部分插件行为。
 * - 可配：`dev` | `test` | `pre`（须与 proxy.ts 导出键一致）。
 * - 默认：`dev`（完整 OIDC+API 代理，本地登录联调用这个）。
 * - 覆盖：`UMI_ENV=test pnpm start:test` 等（见 package.json scripts）。
 * - 改错会怎样：取到 undefined 代理 → 接口全失败；用 test/pre 做登录会缺 /connect。
 * - 与 ABP_TARGET 无关：UMI_ENV 选「哪套 path」，ABP_TARGET 选「上游 Host」。
 */
const { UMI_ENV = 'dev' } = process.env;

/**
 * 构建写入的提交号（define 进包，供关于页/排障）。
 * 优先级：COMMIT_HASH → CF_PAGES_COMMIT_SHA（Cloudflare Pages）→ 构建机 `git rev-parse HEAD` → 空串。
 * 空串不致命，只是版本信息缺失。
 */
const commitHash =
  process.env.COMMIT_HASH ||
  process.env.CF_PAGES_COMMIT_SHA ||
  (() => {
    try {
      return require('node:child_process')
        .execSync('git rev-parse HEAD', {
          stdio: ['ignore', 'pipe', 'ignore'],
          encoding: 'utf-8',
        })
        .trim();
    } catch {
      return '';
    }
  })();

/**
 * @name 使用公共路径
 * @description 部署时的路径，如果部署在非根目录下，需要配置这个变量
 * @doc https://umijs.org/docs/api/config#publicpath
 */
const PUBLIC_PATH: string = '/';

export default defineConfig({
  npmClient: 'pnpm',
  alias: {
    '@root': join(__dirname, '..'),
  },
  /**
   * @name 开启 hash 模式
   * @description 让 build 之后的产物包含 hash 后缀。通常用于增量发布和避免浏览器加载缓存。
   * @doc https://umijs.org/docs/api/config#hash
   */
  hash: true,

  publicPath: PUBLIC_PATH,

  /**
   * @name 兼容性设置
   * @description 设置 ie11 不一定完美兼容，需要检查自己使用的所有依赖
   * @doc https://umijs.org/docs/api/config#targets
   */
  // targets: {
  //   ie: 11,
  // },
  /**
   * @name 路由的配置，不在路由中引入的文件不会编译
   * @description 只支持 path，component，routes，redirect，wrappers，title 的配置
   * @doc https://umijs.org/docs/guides/routes
   */
  // umi routes: https://umijs.org/docs/routing
  routes,
  /**
   * @name 主题的配置
   * @description 虽然叫主题，但是其实只是 less 的变量设置
   * @doc antd的主题设置 https://ant.design/docs/react/customize-theme-cn
   * @doc umi 的 theme 配置 https://umijs.org/docs/api/config#theme
   */
  // theme: { '@primary-color': '#1DA57A' }
  /**
   * @name moment 的国际化配置
   * @description 如果对国际化没有要求，打开之后能减少js的包大小
   * @doc https://umijs.org/docs/api/config#ignoremomentlocale
   */
  ignoreMomentLocale: true,
  /**
   * @name 代理配置
   * @description 本地 dev server 把 API/OIDC/SignalR 转到后端；build 后无效，生产靠 nginx.conf。
   * 选档：`proxy[UMI_ENV]`；上游地址与路径清单见 `./proxy.ts`（ABP_TARGET / 44300 真源说明也在那里）。
   * @doc https://umijs.org/docs/guides/proxy
   */
  proxy: proxy[UMI_ENV as keyof typeof proxy],
  /**
   * @name 快速热更新配置
   * @description 一个不错的热更新组件，更新时可以保留 state
   */
  fastRefresh: true,
  /**
   * @name 路由预加载
   * @description 预加载路由资源，提升页面切换速度
   * @doc https://umijs.org/docs/api/config#routePrefetch
   */
  routePrefetch: {},
  /**
   * @name manifest 配置
   * @description 生成资源清单，配合 routePrefetch 使用
   */
  manifest: {},
  //============== 以下都是max的插件配置 ===============
  /**
   * @name 数据流插件
   * @@doc https://umijs.org/docs/max/data-flow
   */
  model: {},
  /**
   * 一个全局的初始数据流，可以用它在插件之间共享数据
   * @description 可以用来存放一些全局的数据，比如用户信息，或者一些全局的状态，全局初始状态在整个 Umi 项目的最开始创建。
   * @doc https://umijs.org/docs/max/data-flow#%E5%85%A8%E5%B1%80%E5%88%9D%E5%A7%8B%E7%8A%B6%E6%80%81
   */
  initialState: {},
  /**
   * @name layout 插件
   * @doc https://umijs.org/docs/max/layout-menu
   */
  title: 'AbpAdmin',
  layout: {
    locale: true,
    ...defaultSettings,
  },
  /**
   * @name moment2dayjs 插件
   * @description 将项目中的 moment 替换为 dayjs
   * @doc https://umijs.org/docs/max/moment2dayjs
   */
  moment2dayjs: {
    preset: 'antd',
    plugins: ['duration', 'relativeTime'],
  },
  /**
   * @name 国际化插件
   * @doc https://umijs.org/docs/max/i18n
   */
  locale: {
    // default zh-CN
    default: 'zh-CN',
    antd: true,
    // default true, when it is true, will use `navigator.language` overwrite default
    baseNavigator: true,
  },
  /**
   * @name antd 插件
   * @description 内置了 babel import 插件
   * @doc https://umijs.org/docs/max/antd#antd
   */
  antd: {
    appConfig: {},
    configProvider: {
      variant: 'filled',
      theme: {
        token: {
          fontFamily: 'AlibabaSans, sans-serif',
        },
      },
    },
  },
  /**
   * @name 网络请求配置
   * @description 它基于 axios 和 ahooks 的 useRequest 提供了一套统一的网络请求和错误处理方案。
   * @doc https://umijs.org/docs/max/request
   */
  request: {},
  /**
   * @name React Query 插件
   * @description 使用 react-query 管理服务端状态
   * @doc https://umijs.org/docs/max/react-query
   *
   * T3.4：插件保持关闭。它自带的 QueryClientProvider 不导出 client 实例，
   * 而 dictionaryValueEnum 等工具函数必须拿到全局单例（web/src/queryClient.ts），
   * 所以 Provider 统一在 app.tsx 的 rootContainer 里用同一个实例挂载。
   * table-list 等页面的 useQueryClient 走 context，拿到的就是那个单例。
   */
  reactQuery: false,
  /**
   * @name 权限插件
   * @description 基于 initialState 的权限插件，必须先打开 initialState
   * @doc https://umijs.org/docs/max/access
   */
  access: {},
  /**
   * @name Google Analytics
   * @description 使用 GA4 (gtag.js) 进行站点分析
   * @doc https://umijs.org/docs/max/analytics
   */
  // analytics disabled for local ABP admin

  /**
   * @name <head> 中额外的 script
   * @description 配置 <head> 中额外的 script
   */
  headScripts: [
    // 解决首次加载时白屏的问题
    { src: join(PUBLIC_PATH, 'scripts/loading.js'), async: true },
  ],

  //================ pro 插件配置 =================
  // openapi 生成器已退役（前端 API 层全部 AI 手写，见 web/AGENTS.md 服务层约定），
  // 不再保留 openAPI 配置，避免误生成覆写 src/services/abpadmin/ 冻结存量。
  plugins: ['@umijs/request-record'],

  tailwindcss: {},

  mock: false,
  utoopack: {
    module: {
      rules: {
        '*.md': {
          loaders: [{ loader: join(__dirname, 'md-raw-loader.cjs') }],
          as: '*.js',
        },
      },
    },
  },
  requestRecord: {},
  exportStatic: {},
  /**
   * 编译期注入的全局常量（浏览器里可读，不是运行时 process.env）。
   * - process.env.CI：CI 标记，有则部分逻辑可收敛日志/埋点。
   * - process.env.COMMIT_HASH：见上方 commitHash 优先级。
   * - __APP_VERSION__ / __UMI_VERSION__ / __UTOO_VERSION__：来自对应 package.json，勿手改。
   * 敏感值禁止放进 define（会打进静态包）。ABP_TARGET 只给 Node 代理用，不要 define 进前端。
   */
  define: {
    'process.env.CI': process.env.CI,
    'process.env.COMMIT_HASH': commitHash,
    __APP_VERSION__: require('./../package.json').version,
    __UMI_VERSION__: require('@umijs/max/package.json').version,
    __UTOO_VERSION__: require('@utoo/pack/package.json').version,
  },
});
