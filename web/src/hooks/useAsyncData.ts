import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * 收敛「进入 Tab 拉数据」的 loading/refresh 样板（重构报告问题 4）：
 * const { data, loading, refresh } = useAsyncData(() => getXxx(...));
 *
 * fetcher 经 ref 透传，调用方可以直接传内联闭包而不会引发重复请求
 * （若直接把 fetcher 放进 useCallback 依赖，内联闭包每次渲染都是新引用，
 * 会触发 useEffect 死循环）。
 *
 * 竞态防护：refresh 自增请求序号，响应回来只有最新序号才允许 setData/清 loading，
 * 慢的旧响应直接丢弃（否则先发后至会覆盖新数据）。
 */
export function useAsyncData<T>(fetcher: () => Promise<T>) {
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;
  const seqRef = useRef(0);

  const [data, setData] = useState<T>();
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    const seq = ++seqRef.current;
    setLoading(true);
    try {
      const result = await fetcherRef.current();
      if (seq !== seqRef.current) return; // 已有更新的请求在途，丢弃本次过期响应
      setData(result);
    } finally {
      if (seq === seqRef.current) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    // mount 请求失败不再向外抛（unhandled rejection）；错误提示由全局 errorHandler 负责
    refresh().catch(() => undefined);
  }, [refresh]);

  return { data, loading, refresh };
}
