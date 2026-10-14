/**
 * 财务页通用边界状态（页面清单 v1 §2.0）：
 * - 无权限：接口 403 → 「没有权限查看此页面」，不显示任何数据和按钮
 * - 报错：「加载失败」+「重试」；业务错误码按 §1.2 映射，其余显示后端 message
 * - 空状态：各页通过 emptyText 传入
 */
import { Button, Empty, Result } from 'antd';
import React, { useCallback, useState } from 'react';
import { financeErrorText, isFinanceForbidden } from '@/abp/finance';

export type FinanceLoadState = 'ok' | 'forbidden' | 'error';

export function useFinanceLoad() {
  const [state, setState] = useState<FinanceLoadState>('ok');
  const [errorText, setErrorText] = useState<string>();
  const [nonce, setNonce] = useState(0);

  const guard = useCallback(async <T,>(fn: () => Promise<T>): Promise<T | undefined> => {
    try {
      return await fn();
    } catch (e) {
      if (isFinanceForbidden(e)) {
        setState('forbidden');
      } else {
        setErrorText(financeErrorText(e));
        setState('error');
      }
      return undefined;
    }
  }, []);

  const retry = useCallback(() => {
    setState('ok');
    setErrorText(undefined);
    setNonce((n) => n + 1);
  }, []);

  return { state, errorText, guard, retry, nonce, ok: state === 'ok' };
}

export const FinanceStateView: React.FC<{
  state: FinanceLoadState;
  errorText?: string;
  onRetry: () => void;
  children?: React.ReactNode;
}> = ({ state, errorText, onRetry, children }) => {
  if (state === 'forbidden') {
    return <Result status="403" title="没有权限查看此页面" />;
  }
  if (state === 'error') {
    return (
      <Result
        status="error"
        title="加载失败"
        subTitle={errorText}
        extra={
          <Button type="primary" onClick={onRetry}>
            重试
          </Button>
        }
      />
    );
  }
  return <>{children}</>;
};

export const FinanceEmpty: React.FC<{ text: string }> = ({ text }) => (
  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={text} />
);
