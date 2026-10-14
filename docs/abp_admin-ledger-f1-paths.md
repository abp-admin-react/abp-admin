# F1 HTTP 路径表（与前端锁约一致 · Swagger 已核）

前缀：ABP 约定 `/api/app/...`（**无** `/api/finance`）

| 页面 | 方法 | 路径 | 说明 |
|------|------|------|------|
| A1 总览 | GET | `/api/app/fin-overview` | FinOverviewAppService.Get |
| A2b 规则 | GET | `/api/app/fin-posting-rule` | 只读列表 |
| A3 凭证列表 | GET | `/api/app/fin-voucher` | |
| A3 凭证详情 | GET | `/api/app/fin-voucher/{id}` | |
| A3 红冲 | POST | `/api/app/fin-voucher/{id}/reverse` | |
| A3 调账列表 | GET | `/api/app/fin-manual-adjust` | DTO 含 applicantId/auditorId/rejectReason |
| A3 调账详情 | GET | `/api/app/fin-manual-adjust/{id}` | |
| A3 新建 | POST | `/api/app/fin-manual-adjust` | body 见下方「建单/改草稿」 |
| A3 改草稿 | PUT | `/api/app/fin-manual-adjust/{id}` | 仅 Draft；body 同建单 |
| A3 删草稿 | DELETE | `/api/app/fin-manual-adjust/{id}` | 仅 Draft |
| A3 提交 | POST | `/api/app/fin-manual-adjust/{id}/submit` | 需 Submit；Draft→Pending；**reason 必填**（空→400 InvalidInput Fields=reason） |
| A3 审批 | POST | `/api/app/fin-manual-adjust/{id}/approve` | 需 Approve；自批另需 ApproveOwn，否则 403 SelfApproveNotAllowed |
| A3 驳回 | POST | `/api/app/fin-manual-adjust/{id}/reject` | 需 Reject；自批另需 ApproveOwn；body `{ "rejectReason": "..." }` 必填 |
| A3 重开 | POST | `/api/app/fin-manual-adjust/{id}/reopen` | Rejected→Draft；仅制单人+Create |
| A3 调账红冲 | POST | `/api/app/fin-manual-adjust/{id}/reverse` | 需 Adjust.Reverse；Posted→Reversed |
| A4 当前期 | GET | `/api/app/fin-period/current` | 无关账 |
| A5 对账列表 | GET | `/api/app/fin-reconcile?includeDemo=false` | 默认排除演示 |
| A5 跑对账 | POST | `/api/app/fin-reconcile/run` | body/includeDemo |
| A6 业务单 | GET | `/api/app/fin-biz-doc?agentId=&isDemo=` | Scope.Agent 强制本代理 |
| A7 余额投影 | GET | `/api/app/fin-ledger/sub-account-balances?agentId=` | 同上 |
| 试算/单余额 | GET | `/api/app/fin-ledger/trial-balance`、`/balance` | |
| 演示过账 | POST | `/api/app/fin-demo-event` | 仅 demo.*；真实业务走进程内集成 |
| 当前用户 | GET | `/api/abp/application-configuration` | `currentUser.id` 比对 applicantId/auditorId |


## A3 建单/改草稿 body（`CreateUpdateFinManualAdjustDto`）

一阶段表单：金额、币种、借方科目、归属方、原因；**不做多行分录**。贷方缺省 `3101` 人工调整。

```json
{
  "amount": 15,
  "currency": "CNY",
  "debitAccountCode": "1002",
  "debitOwnerType": "Channel",
  "debitOwnerId": "default",
  "creditAccountCode": null,
  "creditOwnerType": "Platform",
  "creditOwnerId": null,
  "reason": "说明"
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `amount` | 是 | >0 |
| `currency` | 是 | `[Required]`，如 `CNY` |
| `debitAccountCode` | 是 | `[Required]`，如渠道清算 `1002` |
| `debitOwnerType` | 否 | `FinOwnerType`：Platform=0 / User=1 / Agent=2 / Channel=3；默认 Platform |
| `debitOwnerId` | 否 | 归属方 ID；Channel 示例 `default` |
| `creditAccountCode` | 否 | 空则贷方 `3101` |
| `creditOwnerType` / `creditOwnerId` | 否 | 默认 Platform / 空 |
| `reason` | 建/改草稿否；**提交时必填** | 原因；**不是** `summary`。提交时空/空白 → 400 InvalidInput，`data.Fields=reason` |

不要传：`summary`、`lines`、`applicantId`（申请人取当前用户）。

进程内：`ILedgerIntegrationService`（`[RemoteService(false)]`，无 HTTP）

## 调账职责分离：权限配置（2026-10-14，海峰定：不写死，老板在角色权限页自己配）

代码里不再按角色名或"申请人≠审批人"写死，每个动作只看自己的权限码；admin 也走同样检查，没有绕过。

| 权限码 | 中文显示名 | 用途 | 默认授予 |
|---|---|---|---|
| `Finance.Adjust.Create` | 创建调账 | 新建；改/删草稿；reopen | fin_accountant |
| `Finance.Adjust.Submit` | 提交调账 | Draft→Pending | fin_accountant |
| `Finance.Adjust.Approve` | 审批调账 | Pending→Posted | fin_accountant、fin_boss |
| `Finance.Adjust.Reject` | 驳回调账 | Pending→Rejected | fin_accountant、fin_boss |
| `Finance.Adjust.Reverse` | 冲销调账 | Posted→Reversed | fin_boss |
| `Finance.Adjust.ApproveOwn` | 可审批自己提交的调账单 | 审批/驳回 applicantId==当前用户 时额外要求 | 不授予任何角色 |

保留（单据归属，不算角色写死）：改/删/提交只能申请人本人（`Finance:Adjust:NotApplicant`），reopen 只能申请人本人（`Finance:Adjust:ReopenDenied`）。

| 错误码 | HTTP | 触发 |
|---|---|---|
| `Finance:Adjust:SelfApproveNotAllowed` | 403 | 审批/驳回自己的单且无 ApproveOwn（**取代**旧 `Finance:Adjust:SelfApproveForbidden`） |
| `Finance:Adjust:CreateDenied` | 403 | 无 Create（建/改/删/reopen） |
| `Finance:Adjust:SubmitDenied` | 403 | 无 Submit（新增） |
| `Finance:Adjust:ApproveDenied` | 403 | 无 Approve |
| `Finance:Adjust:RejectDenied` | 403 | 无 Reject（新增） |
| `Finance:Adjust:ReverseDenied` | 403 | 无 Adjust.Reverse（新增；调账冲销不再看 Voucher.Reverse） |
