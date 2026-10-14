/**
 * 浏览器 offline/online 顶栏提示。
 *
 * - 用 useSyncExternalStore 订阅 window 的 online/offline，与 React 18 外部 store 口径一致。
 * - 文案走 i18n（app.request.offline）；仅反映「本机断网」，不代表后端 5xx。
 * - 与 requestErrorConfig 里 !navigator.onLine 的 message.error 互补：横幅常驻，toast 点状。
 */
import { getIntl } from '@umijs/max';
import { Alert } from 'antd';
import { useSyncExternalStore } from 'react';

const subscribeOnlineStatus = (callback: () => void) => {
  window.addEventListener('online', callback);
  window.addEventListener('offline', callback);
  return () => {
    window.removeEventListener('online', callback);
    window.removeEventListener('offline', callback);
  };
};

const getOnlineStatus = () =>
  typeof navigator === 'undefined' ? true : navigator.onLine;

const OfflineBanner: React.FC = () => {
  const isOnline = useSyncExternalStore(
    subscribeOnlineStatus,
    getOnlineStatus,
    () => true,
  );

  if (isOnline) return null;

  return (
    <Alert
      type="warning"
      showIcon
      closable={false}
      style={{
        position: 'fixed',
        top: 8,
        left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 10,
        maxWidth: 480,
      }}
      title={getIntl().formatMessage({
        id: 'app.network.offline',
        defaultMessage:
          'You are currently offline. Some features may be unavailable.',
      })}
    />
  );
};

export default OfflineBanner;
