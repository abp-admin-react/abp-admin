# Collection 提现（M2/M3 实现说明 + 运维手册）

参考 E:\code\exchange\exchange 的提现闭环（冻结→审核→广播→对账）落在 Collection 模块内，
账本走 `ILedgerIntegrationService`（首个真实 F3 业务消费者）。

## 状态机（ColWithdrawOrders.Status，WithdrawStatus）

```
Pending(0) ──审批(小额自动/人工)──→ Processing(1) ──取件──→ Broadcasting(2) ──回执成功──→ Completed(3)
   │ 用户撤单                                    │                    │
   ↓                                            │              回执失败(未超限) → 回 Processing（RetryCount+1）
Cancelled(6)                                    │              回执失败(超限)   → BroadcastFailed(5)
   │                                            │              无哈希超时        → BroadcastFailed「需人工核查」
Pending ──拒绝──→ Rejected(4)                    └── 发送异常（结果未知）──→ 保持 Broadcasting 无哈希
BroadcastFailed ──重试(验回执/双花门)──→ Processing
BroadcastFailed ──退款拒绝──→ Rejected（净额+手续费退回可用）
无哈希超时 ──其他已完结提现占用同 sender nonce（可证未出款）──→ 自动回 Processing 重播（RetryCount+1）/ 超限自动退款→ Rejected
无哈希超时 ──本地 ColChainTxAttempts / Etherscan 核实 nonce（在途/已挖）──→ 认领哈希 → 回执路径完结
```

- 所有迁移 CAS 条件 UPDATE，0 行 = `WithdrawInvalidStatus`。
- **无人工的自动仲裁（无哈希超时）**：发送**前**在 `ColWithdrawOrders` 提交 `BroadcastSenderAddress` + `NonceUsed` 并在 `ColChainTxAttempts` 落库签名尝试（`GasPriceWei`/`RawSignedTx`）：
  - 若同发送方同一 `NonceUsed` 已被**另一笔已完结（`Completed`）提现**占用＝**可证未出款**：
    未超重试上限自动回 Processing 重播（新 nonce，并按 `RetryCount` 抬升 gas）；超限**自动退款**终态（净额+手续费回可用，告警留痕）。
  - 优先核对本地 `ColChainTxAttempts` 回执，未命中再通过 Etherscan/地址扫描按 `(from, to, ActualAmount, NonceUsed)` 核验并认领哈希，交给正常回执路径完结（绝不仅凭模糊 `pending <= NonceUsed` 重播或退款，防止负载漂移误伤）。
  - 回执失败（revert＝可证未出款）+ 重试用尽 → 同样自动退款。
- **双花与卡池防线**：
  - 归集打油（`ChainTxSlotCoordinator` 同网 `AccountSlot` 串行 + 3 分钟队头同 nonce +10%+1 wei 加价替换 + `ColNetwork.SponsorWatch*` 持久化 5 分钟无主队头看门狗 + 差额预算超限延后等待回落）、归集转账（超时重签按 `RequiredSweepGasWei` 抬价 + 地址 BNB 不足退回补油绝不静默压价 + 打油前安全块 `balanceOf` / `eth_call` 预模拟与有界链上 `discover` + 连续 3 轮不足才转人工）、提现广播（`AccountSlot` 锁内 `AllocateSerialNonceAsync` 单调分配 + 按 `RetryCount` 抬价 + 3 分钟卡死队头同 nonce 加价替换 + 全候选回执解析与热钱包 BNB 不足拒发）全链路落库 `ColChainTxAttempts` 审计。
  - 扫链 RPC `eth_getLogs` 对齐 Ankr BSC 硬限制（单块兜底 `topic2` 按 32 地址分批 + 单块内连续 2 次不明确 `-32000` 熔断故障转移；区间限制触发半窗口降载 + AIMD 自适应块窗）。
  - 管理端重试另有 `HasOtherCompletedAttemptAsync` 证据门；nonce 取值区分单调提现串口（`AccountSlot` + `max(latest, pending, openAttempts)`）与串行打油（`latest` 防负载节点 `pending` 视图漂移）。

## 余额两桶（ColBalances，M1）

