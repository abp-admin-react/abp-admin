import { fireEvent, render, screen } from '@testing-library/react';
import { Table } from 'antd';
import { describe, expect, it, vi } from 'vitest';
import { MENU_TYPE, type MenuTreeDto } from '@/abp/menus';
import { buildMenuColumns } from './menuColumns';
import type { MenuRow } from './menuTree';

vi.mock('@umijs/max', () => ({ request: vi.fn() }));

const catalog: MenuTreeDto = {
  id: 'catalog-1',
  title: '业务管理',
  type: MENU_TYPE.Catalog,
  orderNo: 1,
  isHide: false,
  isEnabled: true,
  children: [],
};

describe('菜单操作权限', () => {
  it('新增子级需要创建权限，编辑权限与创建权限分别控制各自操作', () => {
    const onEdit = vi.fn();
    const onAddChild = vi.fn();
    const table = (canCreateMenus: boolean, canUpdateMenus: boolean) => (
      <Table<MenuRow>
        rowKey={(row) => row.menu.id}
        pagination={false}
        dataSource={[{ menu: catalog }]}
        columns={buildMenuColumns(
          {
            canCreateMenus,
            canUpdateMenus,
            canDeleteMenus: false,
            onEdit,
            onAddChild,
            onDelete: vi.fn(),
          },
          {},
        )}
      />
    );
    const view = render(table(false, true));
    expect(screen.queryByText('新增子级')).toBeNull();
    fireEvent.click(screen.getByText('编辑'));
    expect(onEdit).toHaveBeenCalledWith(catalog);
    expect(onAddChild).not.toHaveBeenCalled();

    view.rerender(table(true, false));
    expect(screen.queryByText('编辑')).toBeNull();
    fireEvent.click(screen.getByText('新增子级'));
    expect(onAddChild).toHaveBeenCalledWith(catalog);
  });
});
