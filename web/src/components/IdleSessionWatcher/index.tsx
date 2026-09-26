import { useModel } from '@umijs/max';
import { App } from 'antd';
import React, { useEffect, useRef } from 'react';
import { logout } from '@/abp/oidc';

const ACTIVITY_EVENTS = [
  'mousemove',
  'mousedown',
  'keydown',
  'scroll',
  'touchstart',
] as const;

/** 跨标签页共享最近活动时间。`storage` 事件只在「其它标签」触发。 */
const LAST_ACTIVITY_KEY = 'abp-admin-idle-last-activity';

/** 写 localStorage / 重装定时器的最小间隔，避免 mousemove 打满主线程。 */
const THROTTLE_MS = 1000;

/**
 * T4.4：SPA 空闲超时。设置项 AbpAdmin.Account.IdleSessionTimeoutMinutes，0 关闭。
 * 活动时间写入 localStorage，其它标签通过 storage 事件重置倒计时，避免「一页闲着、另一页还在用」被踢。
 * 这只覆盖本浏览器源下的标签；服务端会话撤销仍由 Identity session 中间件负责。
 */
const IdleSessionWatcher: React.FC = () => {
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const minutes = Number(
    initialState?.settingValues?.[
      'AbpAdmin.Account.IdleSessionTimeoutMinutes'
    ] ?? 0,
  );
  const timerRef = useRef<number | undefined>(undefined);
  const lastWriteRef = useRef(0);

  useEffect(() => {
    // 三重闸门：未登录不启用；设置值是非法数字串→NaN 不启用；0/缺失
    // （Number(undefined ?? 0) === 0）= 功能显式关闭
    if (
      !initialState?.currentUser?.userid ||
      !Number.isFinite(minutes) ||
      minutes <= 0
    ) {
      return undefined;
    }

    const timeoutMs = minutes * 60 * 1000;

    const readLastActivity = (): number => {
      try {
        const raw = window.localStorage.getItem(LAST_ACTIVITY_KEY);
        const parsed = raw ? Number(raw) : NaN;
        if (Number.isFinite(parsed) && parsed > 0) {
          return parsed;
        }
      } catch {
        // 隐私模式写不了 storage 时退回本标签计时。
      }
      return Date.now();
    };

    // 以 fromTs 为基准重装登出定时器；remain 夹到 0：基准时间已超期时立即触发登出，
    // 而不是给 setTimeout 传负数（会当作 0，行为一样，但语义要写明是有意的）
    const arm = (fromTs: number) => {
      if (timerRef.current) {
        window.clearTimeout(timerRef.current);
      }
      const remain = Math.max(timeoutMs - (Date.now() - fromTs), 0);
      timerRef.current = window.setTimeout(async () => {
        message.warning('会话已空闲超时，即将退出登录');
        await logout();
      }, remain);
    };

    const markActivity = () => {
      const now = Date.now();
      if (now - lastWriteRef.current < THROTTLE_MS) {
        return;
      }
      lastWriteRef.current = now;
      try {
        window.localStorage.setItem(LAST_ACTIVITY_KEY, String(now));
      } catch {
        // 写失败不影响本标签倒计时。
      }
      arm(now);
    };

    const onStorage = (event: StorageEvent) => {
      if (event.key !== LAST_ACTIVITY_KEY || !event.newValue) {
        return;
      }
      const ts = Number(event.newValue);
      if (!Number.isFinite(ts) || ts <= 0) {
        return;
      }
      arm(ts);
    };

    arm(readLastActivity());
    window.addEventListener('storage', onStorage);
    ACTIVITY_EVENTS.forEach((eventName) =>
      window.addEventListener(eventName, markActivity, { passive: true }),
    );
    return () => {
      if (timerRef.current) {
        window.clearTimeout(timerRef.current);
      }
      window.removeEventListener('storage', onStorage);
      ACTIVITY_EVENTS.forEach((eventName) =>
        window.removeEventListener(eventName, markActivity),
      );
    };
  }, [initialState?.currentUser?.userid, minutes, message]);

  return null;
};

export default IdleSessionWatcher;
