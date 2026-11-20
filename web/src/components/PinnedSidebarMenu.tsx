import type { ProLayoutProps } from '@ant-design/pro-components';
import { cloneElement, isValidElement } from 'react';

type MenuContentRender = Exclude<
  NonNullable<ProLayoutProps['menuContentRender']>,
  false
>;

const pinnedTemplateKeys = ['financial-management', 'administration', 'system'];

const renderPinnedSidebarMenu: MenuContentRender = (props, defaultMenu) => {
  const menuData = props.menuData ?? [];
  const pinnedGroups = pinnedTemplateKeys.flatMap((templateKey) =>
    menuData.filter(
      (item) => item.templateKey === templateKey && item.children?.length,
    ),
  );

  if (
    !pinnedGroups.length ||
    !isValidElement<Pick<ProLayoutProps, 'menuData'>>(defaultMenu) ||
    !('menuData' in defaultMenu.props)
  ) {
    return defaultMenu;
  }

  // 两个区域共用 ProLayout 的渲染逻辑，保留选中状态和内嵌展开。
  return (
    <div
      style={{ display: 'flex', flexDirection: 'column', blockSize: '100%' }}
    >
      <div style={{ flex: 1, minBlockSize: 0, overflow: 'auto' }}>
        {cloneElement(defaultMenu, {
          menuData: menuData.filter((item) => !pinnedGroups.includes(item)),
        })}
      </div>
      {/* 限制底部区域的高度，菜单较多时仍能滚动访问上方导航。 */}
      <div
        data-testid="pinned-sidebar-menu"
        style={{ flexShrink: 0, maxBlockSize: '75%', overflow: 'auto' }}
      >
        {cloneElement(defaultMenu, { menuData: pinnedGroups })}
      </div>
    </div>
  );
};

export default renderPinnedSidebarMenu;
