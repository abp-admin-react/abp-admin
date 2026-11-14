import { QuestionCircleOutlined } from '@ant-design/icons';
import { Popover, Space } from 'antd';
import type { ReactNode } from 'react';

// A2/A2b/A4 源自《abp_admin 管理端问号说明文案 v1》。
// 运营页说明随实际页面和接口口径维护。
export type FinanceHelpPage = 'A1' | 'A2' | 'A2b' | 'A3' | 'A4' | 'A5' | 'A6' | 'A7';

const HELP: Record<FinanceHelpPage, ReactNode> = {
  A1: (
    <>
      <p>查看今日资金进出、借贷试算、账面余额和已确认收入。</p>
      <p>「有差异的对账批次」统计历史对账中发现差异的批次。出现告警时可打开「账务对账」查看批次记录。</p>
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
      <p>账簿调账用于补记或更正会计分录。调整用户资金账户可进入「用户余额调账」，审批后会同步生成会计凭证。</p>
      <p>账簿调账流程：建立草稿 → 填写金额、科目和原因 → 提交 → 审批通过后过账并生成凭证。</p>
      <ul>
        <li>被驳回的单可以看驳回意见，只有制单人能点「重新编辑」改完再交。</li>
        <li>记错的凭证不能删，只能「红冲」：系统生成一张反向凭证把它抵掉，原凭证保留可查。</li>
        <li>有审批权限的人员可在「账簿调账待审批」处理申请。默认不能审批自己的申请。</li>
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
      <p>账簿内部核对会检查所选过账期间的借贷合计，并对照分录和账户余额投影。表格显示每个批次的结果及差异项数。</p>
      <p>有执行权限时，可选择过账区间并开始对账。默认排除演示单据。</p>
    </>
  ),
  A6: (
    <>
      <p>按业务类型和业务单号查找会计凭证的来源。点「查看会计凭证」可打开相应凭证。</p>
      <ul>
        <li>代理只能看到自己的单据。</li>
      </ul>
    </>
  ),
  A7: (
    <>
      <p>按归属和币种查看账面可用、冻结和预收余额。账簿内部的核对结果可在「账务对账」中查看。</p>
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
        { title: '会计与对账' },
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
