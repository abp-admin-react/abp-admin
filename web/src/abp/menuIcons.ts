import {
  AppstoreOutlined,
  BellOutlined,
  ClockCircleOutlined,
  ClusterOutlined,
  ControlOutlined,
  DatabaseOutlined,
  DesktopOutlined,
  FileSearchOutlined,
  FileTextOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  GlobalOutlined,
  HistoryOutlined,
  MenuOutlined,
  PayCircleOutlined,
  SafetyOutlined,
  SettingOutlined,
  SmileOutlined,
  TableOutlined,
  TeamOutlined,
  ThunderboltOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { createElement } from 'react';

/**
 * 动态菜单图标映射：后端下发的 icon 字符串名 → antd 图标组件。
 * 名单与后端 MenuManager 模板定义及 config/routes.ts 的 icon 字段保持一致。
 */
const menuIcons: Record<string, ReactNode> = {
  smile: createElement(SmileOutlined),
  appstore: createElement(AppstoreOutlined),
  team: createElement(TeamOutlined),
  safety: createElement(SafetyOutlined),
  control: createElement(ControlOutlined),
  global: createElement(GlobalOutlined),
  fileText: createElement(FileTextOutlined),
  fileSearch: createElement(FileSearchOutlined),
  folder: createElement(FolderOutlined),
  payCircle: createElement(PayCircleOutlined),
  setting: createElement(SettingOutlined),
  desktop: createElement(DesktopOutlined),
  thunderbolt: createElement(ThunderboltOutlined),
  clockCircle: createElement(ClockCircleOutlined),
  bell: createElement(BellOutlined),
  table: createElement(TableOutlined),
  folderOpen: createElement(FolderOpenOutlined),
  database: createElement(DatabaseOutlined),
  user: createElement(UserOutlined),
  menu: createElement(MenuOutlined),
  cluster: createElement(ClusterOutlined),
  history: createElement(HistoryOutlined),
};

export const menuIconNames = Object.keys(menuIcons);

/** 后端 icon 字符串转 ProLayout 菜单项可用的 ReactNode；未知名称返回 undefined（不显示图标）。 */
export function toMenuIcon(icon?: string | null): ReactNode | undefined {
  if (!icon) {
    return undefined;
  }
  return menuIcons[icon];
}

export { menuIcons };
