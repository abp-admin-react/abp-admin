# 管理端表格与图表设计

## 已接入的业务表格

操作日志、会员资金账户和用户管理列表直接使用 `AgGridReact`。点击列头可切换排序，按住 Shift 点击其他列可追加排序。表头下可以直接输入条件，列筛选按钮可以选择比较方式。多个列的条件同时生效，文本、日期和数字列还支持在同一列用 AND / OR 组合两个条件。日期按 UTC 日历日比较，金额输入和传输使用十进制字符串。

| 页面 | 查询接口 | 数据范围 |
| --- | --- | --- |
| 操作日志 | `POST /api/app/operation-log/query` | 当前账号可访问的日志。页面关键词可与列条件叠加 |
| 会员资金 | `POST /api/app/collection-balance-admin/member-balance-accounts/query` | 先按会员汇总各币种余额，再筛选和排序，最后返回当页会员 |
| 用户管理 | `POST /api/app/identity-user-search/query` | 按当前租户及权限查询。账号状态、关键词及其他列条件可叠加，导出沿用筛选和排序 |

### 公共跨列搜索

表格顶部统一使用 Ant Design 搜索框。按回车或点击搜索按钮提交关键词，清空搜索框可以继续使用已有的列头条件。更改关键词后从第一页重新查询。后端先限定当前账号可访问的行，再让已配置的字段按 **OR** 匹配同一个关键词，与列头条件取 **AND**，最后统计、排序和分页。

| 表格 | 顶部搜索的字段 |
| --- | --- |
| 用户管理 | 用户名、名、姓。有更新用户权限时也可按邮箱、手机号搜索。用户 Excel 导出沿用已提交的关键词、列条件和排序。 |
| 操作日志 | 用户名、操作名、业务编号、动作明细。关联 ID、模块和结果可通过各自列头筛选。 |
| 会员资金账户 | 用户 ID 支持输入片段；有查看用户权限时可搜宿主用户名，同时有更新用户权限时可搜邮箱。金额条件仍通过对应币种的列头精确输入。 |

资源在后端一次声明可搜索的字段。举例：

```csharp
var grid = new GridQueryDefinition<Customer>()
    .SearchableField("code", row => row.Code)
    .SearchableField("name", row => row.Name)
    .Field("createdAt", row => row.CreatedAt);
```

这会在编号和名称中查找关键词，创建时间仍可单独排序和筛选。可用 `KeywordOnly(row => row.Property)` 将字段仅用于关键词匹配。仅授权后才可搜索的字段，在后端校验权限后登记。文本和 UUID 按不区分大小写的连续片段匹配；请求只传 `keyword` 和列条件。

### 新增一张同类表

1. 在资源的 AppService 上沿用业务查询权限，在 `GridQueryDefinition<T>` 中用 `.Field("字段键", row => row.Property)` 登记可查询字段。金额绑定数据库 decimal，时间绑定原始时间值。对授权后的 `IQueryable` 调用 `Filter`，统计总数，再调用 `OrderBy` 并传入唯一键，最后 `Skip`、`Take`。返回 ABP `PagedResultDto`。参考[操作日志服务](../backend/framework-app/AbpAdmin.Application/OperationLogs/OperationLogAppService.cs)。
2. 在 `web/src/abp` 定义接收 `GridQuery` 和 `AbortSignal` 的 POST 请求函数。列的 `field` 或 `colId` 对应后端字段键。在 `RemoteColumn<T>[]` 声明标题和类型，文本是默认值，自由格式标识符、UUID、数值、金额、日期、布尔、操作列分别用 `identifier`、`uuid`、`number`、`decimal`、`date`、`boolean`、`display`。展示值可用官方 `valueFormatter` 或 `cellRenderer`。参考[操作日志页面](../web/src/pages/operation-logs/index.tsx)。
3. 需要跨列搜索时调用 `const { gridProps, searchBox } = useSearchableRemoteGrid({ columnDefs, query, getRowId, ariaLabel })`，将 `{searchBox}` 放入工具栏，渲染 `<AgGridReact {...gridProps} />`。页面直接传查询函数。仅需列头筛选的页面可继续使用 `useRemoteGrid`。共享配置处理中文、主题、分页、筛选模型转换、取消过期请求、筛选输入错误提示和加载失败重试。新资源用超过一页的数据验证总数、排序和组合条件。参考[共享配置](../web/src/components/remoteGrid/index.tsx)和[请求转换](../web/src/components/remoteGrid/query.ts)。