`Balance = 可用`、`Frozen = 提现冻结`，合计 = Balance + Frozen（金库核对/开账回填按合计）。
原语：`CollectionBalanceManager.Freeze/Unfreeze/DeductFrozen/Refund`，幂等键
`W:{提现单 Id:N}:{动作}`（BalanceIdempotencyKeys 唯一定义处）。
两桶审计不变量见 `BalanceAuditJobHandler`（可用桶剔除 WithdrawSettle 行；冻结桶按动作方向合计）。

### 储备核对

每次核对保存代币、金库地址、参考区块以及同一本地快照中的可用和冻结余额。核对后差额 = 参考区块金库余额 − 本地用户余额合计 − 已到账待记账净额 + 参考区块后归集且本地已记账的净额。只有归集交易的成功回执、代币转账和本地订单金额能够对应，才计入时差。

运营页保留原始差额。核对后仍为负数时，按剩余缺口告警。有已核实的时间差且覆盖了缺口时，页面显示“记账时差”，同时列出核对后余额差。回执或记账依据不足时单独提示“待核实”。升级前的记录显示“历史记录”，可在核对时间列查看当时的原始观测。

提现钱包与金库共用地址时，未闭合的出款，以及无法证明已在参考块完成的出款，会令本次核对进入“待核实”。如果节点未返回有效的确认块余额，系统单独告警，看板继续显示最近一次成功取得余额的核对时间。

## 账本过账（BizType=withdraw，七 phase）

| 动作 | Phase | 借 | 贷 | 金额口径 |
|---|---|---|---|---|
| 提交冻结 | freeze | 2201 | 2202 | 总额（净+费） |
| 撤单/拒绝 | unfreeze | 2202 | 2201 | 总额 |
| 审核通过 | approve | 2202 | **2241 应付** | 净额 |
| 手续费实现 | fee-take | 2202 | 6003 | Fee（0 跳过） |
| 打款完成 | payout | 2241 | 1001 | 净额（计入 ExpensePhases） |
| 失败退款 | refund | 2241 | 2201 | 净额 |
| 退手续费 | fee-refund | 6003 | 2201 | Fee（0 跳过） |

- 待付中转用 **2241 应付账款**（框架已种）而非 1002 渠道清算：1002 是资产类借方科目，
  审核时贷记它等于链上资产提前减少（原计划表的账务方向修正，已记 ledger-f1-decisions）。
- 充值入账（BizType=deposit, phase=pay，借 1001 / 贷 2201）在归集确认同事务过账（SweepManager）。
- 科目/模板种子：`CollectionLedgerSeedProvider`（ILedgerSeedProvider 扩展点，宿主 DependsOn 即播种）。
- 币种码统一大写 `USDT`/`USDC`（`WithdrawLedgerPoster.CurrencyCode`——枚举 ToString 是 "Usdt"，
  大小写错位会让账本写入与查询互不可见，实测踩过）。
- 存量开账回填：管理端 `POST /api/app/collection-withdraw-admin/backfill-opening`
  （BizType=wallet, phase=post，BizRefId=`opening:{userId}:{currency}`，幂等可重跑）。

## 护栏（WithdrawGuard）

三道检查：审批端点/自动审前 → 广播前 → 签名临界区内发送前（总闸，关 TOCTOU）：
- 热钱包代币余额 ≥ 净额，否则 `HotWalletInsolvent`；
- 低于 `Collection.Withdraw.HotWalletUsdtFloor`（默认 1000）= **熔断**：阻断所有审批并告警
 （`[Alert:WithdrawHotWallet]`），补币后自动恢复；
- BNB < `Collection.Withdraw.MinHotWalletBnb`（默认 0.005）= `HotWalletGasShort`；
- 运行时总闸 `Collection.Withdraw.Halt`（SettingUi 可切）。

## 配置与开关

- 宿主配置（appsettings `Collection:Withdraw`）：`Enabled`（默认 **false**：提交拒绝、worker 睡眠）、
  `BroadcastIntervalSeconds`(15)、`RoundBudgetSeconds`(120)、`BroadcastBatchSize`(5)、`AutoApproveBatchSize`(20)。
