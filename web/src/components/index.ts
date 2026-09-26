/**
 * 这个文件作为组件的目录
 * 目的是统一管理对外输出的组件，方便分类
 */
/**
 * 布局组件
 */
import { LangDropdown, VersionDropdown } from './RightContent';
import { AvatarDropdown } from './RightContent/AvatarDropdown';

/**
 * 业务组件
 */
export { default as CookieConsent } from './CookieConsent';
export { default as ErrorBoundary } from './ErrorBoundary';
export { default as IdleSessionWatcher } from './IdleSessionWatcher';
export { default as ImpersonationBanner } from './ImpersonationBanner';
export { default as NotificationBell } from './NotificationBell';
export { default as OfflineBanner } from './OfflineBanner';
export { default as RealTimeConnection } from './RealTimeConnection';

export { AvatarDropdown, LangDropdown, VersionDropdown };
