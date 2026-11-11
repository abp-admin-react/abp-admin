import {
  AppstoreOutlined,
  AuditOutlined,
  BankOutlined,
  CalendarOutlined,
  CalculatorOutlined,
  CloudOutlined,
  DashboardOutlined,
  BellOutlined,
  ClockCircleOutlined,
  ClusterOutlined,
  ControlOutlined,
  ExperimentOutlined,
  DatabaseOutlined,
  DesktopOutlined,
  FileSearchOutlined,
  FileTextOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  FundOutlined,
  GlobalOutlined,
  HistoryOutlined,
  HddOutlined,
  MenuOutlined,
  MoneyCollectOutlined,
  MonitorOutlined,
  PayCircleOutlined,
  ProfileOutlined,
  SafetyOutlined,
  SearchOutlined,
  SendOutlined,
  ShoppingCartOutlined,
  SettingOutlined,
  SmileOutlined,
  TableOutlined,
  SwapOutlined,
  TeamOutlined,
  ThunderboltOutlined,
  UserOutlined,
  WalletOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { createElement } from 'react';

/** 服务端菜单和编辑器共用的 Ant Design 图标名称。 */
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
  dashboard: createElement(DashboardOutlined),
  shoppingCart: createElement(ShoppingCartOutlined),
  wallet: createElement(WalletOutlined),
  moneyCollect: createElement(MoneyCollectOutlined),
  send: createElement(SendOutlined),
  audit: createElement(AuditOutlined),
  search: createElement(SearchOutlined),
  monitor: createElement(MonitorOutlined),
  cloud: createElement(CloudOutlined),
  experiment: createElement(ExperimentOutlined),
  calculator: createElement(CalculatorOutlined),
  fund: createElement(FundOutlined),
  bank: createElement(BankOutlined),
  profile: createElement(ProfileOutlined),
  calendar: createElement(CalendarOutlined),
  swap: createElement(SwapOutlined),
  hdd: createElement(HddOutlined),
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
