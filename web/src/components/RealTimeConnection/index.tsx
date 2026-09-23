import { useModel } from '@umijs/max';
import React, { useEffect } from 'react';
import { startRealTime, stopRealTime } from '@/abp/signalr';
import { notificationRealTimeHandlers } from '@/components/NotificationBell/realTimeHandlers';

/**
 * T3.2 SignalR 连接生命周期挂载点（挂在 layout 的 childrenRender 内，
 * 在 umi Provider 树之内，可以读 @@initialState）。
 *
 * 登录后（currentUser 出现）建立连接；T3.5 的处理器（ReceiveNotification /
 * ReceiveUnreadCount）在 notificationRealTimeHandlers 注册。
 * 服务端 SignalR:Enabled=false 时不尝试连接，并把可用性置为 false，
 * T3.5 的铃铛据此把未读数切回轮询。
 * 登出断开在 AvatarDropdown 的退出逻辑，租户切换重连在登录页 switchTenant。
 */
const RealTimeConnection: React.FC = () => {
  const { initialState } = useModel('@@initialState');
  const userId = initialState?.currentUser?.userid;
  const signalrEnabled = initialState?.signalrEnabled ?? true;

  useEffect(() => {
    if (!userId) {
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
