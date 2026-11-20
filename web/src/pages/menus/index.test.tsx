import { fireEvent, render, screen, within } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getMenuTree, MENU_TYPE, type MenuTreeDto } from '@/abp/menus';
import MenuManagement from './index';

vi.mock('@umijs/max', () => ({
  useAccess: () => ({
    canManageMenus: true,
    canCreateMenus: true,
    canUpdateMenus: true,
    canDeleteMenus: true,
  }),
}));
vi.mock('@/abp/menus', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/menus')>()),
  getMenuTree: vi.fn(),
  getPermissionOptions: vi.fn().mockResolvedValue({ items: [] }),
  deleteMenu: vi.fn(),
}));
vi.mock('./components/MenuFormModal', () => ({ default: () => null }));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: React.PropsWithChildren) => children,
}));

function node(
  id: string,
  title: string,
  orderNo: number,
  children: MenuTreeDto[] = [],
): MenuTreeDto {
  return {
    id,
    title,
    orderNo,
    type: children.length ? MENU_TYPE.Catalog : MENU_TYPE.Menu,
    isEnabled: true,
    isHide: false,
    children,
  };
}
const leaf = {
  ...node('leaf', '订单管理', 20),
  anyPermissionNames: ['Orders.Read', 'Orders.Write'],
};
const sibling = node('sibling', '收款列表', 10);
const section = node('section', '交易配置', 1, [leaf, sibling]);
const root = node('root', '业务中心', 2, [section]);
const other = node('other', '工作台', 1);

function row(title: string) {
  const element = screen.getByText(title).closest('tr');
  if (!element) throw Error(`找不到菜单行 ${title}`);
  return within(element);
}
function search(q: string) {
  const input = screen.getByRole('searchbox', { name: '搜索菜单' });
  fireEvent.change(input, { target: { value: q } });
  fireEvent.keyDown(input, { key: 'Enter', code: 'Enter', keyCode: 13 });
  fireEvent.keyUp(input, { key: 'Enter', code: 'Enter', keyCode: 13 });
}

describe('菜单原生树表', () => {
  beforeEach(() => {
    vi.mocked(getMenuTree).mockResolvedValue({ items: [root, other] });
  });
  it('搜索折叠的孙菜单时保留两级祖先，清空后恢复展开状态，筛选后的父级仍不可删除', async () => {
    render(
      <App>
        <MenuManagement />
      </App>,
    );
    await screen.findByText('订单管理');
    fireEvent.click(row('业务中心').getByRole('button', { expanded: true }));
    expect(screen.queryByText('订单管理')).toBeNull();
    search('orders.write');
    expect(await screen.findByText('订单管理')).toBeVisible();
    expect(
      row('交易配置').getByRole('button', { expanded: true }),
    ).toBeVisible();
    expect(
      row('业务中心').getByRole('button', { expanded: true }),
    ).toBeVisible();
    expect(screen.queryByText('收款列表')).toBeNull();
    expect(screen.queryByText('工作台')).toBeNull();
    search('业务中心');
    expect(row('业务中心').queryByText('删除')).toBeNull();
    expect(
      row('业务中心').queryByRole('button', { expanded: true }),
    ).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '清除筛选' }));
    expect(
      row('业务中心').getByRole('button', { expanded: false }),
    ).toBeVisible();
    expect(screen.queryByText('订单管理')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '展开全部' }));
    expect(await screen.findByText('订单管理')).toBeVisible();
  });

  it('排序独立应用于同级节点，列条件与关键词必须在同一节点同时成立', async () => {
    render(
      <App>
        <MenuManagement />
      </App>,
    );
    await screen.findByText('订单管理');
    fireEvent.click(screen.getByRole('columnheader', { name: /排序/ }));
    const titles = screen
      .getAllByRole('row')
      .map((r) => r.querySelector('td')?.textContent);
    expect(titles.filter(Boolean)).toEqual([
      '工作台',
      '业务中心',
      '交易配置',
      '收款列表',
      '订单管理',
    ]);
    fireEvent.click(
      within(
        screen.getByRole('columnheader', { name: /绑定权限/ }),
      ).getByLabelText('筛选绑定权限'),
    );
    const field = await screen.findByRole('textbox', { name: '查询绑定权限' });
    fireEvent.change(field, { target: { value: 'orders.read' } });
    fireEvent.keyDown(field, { key: 'Enter', code: 'Enter', keyCode: 13 });
    expect(screen.queryByText('收款列表')).toBeNull();
    expect(await screen.findByText('订单管理')).toBeVisible();
    search('业务中心');
    expect(screen.queryByText('业务中心')).toBeNull();
    expect(screen.queryByText('订单管理')).toBeNull();
  });
  it('使用原生类型筛选器在完整树中查询，并抑制输入法确认时的提前搜索', async () => {
    render(
      <App>
        <MenuManagement />
      </App>,
    );
    await screen.findByText('订单管理');
    const header = screen.getByRole('columnheader', { name: /类型/ });
    fireEvent.click(within(header).getByRole('button'));
    const choice = await screen.findByRole('menuitem', { name: '目录' });
    fireEvent.click(choice);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(screen.queryByText('订单管理')).toBeNull();
    expect(row('交易配置').getByText('目录')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: '清除筛选' }));
    const field = screen.getByRole('searchbox', { name: '搜索菜单' });
    fireEvent.change(field, { target: { value: 'nothing-matches' } });
    fireEvent.keyDown(field, { key: 'Enter', code: 'Enter', keyCode: 229 });
    expect(screen.getByText('订单管理')).toBeVisible();
  });
});
