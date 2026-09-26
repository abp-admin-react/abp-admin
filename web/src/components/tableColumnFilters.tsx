import { SearchOutlined } from '@ant-design/icons';
import type { TableColumnType } from 'antd';
import { Button, DatePicker, Input, Space } from 'antd';
import type { FilterDropdownProps } from 'antd/es/table/interface';
import dayjs, { type Dayjs } from 'dayjs';
import type React from 'react';

/**
 * 列头筛选（filterDropdown / filters）的官方写法工厂。
 *
 * 按官网 custom-filter-panel demo 的结构封装（表格外零筛选 UI 的约定），
 * 返回值直接展开到 ProColumns 上；筛选值经 ProTable 收集后从 request 的
 * 第三参 filter（{ [dataIndex]: value[] }）交给服务端——列上不要配 onFilter。
 *
 * 参考：https://ant.design/components/table-cn#components-table-demo-custom-filter-panel
 */

type ColumnFilterProps = Pick<
  TableColumnType<unknown>,
  'filterDropdown' | 'filterIcon' | 'filters'
>;

/**
 * 标记列为「服务端筛选」：ProTable 的 getServerFilterResult 只转发带非空
 * filters 属性的列——空数组会在 antd/pro-table 列管线中被剔除，confirm 后
 * 的值会在 request 第三参里被静默丢弃。占位项用户不可见（自定义 filterDropdown
 * 接管了面板渲染，内置勾选菜单不会出现），仅用于让列通过管线判定。
 */
const SERVER_FILTER_MARKER = {
  filters: [{ text: 'server-filter', value: '__server_filter__' }],
};

/** 面板操作按钮行（官网 demo 同款）：筛选 = 提交并关面板；重置 = 清空 + 提交 + 关面板 */
const panelButtons = (
  confirm: FilterDropdownProps['confirm'],
  clearFilters: FilterDropdownProps['clearFilters'],
  close: FilterDropdownProps['close'],
) => (
  <Space>
    <Button
      type="primary"
      icon={<SearchOutlined />}
      size="small"
      style={{ width: 72 }}
      onClick={() => {
        confirm?.();
        close?.();
      }}
    >
      筛选
    </Button>
    <Button
      size="small"
      style={{ width: 72 }}
      onClick={() => {
        clearFilters?.();
        confirm?.();
        close?.();
      }}
    >
      重置
    </Button>
  </Space>
);

/** 文本筛选：漏斗面板里一个输入框，回车或点「筛选」提交 */
export function textFilter(placeholder = '输入关键字'): ColumnFilterProps {
  return {
    ...SERVER_FILTER_MARKER,
    filterDropdown: ({
      setSelectedKeys,
      selectedKeys,
      confirm,
      clearFilters,
      close,
    }: FilterDropdownProps) => (
      <div style={{ padding: 8 }} onKeyDown={(e) => e.stopPropagation()}>
        <Input
          placeholder={placeholder}
          value={selectedKeys[0]}
          allowClear
          onChange={(e) =>
            setSelectedKeys(e.target.value ? [e.target.value] : [])
          }
          onPressEnter={() => {
            confirm?.();
            close?.();
          }}
          style={{ marginBottom: 8, display: 'block' }}
        />
        {panelButtons(confirm, clearFilters, close)}
      </div>
    ),
    filterIcon: (filtered: boolean) => (
      <SearchOutlined
        style={filtered ? { color: 'var(--ant-color-primary)' } : undefined}
      />
    ),
  };
}

/** 日期区间筛选：面板里 RangePicker，提交后 selectedKeys = [start, end]（Dayjs 对象）。
 * 页面消费端需把 Dayjs 补齐到整天边界（toDayStart/toDayEnd）——纯日期的结束日是
 * 00:00:00，后端 <= 精确比较会排除结束日当天。 */
export function dateRangeFilter(): ColumnFilterProps {
  return {
    ...SERVER_FILTER_MARKER,
    filterDropdown: ({
      setSelectedKeys,
      selectedKeys,
      confirm,
      clearFilters,
      close,
    }: FilterDropdownProps) => {
      const raw = selectedKeys as unknown[];
      const value =
        Array.isArray(raw) && raw.length === 2 && raw.every(dayjs.isDayjs)
          ? (raw as [Dayjs, Dayjs])
          : null;
      return (
        <div
          style={{ padding: 8 }}
          onKeyDown={(e) => e.stopPropagation()}
          onClick={(e) => e.stopPropagation()}
        >
          <DatePicker.RangePicker
            value={value}
            onChange={(range) => {
              // 只接受完整区间：半开 [start, null] 会让漏斗图标点亮但筛选无效
              setSelectedKeys(
                range && range.length === 2 && range[0] && range[1]
                  ? (range as unknown as React.Key[])
                  : [],
              );
            }}
            allowEmpty={[true, true]}
            style={{ marginBottom: 8, display: 'flex' }}
            // 日历必须挂在筛选面板内部：默认渲染到 body 会落在面板外，
            // 点日期即触发 Dropdown 的「点击外部关闭」，把筛选面板整个关掉
            getPopupContainer={(trigger) =>
              trigger.parentElement ?? document.body
            }
          />
          {panelButtons(confirm, clearFilters, close)}
        </div>
      );
    },
  };
}

/** 枚举列筛选的 filters 选项简写：{ 显示文本: 存储值 } → [{ text, value }]。
 * 值统一字符串化；配 enumFilters 的列记得加 filterMultiple: false——
 * 页面按首值下发，多选两项会静默只按第一项过滤。 */
export function enumFilters(
  map: Record<string, string | number | boolean>,
): { text: string; value: string }[] {
  return Object.entries(map).map(([text, value]) => ({
    text,
    value: String(value),
  }));
}

/** request 第三参 filter / 受控筛选状态里取某列的首个筛选值（字符串）。
 * 统一口径：值存在且为字符串才返回（null/undefined/非字符串一律 undefined）。 */
export function firstFilterValue(
  record: Record<string, unknown[] | null | undefined> | null | undefined,
  key: string,
): string | undefined {
  const v = record?.[key]?.[0];
  return typeof v === 'string' ? v : undefined;
}
