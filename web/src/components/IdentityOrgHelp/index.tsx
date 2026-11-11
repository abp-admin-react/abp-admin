import { QuestionCircleOutlined } from '@ant-design/icons';
import { Link, useAccess } from '@umijs/max';
import { Popover, Space } from 'antd';
import type { ReactNode } from 'react';

export type IdentityOrgHelpPage = 'org' | 'roles' | 'users';

const INTRO: Record<IdentityOrgHelpPage, string> = {
  org: '维护部门和成员；需要批量授权时，可设置部门角色。',
  roles: '为角色配置功能权限，再分配给用户。',
  users: '维护账号，在编辑中分配角色。',
};

const HELP: Record<IdentityOrgHelpPage, ReactNode> = {
  org: (
    <>
      <p>部门记录人员归属，一个用户可以属于多个部门。</p>
      <p>
        通常只需维护部门和成员。可选的“部门统一角色”会授予该部门所有成员，适合批量分配相同权限。
      </p>
    </>
  ),
  roles: (
    <>
      <p>
        角色是一组功能权限，例如查看订单、审核付款。配置一次即可分配给多个用户。
      </p>
      <p>
        用户直接分配的角色与所在部门的统一角色共同生效。菜单根据功能权限显示，无需另配菜单权限。
      </p>
      <p>
        系统角色不能重命名或删除。“注册时默认分配”会将此角色分配给自行注册的新用户。
      </p>
    </>
  ),
  users: (
    <>
      <p>
        创建账号并分配角色，即可获得对应的功能权限。需要记录人员归属时，到部门中添加成员。
      </p>
      <p>
        通常通过角色授予权限。“更多”中的额外权限用于个别用户的特殊授权，账号安全操作也集中在这里。
      </p>
      <p>
        部门统一角色会自动叠加，需到对应部门调整；修改用户的直接角色不会移除部门授予的角色。
      </p>
    </>
  ),
};

export const IdentityOrgHelpIcon = ({
  page,
}: {
  page: IdentityOrgHelpPage;
}) => {
  const access = useAccess();
  return (
    <Popover
      trigger="click"
      placement="bottomLeft"
      content={
        <div style={{ maxWidth: 420, lineHeight: 1.7 }}>
          {HELP[page]}
          {page === 'roles' &&
            access.canManageRoleDataScopes &&
            access.canManageRoles && (
              <p>
                <Link to="/biz-template/data-scope">业务数据范围扩展</Link>
                ：仅在业务需要限制可见数据时配置。
              </p>
            )}
        </div>
      }
    >
      <QuestionCircleOutlined
        aria-label="页面说明"
        style={{ cursor: 'pointer' }}
      />
    </Popover>
  );
};

export function identityOrgHelpHeader(
  page: IdentityOrgHelpPage,
  label: string,
) {
  return {
    title: false as const,
    content: INTRO[page],
    breadcrumb: {
      items: [
        { title: '身份管理' },
        {
          title: (
            <Space size={6} align="end">
              <span>{label}</span>
              <IdentityOrgHelpIcon page={page} />
            </Space>
          ),
        },
      ],
    },
  };
}

export default IdentityOrgHelpIcon;
