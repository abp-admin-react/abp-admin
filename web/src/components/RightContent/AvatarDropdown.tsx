import {
  LogoutOutlined,
  SkinOutlined,
  SwapOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { history, useModel } from '@umijs/max';
import type { MenuProps } from 'antd';
import { Spin } from 'antd';
import React, { startTransition } from 'react';
import { logout, switchAccount } from '@/abp/oidc';
import { stopRealTime } from '@/abp/signalr';
import HeaderDropdown from '../HeaderDropdown';

type GlobalHeaderRightProps = {
  children?: React.ReactNode;
};

const menuItems: MenuProps['items'] = [
  {
    key: 'center',
    icon: <UserOutlined />,
    label: '个人中心',
  },
  {
    key: 'theme',
    icon: <SkinOutlined />,
    label: '主题设置',
  },
  {
    type: 'divider' as const,
  },
  {
    key: 'switch-account',
    icon: <SwapOutlined />,
    label: '切换账号',
  },
  {
    key: 'logout',
    icon: <LogoutOutlined />,
    label: '退出登录',
  },
];

const loginOut = async () => {
  try {
    // T3.2：登出先断开 SignalR 连接（跳转 IdP 登出页之前）
    await stopRealTime();
    await logout();
  } catch {
    history.replace('/user/login');
  }
};

export const AvatarDropdown: React.FC<GlobalHeaderRightProps> = ({
  children,
}) => {
  const { initialState, setInitialState } = useModel('@@initialState');

  const onMenuClick: MenuProps['onClick'] = (event) => {
    const { key } = event;
    if (key === 'logout') {
      startTransition(() => {
        setInitialState((s) => ({ ...s, currentUser: undefined }));
      });
      loginOut();
      return;
    }
    if (key === 'switch-account') {
      // prompt=select_account → IdP 账号选择页（继续当前账号 / 换账号）
      startTransition(() => {
        setInitialState((s) => ({ ...s, currentUser: undefined }));
      });
      stopRealTime().finally(() => {
        switchAccount().catch(() => history.replace('/user/login'));
      });
      return;
    }
    if (key === 'theme') {
      setInitialState((s) => ({ ...s, settingDrawerOpen: true }));
      return;
    }
    if (key === 'center') {
      history.push('/account/center');
      return;
    }
  };

  if (!initialState) {
    return <Spin size="small" />;
  }

  const { currentUser } = initialState;

  if (!currentUser) {
    return <Spin size="small" />;
  }

  return (
    <HeaderDropdown
      placement="bottomRight"
      menu={{
        selectedKeys: [],
        onClick: onMenuClick,
        items: menuItems,
      }}
      arrow
    >
      {children}
    </HeaderDropdown>
  );
};
