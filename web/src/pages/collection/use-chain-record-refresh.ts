import { useEffect } from 'react';

/** 一次只执行一个刷新；执行期间收到的新事件合并到下一次读取。 */
export function trailingRefresh(reload: () => Promise<void>) {
  let active: Promise<void> | undefined;
  let pending = false;
  let stopped = false;
  return {
    run() {
      if (stopped) return Promise.resolve();
      pending = true;
      if (!active) {
        active = (async () => {
          try {
            while (pending && !stopped) {
              pending = false;
              await reload();
            }
          } finally {
            active = undefined;
          }
        })();
      }
      return active;
    },
    stop() { stopped = true; },
  };
}

// 推送触发短延迟合并，轮询补齐断线期间漏掉的更新。后台标签页保留定时器但跳过读取。
export function useChainRecordRefresh(
  reload: (background: boolean) => Promise<void>,
  subscribe?: (changed: () => void) => () => void,
) {
  useEffect(() => {
    let stopped = false;
    let burst: ReturnType<typeof setTimeout> | undefined;
    const refresh = trailingRefresh(async () => {
      if (document.visibilityState === 'visible') await reload(true);
    });
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      await refresh.run();
      if (!stopped) timer = setTimeout(poll, 5_000);
    };
    timer = setTimeout(poll, 5_000);
    const off = subscribe?.(() => {
      clearTimeout(burst);
      burst = setTimeout(() => {
        burst = undefined;
        void refresh.run();
      }, 200);
    });
    return () => {
      stopped = true;
      refresh.stop();
      clearTimeout(timer);
      clearTimeout(burst);
      off?.();
    };
  }, [reload, subscribe]);
}
