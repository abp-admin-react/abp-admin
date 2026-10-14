import type { ProLayoutProps } from '@ant-design/pro-components';

/**
 * ProLayout 默认外观（被 config.ts 的 `layout: { ...defaultSettings }` 展开）。
 * 只影响壳子主题/布局，不改路由与权限。改色/布局可在运行时被用户「设置抽屉」覆盖（若开启）。
 *
 * @doc https://procomponents.ant.design/components/layout
 */
const Settings: ProLayoutProps & {
  logo?: string;
} = {
  /** 导航主题：`light` | `realDark`。改错只影响观感。 */
  navTheme: 'light',
  /** 品牌主色（Ant Design token）。十六进制色值；影响按钮/链接主色。 */
  colorPrimary: '#1677ff',
  /**
   * 布局模式：`side` 侧栏 | `top` 顶栏 | `mix` 顶+侧。
   * 财务等多级菜单当前按 mix 设计；改成 top 可能挤掉侧栏分组。
   */
  layout: 'mix',
  /** 内容区宽度：`Fluid` 撑满 | `Fixed` 定宽。 */
  contentWidth: 'Fluid',
  /** 是否固定顶栏。true 时滚动内容顶栏不跟着走。 */
  fixedHeader: false,
  /** 是否固定侧栏（字段名历史拼写 fixSiderbar）。 */
  fixSiderbar: true,
  /** 色弱模式。true 会套色弱滤镜。 */
  colorWeak: false,
  /** 浏览器标题/侧栏产品名。与 config.ts `title` 宜保持一致。 */
  title: 'AbpAdmin',
  /** 侧栏 Logo URL。空或坏链只影响图标，不挡功能。 */
  logo: 'https://gw.alipayobjects.com/zos/rmsportal/KDpgvguMpGfqaHPjicRK.svg',
  /** iconfont 项目地址；空=不用额外 iconfont。 */
  iconfontUrl: '',
  token: {
    // 细调布局 token 见：https://procomponents.ant.design/components/layout#通过-token-修改样式
  },
};

export default Settings;
