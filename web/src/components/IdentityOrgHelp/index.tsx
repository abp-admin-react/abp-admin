import { QuestionCircleOutlined } from '@ant-design/icons';
import { Popover, Space } from 'antd';
import type { ReactNode } from 'react';

/**
 * 身份四件套（组织单元/岗位/角色/用户）的问号说明 + 组合演示对照。
 * 口径：组织管「人在哪」、岗位管「什么职务」（不带权限）、角色管「能干什么」（权限集合），
 * 用户是三者的组合点；权限来源两条路——直接给角色 / 把角色绑到部门（进部门即得）。
 * 演示人物由 DemoIdentitySeedContributor 净库播种（口令 1q2w3E*），改样例先改种子再同步这里。
 */
export type IdentityOrgHelpPage = 'org' | 'posts' | 'roles' | 'users';

const DEMO_TABLE = (
  <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 12 }}>
    <thead>
      <tr>
        <th style={{ border: '1px solid #eee', padding: '4px 6px', textAlign: 'left' }}>演示人物</th>
        <th style={{ border: '1px solid #eee', padding: '4px 6px', textAlign: 'left' }}>部门（组织）</th>
        <th style={{ border: '1px solid #eee', padding: '4px 6px', textAlign: 'left' }}>岗位（职务）</th>
        <th style={{ border: '1px solid #eee', padding: '4px 6px', textAlign: 'left' }}>角色（权限来源）</th>
      </tr>
    </thead>
    <tbody>
      <tr>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>张智 zhang.cto</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>技术中心 + 研发部（一人多部门）</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>技术负责人</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>直接给「部门经理」</td>
      </tr>
      <tr>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>李工 li.dev</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>研发部</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>开发工程师 + 运维工程师（兼岗）</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>
          <b>没有直接角色</b>——来自「研发部」绑定的「普通员工」
        </td>
      </tr>
      <tr>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>王财 wang.fm</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>财务中心</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>财务经理</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>直接给「fin-accountant」（复用财务域角色库）</td>
      </tr>
      <tr>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>赵营销 zhao.mkt</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>市场部</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>市场专员</td>
        <td style={{ border: '1px solid #eee', padding: '4px 6px' }}>直接给「普通员工」（与李工同权限、来源不同的对照组）</td>
      </tr>
    </tbody>
  </table>
);

const HELP: Record<IdentityOrgHelpPage, ReactNode> = {
  org: (
    <>
      <p><strong>组织单元是什么</strong>：公司的部门树（集团 → 中心 → 部门），回答「人在哪个部门」。一个用户可以同时属于多个部门。</p>
      <ul>
        <li><strong>成员</strong>：往部门里挂人；调部门 = 移出旧部门再加入新部门。</li>
        <li><strong>绑定角色</strong>（组合玩法的关键）：把角色绑到部门后，该部门的所有成员<b>自动获得</b>这个角色的权限——新人入职加入部门即有权限，不必逐人配角色。</li>
        <li>演示里「研发部」绑了「普通员工」：李工（li.dev）没有任何直接角色，但他在研发部，所以拥有财务只读权限。</li>
      </ul>
      <p><strong>演示样例（净库自动播种，口令 1q2w3E*）</strong>：</p>
      {DEMO_TABLE}
    </>
  ),
  posts: (
    <>
      <p><strong>岗位是什么</strong>：用户的职务维度（技术负责人 / 开发工程师 / 财务经理…），回答「这人是什么职级/工种」。</p>
      <ul>
        <li><b>岗位不携带权限</b>——它是给人看的标签，用于人员列表展示和业务流转（比如审批单上显示申请人职务）；权限永远走「角色」。</li>
        <li>一个用户可以<b>同时挂多个岗位</b>：演示里的李工 = 开发工程师 + 运维工程师（一人兼岗）。</li>
        <li>停用的岗位不出现在用户表单的岗位下拉里（演示里的「测试工程师」就是停用态），已关联的用户不受影响。</li>
      </ul>
      <p><strong>演示样例（净库自动播种，口令 1q2w3E*）</strong>：</p>
      {DEMO_TABLE}
    </>
  ),
  roles: (
    <>
      <p><strong>角色是什么</strong>：权限的集合（能打开哪些菜单、能按哪些按钮），回答「能干什么」。</p>
      <ul>
        <li>给角色配好权限后，有两条路生效：<b>直接把角色分给用户</b>（用户页-角色），或<b>把角色绑到部门</b>（组织单元页-绑定角色，部门成员自动获得）。</li>
        <li>演示角色：「部门经理」= 组织 + 用户查看 + 财务只读；「普通员工」= 财务只读；财务域还内置 fin-accountant（全操作）/ fin-boss（只读+审批）。</li>
        <li>admin 是框架静态超管角色，不需要也不能改它的权限。</li>
      </ul>
      <p><strong>演示样例（净库自动播种，口令 1q2w3E*）</strong>：</p>
      {DEMO_TABLE}
    </>
  ),
  users: (
    <>
      <p><strong>用户是把三者组合起来的地方</strong>：给一个人挂<b>部门</b>（组织单元，人在哪）、挂<b>职务</b>（岗位，干什么的）、授<b>权限</b>（角色——直接给，或靠所在部门绑定的角色自动获得）。</p>
      <ul>
        <li>速记：<b>部门管「在哪」、岗位管「职务」、角色管「能干什么」</b>。</li>
        <li>岗位与部门是纯归属信息；只有角色影响菜单与按钮权限。</li>
        <li>演示里四个账号展示四种组合：跨部门 + 兼岗 + 权限来自部门绑定 + 复用域角色库。</li>
      </ul>
      <p><strong>演示样例（净库自动播种，口令 1q2w3E*，可直接登录体验权限差异）</strong>：</p>
      {DEMO_TABLE}
    </>
  ),
};

/** 点击弹出本页说明；挂在面包屑末项旁（与 FinanceHelp 同款形态）。 */
export const IdentityOrgHelpIcon = ({ page }: { page: IdentityOrgHelpPage }) => (
  <Popover
    trigger="click"
    placement="bottomLeft"
    content={<div style={{ maxWidth: 480, lineHeight: 1.7 }}>{HELP[page]}</div>}
  >
    <QuestionCircleOutlined
      aria-label="页面说明"
      style={{ color: '#8c8c8c', cursor: 'pointer', fontSize: 16, verticalAlign: 'middle' }}
    />
  </Popover>
);

/** PageContainer 页头：显式面包屑（末项 + ?），不依赖菜单权限推导（与 financeHelpHeader 同款）。 */
export function identityOrgHelpHeader(page: IdentityOrgHelpPage, label: string) {
  return {
    title: false as const,
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
