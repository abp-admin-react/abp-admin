/**
 * SPA 语言 → 后端文化的桥接。
 *
 * ABP 官方对独立 SPA 前端（Angular UI）的处理方式：前端语言是唯一事实源
 * （存 localStorage），每个 API 请求通过 REST 拦截器带 `Accept-Language` 头，
 * 后端 RequestLocalization 管线按 provider 优先级（用户上下文 → query → cookie →
 * Accept-Language）解析出当前文化，错误消息随响应本地化。后端 Razor 页
 * （登录页）看不到 SPA 的 localStorage，靠 `.AspNetCore.Culture` cookie 对齐——
 * 所以切换语言时同步写这个 cookie，两处 UI 才不会各说各话。
 */

/** umi locale → 后端 SupportedUICultures 里的文化名（AbpAdminDomainSharedModule / 本地化管理页维护）。
 *  未出现在映射里的 locale 原样传递：后端不支持时 RequestLocalization 自动回退默认文化，
 *  头本身无害；语言在「本地化管理」页启用后即刻生效，前端无需改动。 */
const localeToCultureMap: Record<string, string> = {
  'zh-CN': 'zh-Hans',
  'zh-TW': 'zh-Hant',
  'en-US': 'en',
};

export function toAbpCulture(locale: string): string {
  return localeToCultureMap[locale] ?? locale;
}

/** CookieRequestCultureProvider 的取值格式：`c=<culture>|uic=<culture>`（整体 URL 编码）。 */
function formatCultureCookie(culture: string): string {
  const raw = `c=${culture}|uic=${culture}`;
  return encodeURIComponent(raw);
}

/** 切换语言时把 `.AspNetCore.Culture` cookie 写成 SPA 的选择——
 *  cookie 优先级高于 Accept-Language，不同步会让后端永远拿到旧文化。 */
export function syncCultureCookie(locale: string): void {
  if (typeof document === 'undefined') {
    return;
  }
  document.cookie = `.AspNetCore.Culture=${formatCultureCookie(
    toAbpCulture(locale),
  )}; path=/; samesite=lax`;
}
