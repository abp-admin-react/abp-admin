# 管理端全站表格设计

2026-11-20 · 方案评审稿 · 范围为 `web/src` 中的管理端业务表格，含主列表、弹窗、抽屉、页签、看板和表单中的表格。

## 选型决定

只替换表格。建议以 AG Grid Community 作为全站业务表格的统一目标。每个页面直接从 `ag-grid-react` 导入 `AgGridReact`，以官方 `ColDef<T>`、`GridOptions<T>`、行模型和事件为开发接口。应用入口统一注册所需 Community 模块。页面布局、导航、PageContainer、工具栏业务按钮、ProForm、选择器、时间线、弹窗和抽屉沿用现有 Ant Design Pro / Ant Design 实现及打开方式。数据请求沿用项目的 `web/src/abp` 服务层。菜单树与行内明细在原位置验收。

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

## 查询契约

每个资源在后端声明稳定的字段键、业务类型、允许的比较操作、能否排序、关键词匹配范围和权限。以同一份定义生成查询能力描述，并在服务端执行校验。前端用标准 `ColDef` 决定标题、显示格式、宽度和自定义单元格。资源级配置允许按需定制，服务端只返回业务能力和数据。

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
