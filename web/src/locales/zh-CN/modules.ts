import type english from '../en-US/modules';

export default {
  'pages.modules.title': '模块管理',
  'pages.modules.platform': '平台管理',
  'pages.modules.name.Orders': '订单',
  'pages.modules.name.Collection': '收款',
  'pages.modules.name.Chain': '链上充提',
  'pages.modules.name.Finance': '财务工作区',
  'pages.modules.column.module': '模块',
  'pages.modules.column.status': '状态',
  'pages.modules.column.dependencies': '依赖',
  'pages.modules.column.actions': '操作',
  'pages.modules.status.pending': '待初始化',
  'pages.modules.status.enabled': '已启用',
  'pages.modules.status.disabled': '停止受理',
  'pages.modules.configure': '配置',
  'pages.modules.configureLabel': '配置{module}',
  'pages.modules.history': '历史记录',
  'pages.modules.historyLabel': '查看{module}历史记录',
  'pages.modules.enable': '启用',
  'pages.modules.disable': '停用',
  'pages.modules.enableTitle': '启用{module}',
  'pages.modules.disableTitle': '停用{module}',
  'pages.modules.confirm': '确认',
  'pages.modules.cancel': '取消',
  'pages.modules.close': '关闭',
  'pages.modules.retry': '重试',
  'pages.modules.fetchError': '加载模块状态失败',
  'pages.modules.uninitialized': '部署模块尚未初始化',
  'pages.modules.previewError': '无法预览状态变更，已重新读取模块状态。',
  'pages.modules.updateSuccess': '模块状态已更新',
  'pages.modules.updateError':
    '模块状态更新失败，已重新读取模块状态。请核对后重试。',
  'pages.modules.blocked': '当前无法停用',
  'pages.modules.blockedBy': '请先处理仍依赖此模块的启用项：{modules}',
  'pages.modules.suspension.Orders':
    '停止创建订单、生成新报价和发起新支付。已发起的支付和订单后续处理继续。',
  'pages.modules.suspension.Collection':
    '停止创建新收款和首次余额扣款。已发起的支付可继续查单、回调和退款。',
  'pages.modules.suspension.Chain':
    '停止生成新充值地址和受理新提现。旧地址到账、已提交提现和归集继续处理。',
  'pages.modules.suspension.Finance':
    '停止新建和提交人工账簿调账。已受理事项和集成过账继续按原规则处理。',
} satisfies Record<keyof typeof english, string>;
