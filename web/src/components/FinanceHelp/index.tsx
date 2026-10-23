import { QuestionCircleOutlined } from '@ant-design/icons';
import { Popover, Space } from 'antd';
import type { ReactNode } from 'react';

// 文案来源：产品《abp_admin 管理端问号说明文案 v1》，必须原样照搬；改文案先改那份文档再同步这里。
// 口径是框架通用账，禁止出现具体业务模块的科目/模板/业务词。
export type FinanceHelpPage = 'A1' | 'A2' | 'A2b' | 'A3' | 'A4' | 'A5' | 'A6' | 'A7';

const HELP: Record<FinanceHelpPage, ReactNode> = {
  A1: (
    <>
      <p><strong>这页看什么</strong>：钱整体对不对。看三件事：借贷是否平衡、今天进出多少、还有几笔没对上。</p>
      <ul>
        <li>老板：每天看一眼。显示「平衡」且未对上为 0 就没问题；变红就点进去看对账。</li>
        <li>会计：有红标先去「对账」查原因。</li>
      </ul>
    </>
  ),
  A2: (
    <>
      <p><strong>科目是什么</strong>：账本里的分类，比如「银行存款」「主营收入」「管理费用」。每笔钱都要记到某个科目上。</p>
      <ul>
        <li>系统科目带锁，不能改也不能删。</li>
        <li>已经记过账的科目不能删，也不能改类型和方向；不用了就「停用」。</li>
        <li>会计：业务需要新分类时在这里新建。编号不能重复，一般先建上级再建下级。</li>
        <li>老板：一般只看不改。</li>
      </ul>
    </>
  ),
  A2b: (
    <>
      <p><strong>过账规则是什么</strong>：系统自动记账的规则，规定某类业务发生时记到哪些科目，比如收到款记「银行存款」增加、「预收」增加。</p>
      <ul>
        <li>这页只读。规则由系统管理员发布，会计在这里核对规则对不对。</li>
      </ul>
    </>
  ),
  A3: (
    <>
      <p><strong>凭证是什么</strong>：每一笔记账的记录。一张凭证里「借」和「贷」的金额必须相等。系统自动记的账也会生成凭证。</p>
      <p><strong>调账是什么</strong>：系统没自动记上、或者记错了，需要人工补的一笔账。</p>
      <p>流程：会计建草稿 → 填清金额、科目和原因 → 提交 → 另一个人审批 → 审批通过后自动生成凭证。</p>
      <ul>
        <li>被驳回的单可以看驳回意见，只有制单人能点「重新编辑」改完再交。</li>
        <li>记错的凭证不能删，只能「红冲」：系统生成一张反向凭证把它抵掉，原凭证保留可查。</li>
        <li>会计：负责建单、提交、红冲。默认不能批自己的单。</li>
        <li>老板：在「待审批」里批或驳回。驳回时要写原因。</li>
      </ul>
    </>
  ),
  A4: (
    <>
      <p><strong>期间是什么</strong>：按月划分的记账区间，用来按月看账。</p>
      <ul>
        <li>第一阶段只看不结账，月结后面上线。</li>
      </ul>
    </>
  ),
  A5: (
    <>
      <p><strong>对账是什么</strong>：系统自动检查账有没有问题：借贷是否相等、各人余额和账本是否一致、收款渠道的记录和账本是否一致。</p>
      <ul>
        <li>有差异会标红，点进去能跳到对应凭证或单据。</li>
        <li>会计：可以手动「跑对账」，发现差异后查原因，需要补账就去建调账。</li>
        <li>老板：看结果，差异为 0 即可。</li>
      </ul>
    </>
  ),
  A6: (
    <>
      <p><strong>这页看什么</strong>：订单、收款、支付等业务单据，只读。点「关联凭证」能看这笔业务记了哪些账。</p>
      <ul>
        <li>代理只能看到自己的单据。</li>
      </ul>
    </>
  ),
  A7: (
    <>
      <p><strong>这页看什么</strong>：每个用户现在有多少钱：可用、冻结、预收。数字来自账本，和账本不一致时会标红并能跳到对账。</p>
      <ul>
        <li>代理只能看到自己名下的用户。</li>
      </ul>
    </>
  ),
};

/** 点击弹出本页说明；挂在面包屑末项旁（页头 title 行被密度优化隐藏）。 */
export const FinanceHelpIcon = ({ page }: { page: FinanceHelpPage }) => (
  <Popover
    trigger="click"
    placement="bottomLeft"
    content={<div style={{ maxWidth: 420, lineHeight: 1.7 }}>{HELP[page]}</div>}
  >
    <QuestionCircleOutlined
      aria-label="页面说明"
      className="finance-help-icon"
      style={{ color: '#8c8c8c', cursor: 'pointer', fontSize: 16, verticalAlign: 'middle' }}
    />
  </Popover>
);

/**
 * PageContainer 页头：显式面包屑（末项 + ?），不依赖菜单权限推导。
 * 原因：无 Finance 菜单权限时自动面包屑为空，再叠加隐藏 title 行会整页无标题。
 */
export function financeHelpHeader(page: FinanceHelpPage, label: string) {
  return {
    title: false as const,
    breadcrumb: {
      items: [
        { title: '财务管理' },
        {
          title: (
            // align="end" 与 global.less 页头面包屑「末项放大为标题 + ol 基线对齐」配套：
            // 末项文字 20px、问号 16px，问号按底边对齐文字（CJK 无下降部，底边≈基线）。
            // 改动对齐方式需与 global.less 的面包屑规则同步评估。
            <Space size={6} align="end">
              <span>{label}</span>
              <FinanceHelpIcon page={page} />
            </Space>
          ),
        },
      ],
    },
  };
}

export default FinanceHelpIcon;
