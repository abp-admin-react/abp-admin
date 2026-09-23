import { QueryClient } from '@tanstack/react-query';

/**
 * 全局 QueryClient 单例（T3.4 第 8 步）。
 * useDictionary（hook，走 React context 里的 QueryClientProvider）与
 * dictionaryRequest（工具函数，直接引用这个单例）
 * 必须共享同一个实例：用两个不同的 QueryClient 会各自缓存一份，请求翻倍且数据可能不一致。
 * Provider 在 app.tsx 的 rootContainer 里挂载。
 */
export const queryClient = new QueryClient();
