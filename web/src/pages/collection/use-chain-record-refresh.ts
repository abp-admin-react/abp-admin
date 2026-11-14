import { useEffect } from 'react';

// 页面可见时定期拉取，补齐连接中断期间漏掉的推送。
export function useChainRecordRefresh(
  reload: (background: boolean) => Promise<void>,
) {
  useEffect(() => {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout>;
    const refresh = async () => {
      if (document.visibilityState === 'visible') await reload(true);
      if (!stopped) timer = setTimeout(refresh, 5_000);
    };
    timer = setTimeout(refresh, 5_000);
    return () => {
      stopped = true;
      clearTimeout(timer);
    };
  }, [reload]);
}
