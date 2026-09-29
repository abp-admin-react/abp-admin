import { useModel } from '@umijs/max';
import React, { useEffect } from 'react';
import { startRealTime, stopRealTime } from '@/abp/signalr';
import { notificationRealTimeHandlers } from '@/components/NotificationBell/realTimeHandlers';

/**
 * T3.2 SignalR 连接生命周期挂载点（挂在 layout 的 childrenRender 内，
 * 在 umi Provider 树之内，可以读 @@initialState）——连接的建立与关停都以这里为属主。
 *
 * 登录后（currentUser 出现）建立连接；T3.5 的处理器（ReceiveNotification /
 * ReceiveUnreadCount）在 notificationRealTimeHandlers 注册。
 * 服务端 SignalR:Enabled=false 时不尝试连接，并把可用性置为 false，
 * T3.5 的铃铛据此把未读数切回轮询。
 * 会话消失（currentUser 不再出现——菜单退出、401 内部跳转、空闲看门狗登出等一切路径）
 * 时对称关停：否则单例连接带着无限快速重连策略在登录页空转（无 token 的 negotiate
 * 循环 + 后台流量）。AvatarDropdown 退出时的直接 stopRealTime 保留作幂等双保险，
 * 租户切换重连在登录页 switchTenant。
 */
const RealTimeConnection: React.FC = () => {
  const { initialState } = useModel('@@initialState');
  const userId = initialState?.currentUser?.userid;
  const signalrEnabled = initialState?.signalrEnabled ?? true;

  useEffect(() => {
    if (!userId) {
      void stopRealTime();
      return;
    }
    if (!signalrEnabled) {
      // stopRealTime 是幂等的：无连接时只把可用性置为 false
      void stopRealTime();
      return;
    }
    void startRealTime(notificationRealTimeHandlers);
  }, [userId, signalrEnabled]);

  return null;
};

export default RealTimeConnection;
