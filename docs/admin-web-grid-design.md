# 管理端表格与图表设计

## 管理端表格

管理端的平面列表、抽屉明细和字典编辑直接使用 AG Grid Community 的 `AgGridReact`。菜单管理等独立树形表使用 Ant Design 的 `Table`。导航、表单、工具栏、抽屉和确认对话框使用 Ant Design Pro / Ant Design。

- AG Grid 列表可点击数据列标题排序，按住 Shift 点击其他列追加排序。操作列只呈现该行可用的业务动作。
- 表头使用列类型对应的文本、数字、日期和选项筛选。列与列取交集，文本、数值和日期可在同一列用 AND / OR 组合条件。
- 分页资源在原业务权限和租户范围内执行查询，再返回当页。会计余额先聚合再比较金额。关键词、列条件和多列排序在翻页时继续生效。
- 完整返回的配置、子项或单据明细使用 Client-Side Row Model。此时搜索覆盖当前目录、单据或配置集合中的所有已返回行。字典草稿和编辑动作依靠稳定行键定位。

### 公共跨列搜索

分页表格的跨列搜索使用 Ant Design 搜索框。按回车或点击搜索按钮提交关键词，清空搜索框可以继续使用已有的列头条件。更改关键词后从第一页重新查询。后端先限定当前账号可访问的行，再让已配置的字段按 **OR** 匹配同一个关键词，与列头条件取 **AND**，最后统计、排序和分页。

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

### 完整明细和配置

使用 `useSearchableLocalGrid({ columnDefs, rowData, getRowId, keywordFields, ariaLabel })`。每张表用 `keywordFields` 指定可搜索的字段或取值函数。金额列使用精确十进制排序与比较，日期按 UTC 日历日匹配。业务按钮、链接、编辑状态用原生 AG 列渲染器与稳定行键实现。存量的 Ant / Pro 列展示可通过 `toGridColumns` 迁移，新表可以直接定义原生列。

### 独立树形表

菜单管理保留接口返回的 `children` 层级，用 Ant Design Table 的受控展开状态管理分支。列排序在同一父节点的子项内进行。关键词和列条件共同匹配折叠分支，结果保留命中节点的祖先。清空条件后恢复原来的展开状态。编辑和删除以原始节点为准，搜索结果中暂时不可见的子节点仍计入删除限制。

### 核对方法

用超出一页的记录验证关键字、列过滤、排序和分页，比较总数及结果。遮罩字段还需用低权限账号验证。详情表应检查编辑或选择后的记录在筛选和重新排序后仍与原主键一致，带有资源链接的列表应在重置条件后保留业务范围。数据查询完成后，可通过公共 hook 返回的 `refresh()` 刷新远程数据块。

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