## 选型与后续规划

以下记录 2026-11-20 的设计分析。动态字段清单 `/schema`、多值选择和跨资源列偏好属于后续规划。当前已接入表格的可查询字段由前后端显式登记。

## 全站分工

- **Ant Design Pro / Ant Design** 负责应用布局、导航、权限入口、页面容器、指标卡、表单、筛选、工具栏、弹层、错误和空状态。
- **业务表格优先验证 AG Grid Community。** 页面直接用官方 `AgGridReact`。普通列表需要列头条件、远程分页和全量排序，Community 的 Infinite Row Model 已提供这套数据源接口。用一份字段约定生成官方列配置，后端验证并执行，现有表格逐模块评估。
- **VChart** 负责独立图表，可直接放在 ProCard 中。图表与普通表格分别选型及按需加载。VTable 的树列、透视表、表内图有独立价值；出现明确的报表需求时再评估引入第二套表格依赖。[React VChart](https://visactor.io/vchart/guide/tutorial_docs/Cross-terminal_and_Developer_Ecology/react) · [VTable 图表单元格](https://visactor.io/vtable/guide/cell_type/chart)

## 怎样让新页面自带列头排序和搜索

界面约定为：点击可排序列的表头切换顺序；普通文字列的表头下直接输入关键词；日期、数值或枚举用与类型匹配的控件；不同列的条件叠加，清除后回到第一页。全部查询以当前账号可访问的完整结果集为范围。保留固定列、调宽、分页及明确的加载、错误和空状态。简单数字与金额精确匹配采用不同的输入精度规则。

| 业务字段 | 表头默认提供的操作 | 实施条件 |
| --- | --- | --- |
| 名称、说明 | 文本包含或前缀查询，允许排序时显示排序按钮 | 资源定义允许的匹配方式 |
| 用户 ID、单号 | 精确匹配，需要时粘贴多值 | ID 以字符串处理，服务端验证后执行 in |
| 状态、币种 | 单选或多选，支持清空 | 候选来自业务字典或授权接口；Community 使用共享的小型筛选器，成品 Set Filter 属于 Enterprise |
| 笔数、耗时、日期 | 排序、比较与范围 | 数值按原始单位，日期按业务时区换算 |
| 高精度金额 | 排序、精确值与区间 | 输入保留十进制字符串，数据库使用 decimal 比较。必要时仅复用一个金额筛选控件 |
| 操作按钮、仅展示的计算列 | 保留原操作与显示 | 服务端能对全量计算结果排序或筛选后再开放相应入口 |

**后端每个资源登记一次。** 字段声明给出稳定键、类型、允许的操作与权限。同一份定义生成能力清单并校验请求；服务端执行授权、过滤、全量排序、稳定并列排序和分页。老 GET 可以在服务层映射，新资源可以使用下述查询契约。

**前端公共接线只做一次。** 应用入口注册 Community 模块、中文及主题。字段定义转为官方 `ColDef`，原生文本/数字/日期筛选器按需开启 `floatingFilter`；官方 datasource 把 `sortModel`、`filterModel` 和分块范围翻译为 ABP 查询。业务页面只指定资源和特殊单元格，保留所有官方属性，不再包一层表格，也不逐页拼筛选参数。字段未获授权、或 API 尚不支持该操作时，表头不暴露相应入口。

**为什么优先用 AG Grid 做这一轮。** 独立 Chromium 中用 AG Grid Community 36.3.0 直接验证了单列/多列排序、分块翻页、输入文本以及文本与数字叠加筛选，查询模型能送给模拟服务端。VTable 1.26.8 也实测了原生列头点击后由服务端排序并回首页；其官方 FilterPlugin 在确认时会先对已载入数据应用规则，再通知订阅者，默认候选值来自已加载记录。分页数据若采用 VTable，需要公共的远程筛选交互或其他经验证的接法。两者都需要业务 API 的真实查询能力。[AG Grid 分块行模型](https://www.ag-grid.com/react-data-grid/infinite-scrolling/) · [AG Grid 列筛选](https://www.ag-grid.com/react-data-grid/filter-conditions/) · [VTable 筛选执行顺序](https://github.com/VisActor/VTable/blob/a938bf7988391b6c2420f9fd2e0ada40a52b9f8e/packages/vtable-plugins/src/filter/filter-state-manager.ts#L198-L206)

## 现状与优先级

生产前端主要使用指标卡、进度条和表格呈现统计信息，工作台的审计错误率则使用 CSS 比例条。首先从运营要回答的问题选择图形。

| 页面与问题 | 建议展示和操作 | 数据条件 |
| --- | --- | --- |
| 工作台：错误集中在哪一天、涉及多少请求？ | 在审计卡内放每日错误率折线；悬停显示当日总数、错误数及比例；保留整段汇总与可访问的每日数表。选择一天可定位同范围审计记录。 | 已有 error-rate 的 dataPoints，画首图可沿用接口。按日下钻需要审计页接收时间范围及与统计一致的错误条件；当天的权限和统计窗口必须相同。 |
| 会计概览：某币种今天收支多少、余额中多少被冻结？ | 用 Ant 币种选择器，VChart 展示所选币种的收支对照、可用和冻结构成，明细表提供精确金额。试算、对账异常继续用状态与链接表达。 | 使用 currencyTotals。多币种时现有 API 将顶层金额置零，逐币实际值在明细中；顶栏读法也应更新。过去 7/30 日收支需按 UTC 凭证创建日、币种、记账阶段增加聚合。历史余额需账本重建或快照。 |
| 链上监控：单据主要停在哪个状态？ | 充值、提现分别用横向条形按状态显示笔数；悬停看数量，选中状态查看相应记录；对齐币种后才展示金额。 | 已有充值按状态、提现按状态与币种的快照。状态分布和当日创建订单是不同范围，需要分别标注。页面 30 秒刷新一次，历史趋势需带时间分组的查询。 |
| 服务监控：CPU 高是瞬间波动还是持续过载？ | 当前占用继续用 Ant 指标与进度；有连续样本后再提供历史折线，展示采样时间和缺口。 | 目前返回当前采样。跨会话、跨天趋势要有服务端存储、采样间隔及保留期。 |

工作台的错误率按服务端审计判定计算。默认区间为当前时刻回溯七天，按 UTC 日聚合，首尾可能是部分日，可能包含八个日历日期。总错误率用整个窗口的错误数除以总数，与逐日百分比的简单平均可能不同。无记录时标记无样本；有审计记录但错误数为零才画 0%。账务和链上图保留金额原单位和精确值，提示框与数表使用响应的精确值；画图坐标可以作显示用转换。图表与详情查询都要遵守后端资源权限、租户及数据范围。

现有实现见 [工作台审计](../web/src/pages/Welcome.tsx)、[审计统计](../backend/framework-app/AbpAdmin.Application/AuditLogs/AuditLogAppService.cs)、[会计概览](../web/src/pages/finance/overview/index.tsx)、[按币汇总](../backend/modules/platform/AbpAdmin.Ledger/Services/FinOverviewAppService.cs)、[链上监控](../web/src/pages/collection/dashboard.tsx) 和 [服务器采样](../backend/framework-app/AbpAdmin.Application/Monitoring/ServerMonitorAppService.cs)。

## 已验证的集成与尚待验证的业务交互

**独立图表。** 独立 Chromium 样例使用 React / ReactDOM 19.3、Ant Design 6.7 和 React VChart 2.2.2。在 ConfigProvider 下的 Card 内可绘制真实图形，测试了更新数据、缩窄容器和切换明暗主题。主题需要把 Ant token 显式传给图表，并同步 VChart 的主题。此试验没有测生产路由包体、线上数据量或用户设备性能。[官方主题 API](https://visactor.io/vchart/guide/tutorial_docs/Theme/Apply_Theme) · [按需加载](https://visactor.io/vchart/guide/tutorial_docs/Load_on_Demand)

**表格的特殊页面。** 菜单树与订单 / 审计的原位展开、可编辑字典和提现跨页选择各做一次实际业务回归。AG Grid Community 的原生 Tree Data、Master/Detail 需商业版；已在免费版样例用树的可见行和全宽行验证了原位展开。带真实 Ant 弹层和可变行高的远程分页仍需验证。另一条可比较的路径是 VTable 的树列及主从表插件。React 19 的 VTable 自定义 DOM 单元格样例对齐 react-reconciler 后，按钮可点击，但外层 Ant 主色未传入该挂载路径。[AG Grid 功能分层](https://www.ag-grid.com/react-data-grid/community-vs-enterprise/) · [VTable React 集成](https://visactor.io/vtable/guide/Developer_Ecology/react) · [VTable 主从表](https://visactor.io/vtable/guide/plugin/master-detail)

## 首批业务验收

1. **先验收两张真实表。** 操作日志用“失败 + 模块关键词 + 耗时降序”，接通现有后端排序。会员资金把最高余额放在原第二页，验证点降序后移到第一页；用只出现在后续页的完整用户 ID 叠加金额下限，检查首行、总数、清空及正确的权限范围。两页必须使用同一套公共接线，新增普通列由字段声明生成排序和搜索入口。
2. **财务与链上。** 用至少两个币种、零值、负值（适用字段）、高精度小数、接口错误和无记录样本对照精确数表。筛选、tooltip 与钻取结果共用同一时间口径、币种和状态条件。金额与笔数分别提供指标切换或独立面板。
3. **76 处表格分批迁移。** 普通列表先行。实际验证菜单树原位展开、订单时间线、审计子表、字典整批保存、待审提现显式跨页选取，并保留现有服务端导出。各模块用相同数据量测首屏增量、查询延迟、窄屏与深浅主题、键盘及图表路由卸载，旧组件零业务引用后删除。

工作台审计属于独立的 VChart 验证项。最近七天错误率、总数应与现有 API 一致，鼠标、触摸和键盘均可查看所选日期。允许下钻时，列表和图上对应的记录数一致。

## 当前覆盖面

按生产 JSX 调用点计数，排除测试、生成目录及封装自身的渲染调用。61 个源文件共有 76 处，同一文件可包含多种表格。此数是迁移基线，不能当作路由数量。

| 现有调用 | 处数 |
| --- | ---: |
| AutoHeightProTable | 42 |
| Ant Design Table | 15 |
| ResizableTable | 11 |
| ProTable | 7 |
| EditableProTable | 1 |

涉及的行为还包括工具栏、业务操作后的刷新、跨页选择、导出、列偏好、URL 深链和权限。验收时逐条检查实际操作流程。

按目录分布，收款与资金 14 处、财务 13 处、身份 8 处、账户 6 处、审计 5 处、OpenIddict 4 处、订单 3 处。文件、数据字典、语言、定时任务、SaaS、Webhooks 各 2 处。其余 11 处依次分布在后台作业、业务模板、菜单、模块、通知、操作日志、支付、系统、租户、文本模板和虚拟文件浏览器。

## 查询契约

每个资源在后端声明稳定的字段键、业务类型、允许的比较操作、能否排序、关键词匹配范围和权限。以同一份定义生成查询能力描述，并在服务端执行校验。前端用原生列配置决定标题、显示格式、宽度和自定义单元格。资源级配置允许按需定制，服务端只返回业务能力和数据。

以已有会员余额资源为例，建议在现有资源路径下新增 `GET /schema`、`POST /query`。其他简单列表可以沿用现有 ABP GET，由服务层映射已有 DTO 支持的条件。后端不接收任意 SQL、属性路径、Grid 私有模型或来自客户端的权限声明。

```json
{
  "skipCount": 0,
  "maxResultCount": 20,
  "filters": [
    {
      "field": "usdtBalance",
      "join": "and",
      "conditions": [{ "op": "gte", "value": "100.00" }]
    },
    {
      "field": "updatedAt",
      "join": "and",
      "conditions": [
        { "op": "gte", "value": "2026-11-17T16:00:00Z" },
        { "op": "lt", "value": "2026-11-18T16:00:00Z" }
      ]
    }
  ],
  "sort": [{ "field": "usdtBalance", "direction": "desc" }]
}
```

该请求查询北京时间 11 月 18 日有更新且 USDT 可用余额至少为 100 的账户。响应沿用 `items` 和 `totalCount`。每个字段组内部按 `join` 组合，各字段组之间 AND，`in` 接受数组。可选 `keyword` 在允许的列之间 OR，再与字段组取交集。数值与时间原生比较，日期以明确业务时区转换成 UTC 半开区间；区分空值和零值。为每个字段约定空值排序位置。

后端先应用资源权限、租户/宿主限制与数据范围，映射到注册过的 EF 查询表达式，再做聚合、筛选、计数、排序和分页。校验类型、操作、分页上限和多值数量。排序末尾追加稳定唯一键。在会员余额查询中，把 USDT/USDC 可用和冻结额移到数据库里先汇总，缺币按零处理。耗时等普通数值可以使用 Number Filter；高精度金额可用项目已有的 Ant Design 数值输入配合 stringMode，从录入到传输均保持十进制字符串，由数据库 decimal 比较。现有逐行用户查询应改为按权限批量补全显示名；按用户名查全量账户时使用有权限的身份查询能力，不能拿当前页已补全的名字做筛选。

<details>
<summary>选型阶段的对照与 AG Grid 试验记录（归档）</summary>

## VTable、VChart 与 AG Grid Community 的对照

VTable 的 ListTable 处理明细记录，PivotTable 处理按维度分组和汇总的交叉表。两者可以独立工作。需要在单元格放图，或用 PivotChart 在交叉维度里展示一组图时，才注册 VChart 并提供图表配置。日常审批和身份列表可只加载表格。VTable、官方 React 封装及 VChart 当前发行包均为 MIT；React 封装声明支持本项目所用的 React 19。[VTable](https://github.com/VisActor/VTable) · [React 版本声明](https://registry.npmjs.org/@visactor%2Freact-vtable/1.26.8) · [图表单元格](https://visactor.io/vtable/guide/cell_type/chart) · [VChart](https://github.com/VisActor/VChart)

| 本项目的任务 | VisActor 路线 | AG Grid Community 路线 |
| --- | --- | --- |
| 操作日志、会员资金跨全量数据排序和组合查询 | ListTable 有多列排序、排序事件和异步数据源。FilterPlugin 有按值勾选及条件筛选；该版本插件从表格记录提取枚举，应用规则时更新表格自身的数据源。仅加载一页时，全量枚举候选、筛选回传、重置和并发响应需接 ABP 验证。 | Infinite datasource 的分块索引、排序和筛选模型已通过独立浏览器试验。枚举勾选界面需业务控件或商业版 Set Filter。 |
| 菜单父子项原位展开 | ListTable 支持 tree 列、子节点、初始展开深度及懒加载。重点核对父子顺序、已有编辑按钮、键盘与展开状态。 | Community 样例用可见记录与单元格按钮验证了三级树；成品 Tree Data 属于 Enterprise。 |
| 审计属性子表、订单时间线 | 官方 MasterDetailPlugin 可以在行下嵌入 ListTable，与审计属性明细较吻合。插件的 detailTableOptions 描述子表；订单既有 Ant Design 时间线仍需验证自定义布局 / React DOM 接入。 | Community 全宽行样例验证了文本明细原位展开。真实 React 时间线、动态行高、焦点和服务端分页有待联测。 |
| 提现审批、字典编辑 | Canvas 网格提供编辑器、React 自定义布局和 DOM 宿主。需实测 Ant 按钮、输入、弹层、主题、权限变化、滚动回收与键盘。 | 可直接使用 React 单元格渲染器及官方编辑器。也需要验证稳定 ID、跨页显式勾选、一次性草稿保存和权限。 |
| 交叉报表、表内趋势 | PivotTable 可显示数值交叉表，VChart 可选用于图表单元格及 PivotChart。实际评价包含汇总口径、图表装载成本和阅读体验。 | Community 可显示后端已汇总的普通网格，自带透视功能属于 Enterprise。 |

依据见 [VTable 官方筛选插件](https://visactor.io/vtable/guide/plugin/filter)、[该版筛选执行源码](https://github.com/VisActor/VTable/blob/a938bf7988391b6c2420f9fd2e0ada40a52b9f8e/packages/vtable-plugins/src/filter/filter-engine.ts)、[枚举候选来源](https://github.com/VisActor/VTable/blob/a938bf7988391b6c2420f9fd2e0ada40a52b9f8e/packages/vtable-plugins/src/filter/value-filter.ts)、[异步数据源](https://visactor.io/vtable/guide/data/async_data)、[树形列表](https://visactor.io/vtable/guide/table_type/List_table/tree_list)、[主子表插件](https://visactor.io/vtable/guide/plugin/master-detail)、[React 自定义组件](https://visactor.io/vtable/guide/custom_define/react-custom-component) 和 [AG Grid 版本差异](https://www.ag-grid.com/react-data-grid/community-vs-enterprise/)。

**“多个搜索”的三个口径。** 勾选多个状态、对不同列同时设置条件、对同一列构造两条 AND/OR 条件，是三种不同能力。VTable 的插件可按值多选，也可给多列设置条件。已核对版本中，每个字段的模式是多值列表或一项条件配置。跨列关键词、复杂布尔条件和按权限查询全量候选需要明确的接口与交互约定。哪些金额允许排序、哪个 ID 允许精确搜索仍以服务端业务字段为准。[发布版筛选状态定义](https://github.com/VisActor/VTable/blob/a938bf7988391b6c2420f9fd2e0ada40a52b9f8e/packages/vtable-plugins/src/filter/types.ts)

**独立 Chromium 试验。** 使用 VTable 1.26.8，仅加载表格包时普通列表可显示记录，PivotTable 对同一分类的 3 和 4 得到 7；随后加载 VChart 2.2.2，调用官方 chartModule 注册接口，ListTable 的图表列实际绘出了柱形。简单迷你折线也可以直接使用 VTable 自带的 sparkline。此试验验证了发行包组合，尚未测真实财务数据量和路由资源大小。[图表单元格](https://visactor.io/vtable/guide/cell_type/chart) · [迷你折线](https://visactor.io/vtable/guide/cell_type/sparkline)

**复用现有 Ant Design 单元格的实测。** React-VTable 1.26.8 与 React / ReactDOM 19.3 的普通表可显示。高级自定义单元格走官方 React DOM 接入路径时，此样例默认解析的 react-reconciler 0.29 使按钮渲染报错。按官方建议对齐为 0.33.0 后，Ant Design 6.7 按钮可以显示和点击。该路径下，外部 ConfigProvider 设置的主色未传入按钮，按钮读取到默认主题。这个结果限于该挂载方式；迁移前应实测现有按钮、确认弹层、下拉编辑、焦点和上层上下文，分别记录配置或适配成本。普通表格与嵌入复杂 DOM 的可行性要分别验收。[React 19 接入说明](https://visactor.io/vtable/guide/Developer_Ecology/react) · [自定义单元格源码](https://github.com/VisActor/VTable/blob/v1.26.8/packages/react-vtable/src/table-components/custom/custom-layout.tsx)

**同题验证，分别记录结果。** 用相同数据、接口和业务权限跑两种表格。① 在会员资金中查出只存在于后续页的候选，组合金额范围，检查命中总数、首行和重置后的结果。② 菜单展开后编辑原节点，审计同时展开两笔属性明细，并用键盘进入和退出订单时间线。③ 字典一次修改两项并取消其中一项，提现跨页仅选待审记录，检查实际提交的键。④ 用同一数据集做一张“币种 × 日期”的数值表和表内趋势图，普通列表和图表路由分别测加载量。比较实现所需的自写交互、对现有业务组件的复用、键盘 / 读屏、浏览器自动化、首屏和查询 P95；尚无同口径的生产样本性能比较。

以下保留选型期间的 AG Grid 接入记录。

## AG Grid 候选接入方式

每个页面直接从 `ag-grid-react` 导入 `AgGridReact`，以官方 `ColDef<T>`、`GridOptions<T>`、行模型和事件为开发接口。应用入口统一注册所需 Community 模块。

持久共用的代码限于确有复用价值的设置、数据转换和业务控件，例如中文文案、主题、金额显示，以及从 ABP 列表响应向官方 datasource 回传结果。页面仍可用所有官方属性。迁出最后一个使用方后删除 `AutoHeightProTable`、`ResizableTable`、旧列宽与列筛选适配，不再维护自有表格组件或一套平行的属性、事件和列配置 API。

远程列表的页面调用示意如下。`columns` 是官方 `ColDef<OperationLogDto>[]`，`logDatasource` 实现官方 `IDatasource`，负责调用已有服务层。表格位于原来的 PageContainer 及工具栏下方。

```tsx
import { AgGridReact } from 'ag-grid-react';

<AgGridReact<OperationLogDto>
  columnDefs={columns}
  rowModelType="infinite"
  datasource={logDatasource}
  getRowId={({ data }) => data.id}
  pagination
  paginationPageSize={20}
  paginationPageSizeSelector={false}
  cacheBlockSize={20}
/>
```

## 用户看到什么

- **排序**。只有能真实执行全量排序的字段显示可排序表头。支持多列优先级。金额、时间、带单位的耗时均使用原始业务值排序。操作按钮、关联链接的显示文案默认不参与。
- **单列查询**。名称提供文本匹配，用户 ID 或交易号提供精确匹配，日期和金额提供范围。空表首次打开即能显示可用的筛选方式。表头放筛选入口，高频字段可开 floating filter。
- **同列多个条件**。文字、数字、日期的简单筛选可连接 AND / OR，例如“用户名以 A 或 B 开头”。状态勾选多个值、粘贴多个用户 ID 则使用 `in`。前者有 Community 原生控件，后者可直接用现有 Ant Design 多选或输入控件。不同列之间默认 AND。若要求现成的 Excel 式勾选面板、不同类型筛选器叠在同一列、可嵌套的跨列条件构建器，须评估 Enterprise 的 Set / Multi / Advanced Filter。
- **跨列关键词**。在工具栏标明匹配字段。远程列表把关键词与列条件一起交给后端，在全量授权记录内搜索。本地数据全部加载时可使用内置 Quick Filter。条件变化回到第一页，清除条件与 URL 深链生效规则明确，失败可重试。

## 两种数据来源

**小型完整集合**直接传 `rowData`，用 Client-Side Row Model 排序、筛选。适合已加载完整的配置字典或详情行。AG Grid 支持从首条非空行自动生成列，可用于已获准显示全部字段的临时预览。正式业务页显式给出可见字段和类型，避免空数据、格式化字符串和权限差异改变表头。

**远程分页集合**用 Community 的 Infinite Row Model 和官方 pagination。页面直接声明 `rowModelType="infinite"`、`datasource`、`getRowId` 和受支持的列。将每个 `getRows` 请求的 `startRow/endRow`、`sortModel/filterModel` 转成业务查询。用 `items` 与 `totalCount` 更新数据块及分页，失败走失败回调。块大小至少覆盖当前页大小。多个数据块可以并行；更换资源、租户或筛选时更新数据源、隔离过期结果。业务动作完成后刷新相应缓存。分块加载下显式指定列类型；长 ID 和高精度金额保持文本传输。

主列表由页面布局提供确定高度，使用正常模式及虚拟化。详情里的小表才适合随内容增高。按官方 Theming API 对齐 Ant Design 明暗主题，配置中文文案。列宽、顺序、显隐采用官方 Grid State。偏好按用户、租户、表格和版本分开保存，只恢复当前有权访问的列，以稳定的 `colId` 识别。将关键词和敏感筛选值的持久化作为业务选择。

**行内明细是远程分页的例外**。审计详情已有完整行集合，可在 Client-Side 行模型内显示展开行。对于分页订单，服务端仍按父记录分页、过滤和计数，当前页分开保存父记录与展开状态，表格渲染这一页及展开行。父行使用订单 ID，详情行使用独立稳定键。展开十条也不改变订单总数、页数和排序结果。Infinite 行模型要求固定行高，不能直接用于高度随内容变化的详情行。原型需核实内容高度变化、横向滚动、键盘进入详情再回到触发按钮。[全宽行](https://www.ag-grid.com/react-data-grid/full-width-rows/) · [行高限制](https://www.ag-grid.com/react-data-grid/row-height/)

**独立浏览器验证**。React 19.3.0 与 AG Grid Community 36.3.0，直接渲染 `AgGridReact`，通过本机 HTTP 模拟服务返回 45 条、每页 10 条。Chromium 实测点击下一页请求第 11–20 条；按分数降序后原第 45 条到首行；Shift 点击第二列表头生成两个有序排序条件；文字包含条件和数值等于 29 叠加后只剩第 29 条记录。此试验覆盖网格与 datasource 之间的事件和全量查询回显，ABP 权限、数据库性能和真实页面另行验收。

第二个 Chromium 样例只用 Community 的 Client-Side 行模型和业务级渲染函数。三级树可在原位置展开和收起，两个分支可同时打开，验证了鼠标、Enter、Space 和展开状态提示。试验还验证了两条父记录可各自显示、收起一条全宽明细，宽度横跨固定列。样例使用固定高度和文本，真实页面的异步内容高度、子表键盘焦点、远程订单分页仍需集成验证。

## 需要先看实物的页面

| 现有交互 | Community 方案及验收 |
| --- | --- |
| 菜单树支持展开全部、逐级折叠、在父节点下新增 | 保留同表展开的入口。页面已有完整树和展开键，可向原生网格提供可见行，在第一列的 cellRenderer 放置缩进与展开按钮。验证父子项顺序、全展开、编辑定位、键盘和无障碍层级提示。成品 Tree Data 属于 Enterprise。 |
| 订单行内时间线、审计记录展开属性变更子表 | 展开后在原记录下方使用官方 Full Width Row 承载现有时间线 / 属性明细内容，保留同时打开多行和已存在的上层抽屉。验证高度、分页和收起后的焦点。成品 Master/Detail 属于 Enterprise。 |
| 数据字典多行编辑与“保存全部” | 使用 Community 编辑器接入页面的草稿状态，使用稳定临时行键。按现有规则校验唯一编码、仅显示名可编辑的静态项和增删权限。取消编辑和提交失败均要保留正确草稿，保存仍为一次全量业务提交。 |
| 用户列表默认隐藏邮箱验证、锁定到期、二步验证、登录失败次数，用户可按需查看 | Community 提供列状态 API，官方 Column Chooser 和 Columns Tool Panel 属于 Enterprise。保留默认值和个人偏好的免费方案，是让工具栏里的 Ant Design 勾选菜单直接调用列状态 API。[官方列选择器](https://www.ag-grid.com/react-data-grid/column-chooser/) |
| 提现跨页勾选后批量操作 | 稳定行 ID，保存用户显式选中的 ID 集合，保持 Pending 才可选以及执行时的后端校验。Infinite 模型没有“全选全部匹配记录”。需要该功能时另设计服务端操作、范围提示和确认，迁移现有显式选择即可。 |
| 审计和用户导出 Excel | 沿用现有带权限的服务端导出、同步/异步分流和筛选范围。列表与导出共享同一有效查询状态。不会因虚拟滚动只导出当前缓存。 |

如果这几页需要自建通用树表或主从网格才能达到原有可用性，暂停替换这些页面并保留其当前实现。先评估原生功能更齐全的组件或 Enterprise 许可，再决定全站能否收口。调整页面结构、引入商业许可须另行决策。分别查阅 [AG Grid 版本差异](https://www.ag-grid.com/react-data-grid/community-vs-enterprise/)、[Tree Data](https://www.ag-grid.com/react-data-grid/tree-data/) 和 [Master/Detail](https://www.ag-grid.com/react-data-grid/master-detail/)。

## 分批实施与退出旧组件

1. **选型验证**。同一版本锁定 `ag-grid-react` 与 `ag-grid-community`。在实际管理端验证操作日志的服务器排序、筛选及分页，再给菜单树和审计详情各做一个可操作原型。逐项评审操作步骤、需要同时对照的记录、键盘焦点和窄屏可用性。特殊交互的取舍确定后再扩大范围。
2. **普通列表、看板和弹窗**。按功能模块提交，按页选择本地完整数据或远程分块加载。保留权限按钮、深链、点击行、变更后刷新、列偏好和错误提示。新迁模块用真实 AG Grid 的浏览器测试补充原来的组件 mock。
3. **数据与操作语义复杂的模块**。迁会员资金、提现、导出和可编辑字典。与旧接口对照总数、金额顺序、日期边界、跨页选择、导出内容和保存行为。保留可在迁移期间回退的旧 API。比较同一数据量下首屏 JS 增量、首次可操作时间、筛选/翻页的 P95、请求数和长列表滚动。
4. **收口**。按同一统计口径重扫目标 76 处，补扫期间新增的调用，确认旧组件、过滤适配及测试替身的业务引用为零后删除。在项目约定里记录直接导入的示例，CI 阻止重新引入旧表格和未经选型的企业包。ProLayout、ProForm 等其他组件继续按实际使用情况维护。

压测和回归包含空表、只有一页、超出一页、刷新前后权限变化以及相同排序值。用例包含最大余额原本落在第二页、低权限用户不能搜索身份字段、同筛选全量导出、抽屉关闭焦点回到触发行、390px 窄屏和主题切换。数据量、延迟与包体使用实测基线，评审稿不预报收益。

更多官方依据见 [Infinite Row Model](https://www.ag-grid.com/react-data-grid/infinite-scrolling/)、[单列条件](https://www.ag-grid.com/react-data-grid/filter-conditions/)、[列类型推断](https://www.ag-grid.com/react-data-grid/cell-data-types/)、[Grid State](https://www.ag-grid.com/react-data-grid/grid-state/) 和 [布局模式](https://www.ag-grid.com/react-data-grid/grid-size/)。

</details>