- 运行期设置（SettingUi，`Collection.Withdraw.*`）：MinAmount(10)、FeeRate(0.004，8 位截断)、
  MaxDailyAmount(50000，空=不限)、MaxDailyCount(3)、AutoApproveMaxAmount(100)、Halt(false)、
  BroadcastTimeoutMinutes(30)、MaxRetries(3)、HotWalletUsdtFloor(1000)、MinHotWalletBnb(0.005)。
- 权限：`AbpAdmin.Collection.WithdrawAdmin`（Host 专用；管理端服务 AssertHost 双保险）。
- worker 注册：宿主 `AddCollectionChainWorkers`（Redis 锁守卫同扫链/归集）；DbMigrator 不注册。

## 上线步骤（运维手册）

1. 迁移落库（Collection 迁移链 +1：AddBalanceFrozen / AddWithdrawOrders / AddWithdrawHotWallet）。
2. 账本种子已随 IDataSeeder 播种（1001/2201/2202/6003 + 七条模板）；确认 `Ledger:Enabled=true`。
3. 生成独立热钱包（**与归集助记词不同源**），管理端
   `POST /api/app/collection-withdraw-admin/set-withdraw-vault`（body 传地址+私钥，
   服务端校验私钥与地址匹配，密文入库）。
4. 执行 `backfill-opening` 一次性开账（把存量 ColBalance 合计差额过账进 2201）。
5. SettingUi 调限额/费率/地板值 → appsettings 开 `Collection:Withdraw:Enabled=true` 重启。
6. 日常：管理页 `/collection/withdrawals`（状态卡 + 四态审核）；熔断告警走
   `[Alert:WithdrawHotWallet]` 日志 + ChainAlertService 站内信。
7. 卡单处置：**全自动**（nonce 仲裁：可证未出款自动重播/退款；交易在途自动认领哈希完结）。
   管理端重试/人工完结/退款拒绝保留为兜底工具（极端脏数据/审计需要时用），正常流程不需要人。

## 已知边界（后续里程碑）

- 人工完结优先核对原发送方 `BroadcastSenderAddress`、`NonceUsed` 与链上 ERC20 `Transfer` 金额（未达标拒绝完结）。
- **Phase 2 + 第一性原理安全加固已落地**：
  - `HotWalletWatchManager`：入口经 `VerifyDeployedCodeAsync` 多节点/双次复核检查热钱包字节码（防 EIP-7702 委托），确认非空立即触发 `Collection.Withdraw.Halt=true`（节点不一致仅告警不误熔断）；主扫描优先走 RPC `eth_getLogs`、失败或截断回退 Etherscan；支持结合 `ColChainTxAttempts` 自动把迟到回填的 `Anomaly` 流水晋升为 `Matched`；`Self` 自转按净额 0 处理；扫描 `(safeHead, head]` 未确认出站实时预警（仅告警不拉闸）；每 5 分钟在安全块按 `LastCheckpointWalletAddress` 隔离做一次余额 Checkpoint 对账（发现未解释减少即告警）；可选 `Collection.Withdraw.AutoHaltOnAnomaly`（命中已确认未匹配出账或余额缺口自动拉响总闸）。
  - `SweepManager.RunSponsorHeadWatchdogAsync`：同样经 `VerifyDeployedCodeAsync` 复核 Gas Sponsor 字节码，确认非空或读数不一致均暂停签署自转交易。
  - `ColGasFeeLogs`：按 `(NetworkId, TxHash)` 幂等记录确认/revert 交易实际消耗的 `GasUsed × EffectiveGasPriceWei` 与 `FeeBnb`（覆盖打油、归集、提现）。
  - `SetWithdrawVaultAsync` 增加 `ConfirmAddress` 校验、在途 `Broadcasting` 提现拦截、充值地址冲突拦截与 `eth_getCode` 防委托预检；新增 `GetWalletNonceStatusAsync` 与 `PurgeStuckNonceAsync` 管理端卡死队头自转清理逃生舱（按目标取分布式锁、经 `VerifyDeployedCodeAsync` 复核，并默认拒绝顶替活跃 `Fueling`/`Broadcasting` 单，强制覆盖需 `ForceOverrideActiveOwner`，热钱包还须先拉响 `Halt`）。
- 提现侧用户 API 已备（/api/app/collection-withdraw），用户端 H5 未在本仓库。
