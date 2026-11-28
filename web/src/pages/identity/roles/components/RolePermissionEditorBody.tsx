import { DownOutlined, RightOutlined, SearchOutlined } from '@ant-design/icons';
import {
  Alert,
  Button,
  Checkbox,
  Empty,
  Input,
  Segmented,
  Select,
  Tag,
} from 'antd';
import { createStyles } from 'antd-style';
import { useId, useMemo, useRef, useState } from 'react';
import type { PermissionGrantInfo, PermissionGroup } from '@/abp/permissions';

type PermissionUpdate = { name: string; isGranted: boolean };

type Props = {
  groups: PermissionGroup[];
  changes: ReadonlyMap<string, boolean>;
  disabled: boolean;
  onChange: (updates: PermissionUpdate[]) => void;
};

type PermissionRow = {
  permission: PermissionGrantInfo;
  path: string;
  searchText: string;
  children: PermissionRow[];
};

// pending = 尚未 PUT 的草稿；界面文案为「本次改动」。
type Filter = 'all' | 'granted' | 'pending';

const filterOptions: { value: Filter; label: string }[] = [
  { value: 'all', label: '全部' },
  { value: 'granted', label: '已授权' },
  { value: 'pending', label: '本次改动' },
];

const voucherReverse = 'Finance.Voucher.Reverse';
const voucherView = 'Finance.Voucher.View';

const useStyles = createStyles(({ css, token }) => ({
  editor: css`
    container: role-permissions / inline-size;
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-inline-size: 0;
    min-block-size: 0;
    flex: 1 1 auto;
    overflow: hidden;
    block-size: 100%;
  `,
  filters: css`
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
    flex: none;
  `,
  columns: css`
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    align-items: stretch;
    gap: 16px;
    flex: 1 1 auto;
    min-inline-size: 0;
    min-block-size: 0;
    overflow: hidden;

    @container role-permissions (min-width: 38rem) {
      grid-template-columns: minmax(9rem, 27%) minmax(0, 1fr);
    }
  `,
  groupNavigation: css`
    display: none;
    min-inline-size: 0;
    min-block-size: 0;
    overflow-y: auto;
    scrollbar-gutter: stable;
    overscroll-behavior: contain;
    align-content: start;

    @container role-permissions (min-width: 38rem) {
      display: grid;
      gap: 4px;
    }
  `,
  groupButton: css`
    display: flex;
    align-items: start;
    justify-content: space-between;
    gap: 8px;
    inline-size: 100%;
    block-size: auto;
    padding: 8px;
    text-align: start;
    white-space: normal;
    overflow-wrap: anywhere;

    &[aria-current='true'] {
      background: ${token.colorPrimaryBg};
      color: ${token.colorPrimaryText};
      font-weight: ${token.fontWeightStrong};
    }
  `,
  groupCount: css`
    flex: none;
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
    font-variant-numeric: tabular-nums;
  `,
  selectField: css`
    display: grid;
    gap: 4px;
    min-inline-size: 0;

    @container role-permissions (min-width: 38rem) {
      display: none;
    }
  `,
  field: css`
    display: grid;
    gap: 4px;
    min-inline-size: 0;
  `,
  toolbar: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
    flex: none;

    @media (pointer: coarse) {
      & button { min-block-size: 3rem; }
    }
  `,
  checkbox: css`
    @media (pointer: coarse) {
      min-block-size: 3rem;
      align-items: center;
    }
  `,
  summary: css`
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
  `,
  review: css`
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 4px 12px;
    flex: none;
    padding-block: 6px;
    border-block-start: 1px solid ${token.colorBorderSecondary};
    background: ${token.colorBgElevated};

    &:empty { display: none; }
  `,
  workspace: css`
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-inline-size: 0;
    min-block-size: 0;
    overflow: hidden;
  `,
  // 右侧独立滚动；stable gutter 避免滚动条出现时把「将授予」挤换行。
  permissionPane: css`
    flex: 1 1 auto;
    min-inline-size: 0;
    min-block-size: 0;
    overflow-y: auto;
    scrollbar-gutter: stable;
    overscroll-behavior: contain;
    border: 1px solid ${token.colorBorderSecondary};
    border-radius: ${token.borderRadius}px;
  `,
  permissionList: css`
    list-style: none;
    margin: 0;
    padding: 0;
  `,
  permissionChildren: css`
    list-style: none;
    margin: 0;
    padding: 0;
  `,
  permissionRow: css`
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: start;
    column-gap: 12px;
    min-inline-size: 0;
    padding: 8px 12px;
    border-block-end: 1px solid ${token.colorBorderSecondary};
  `,
  permissionActions: css`
    display: flex;
    flex-wrap: nowrap;
    align-items: center;
    gap: 8px;
    justify-self: end;

    @media (pointer: coarse) {
      & button { min-block-size: 3rem; }
    }
  `,
  permissionLabel: css`
    display: flex;
    align-items: center;
    column-gap: 4px;
  `,
  disclosure: css`
    flex: none;
    inline-size: 24px;

    @media (pointer: coarse) {
      inline-size: 40px;
      min-block-size: 3rem;
    }
  `,
  permissionMain: css`
    display: grid;
    gap: 3px;
    min-inline-size: 0;
    overflow-wrap: anywhere;
  `,
  permissionDetails: css`
    display: flex;
    flex-wrap: wrap;
    gap: 2px 10px;
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
  `,
}));

function indexPermissions(group: PermissionGroup) {
  const groupsByParent = new Map<string | undefined, PermissionGrantInfo[]>();
  const permissionNames = new Set(group.permissions.map(({ name }) => name));
  const groupLabel = group.displayName || group.name;
  const rows: PermissionRow[] = [];
  const roots: PermissionRow[] = [];
  const visited = new Set<string>();

  for (const permission of group.permissions) {
    const parent =
      permission.parentName && permissionNames.has(permission.parentName)
        ? permission.parentName
        : undefined;
    const siblings = groupsByParent.get(parent) ?? [];
    siblings.push(permission);
    groupsByParent.set(parent, siblings);
  }

  function append(
    permission: PermissionGrantInfo,
    ancestors: string[],
    destination: PermissionRow[],
  ) {
    if (visited.has(permission.name)) return;
    visited.add(permission.name);
    const path = ancestors.join(' › ');
    const row: PermissionRow = {
      permission,
      path,
      // 权限键（如 BizTemplate.Projects.Delete）只进搜索，列表不渲染。
      searchText: [path, group.name, permission.displayName, permission.name]
        .join(' ')
        .toLowerCase(),
      children: [],
    };
    destination.push(row);
    rows.push(row);
    for (const child of groupsByParent.get(permission.name) ?? []) {
      append(
        child,
        [...ancestors, permission.displayName || permission.name],
        row.children,
      );
    }
  }

  for (const permission of [
    ...(groupsByParent.get(undefined) ?? []),
    ...group.permissions,
  ]) {
    append(permission, [groupLabel], roots);
  }

  return {
    group,
    rows,
    roots,
    granted: rows.filter(({ permission }) => permission.isGranted).length,
  };
}

export default function RolePermissionEditorBody({
  groups,
  changes,
  disabled,
  onChange,
}: Props) {
  const { styles } = useStyles();
  const id = useId();
  const filtersRef = useRef<HTMLFormElement>(null);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [selectedGroupName, setSelectedGroupName] = useState<string>();
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const groupRows = useMemo(
    () =>
      groups
        .filter(({ permissions }) => permissions.length > 0)
        .map(indexPermissions),
    [groups],
  );
  const allRows = useMemo(
    () => groupRows.flatMap(({ rows }) => rows),
    [groupRows],
  );
  const selectedGroup =
    groupRows.find(({ group }) => group.name === selectedGroupName) ??
    groupRows[0];
  const searchTerms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const isGlobal = searchTerms.length > 0 || filter === 'pending';
  // 树形浏览用缩进表达层级，不再重复展示分组路径。
  const browsingTree = !isGlobal && filter === 'all';
  const visibleRows = (isGlobal ? allRows : (selectedGroup?.rows ?? [])).filter(
    ({ permission, searchText }) => {
      if (filter === 'granted' && !permission.isGranted) return false;
      if (filter === 'pending' && !changes.has(permission.name)) return false;
      return searchTerms.every((term) => searchText.includes(term));
    },
  );
  const plannedValue = (permission: PermissionGrantInfo) =>
    changes.get(permission.name) ?? permission.isGranted;
  const editableRows = (selectedGroup?.rows ?? []).filter(
    ({ permission }) => permission.isEditable !== false,
  );
  const grantable = editableRows.filter(
    ({ permission }) => !plannedValue(permission),
  );
  const revocable = editableRows.filter(({ permission }) =>
    plannedValue(permission),
  );
  const hasPending = changes.size > 0;
  const reversePermission = allRows.find(
    ({ permission }) => permission.name === voucherReverse,
  )?.permission;
  const viewPermission = allRows.find(
    ({ permission }) => permission.name === voucherView,
  )?.permission;
  const missingVoucherView = !viewPermission || !plannedValue(viewPermission);
  const addingVoucherReverse = changes.get(voucherReverse) === true;
  const removingRequiredView =
    changes.get(voucherView) === false &&
    reversePermission &&
    plannedValue(reversePermission);

  function changeGroup(isGranted: boolean) {
    const updates = (isGranted ? grantable : revocable).map(
      ({ permission }) => ({ name: permission.name, isGranted }),
    );
    onChange(updates);
  }

  function selectGroup(name: string) {
    setSelectedGroupName(name);
    setQuery('');
    if (filter === 'pending') setFilter('all');
  }

  function showPending() {
    // 跨组核对草稿时清掉搜索，避免只看到当前关键词命中的改动。
    setFilter('pending');
    setQuery('');
  }

  function toggleBranch(name: string) {
    setCollapsed((before) => {
      const after = new Set(before);
      if (after.has(name)) after.delete(name);
      else after.add(name);
      return after;
    });
  }

  function renderPermission(row: PermissionRow, depth = 0) {
    const { permission, path, children } = row;
    const { name, displayName, isGranted, isEditable } = permission;
    const label = displayName || name;
    const detailsId = `${id}-${name}`;
    const childrenId = `${detailsId}-children`;
    const change = changes.get(name);
    const expandable = browsingTree && children.length > 0;
    const expanded = expandable && !collapsed.has(name);
    const showPath = !browsingTree && Boolean(path);
    const showVoucherHint = name === voucherReverse;
    const showDetails = showPath || showVoucherHint;

    return (
      <li key={name}>
        <div
          className={styles.permissionRow}
          style={{ paddingInlineStart: 12 + Math.min(depth, 4) * 14 }}
        >
          <div className={styles.permissionMain}>
            <div className={styles.permissionLabel}>
              {expandable ? (
                <Button
                  type="text"
                  size="small"
                  className={styles.disclosure}
                  icon={expanded ? <DownOutlined /> : <RightOutlined />}
                  aria-label={`${expanded ? '折叠' : '展开'}${label}子权限`}
                  aria-expanded={expanded}
                  aria-controls={childrenId}
                  onClick={() => toggleBranch(name)}
                />
              ) : (
                browsingTree && <span className={styles.disclosure} />
              )}
              <Checkbox
                className={styles.checkbox}
                aria-describedby={showDetails ? detailsId : undefined}
                name={name}
                checked={plannedValue(permission)}
                disabled={disabled || isEditable === false}
                onChange={(event) =>
                  onChange([{ name, isGranted: event.target.checked }])
                }
              >
                {label}
              </Checkbox>
            </div>
            {showDetails && (
              <div className={styles.permissionDetails} id={detailsId}>
                {showPath && <span>{path}</span>}
                {showVoucherHint && <span>执行红冲还需查看凭证权限</span>}
              </div>
            )}
          </div>
          {(change !== undefined || isEditable === false) && (
            <div className={styles.permissionActions}>
              {isEditable === false && <Tag>不可修改</Tag>}
              {change !== undefined && (
                <>
                  <Tag color={change ? 'success' : 'error'}>
                    {change ? '将授予' : '将取消'}
                  </Tag>
                  <Button
                    type="link"
                    aria-label={`撤回${label}的变更`}
                    disabled={disabled || isEditable === false}
                    onClick={() => onChange([{ name, isGranted }])}
                  >
                    撤回
                  </Button>
                </>
              )}
            </div>
          )}
        </div>
        {expandable && (
          <ul
            className={styles.permissionChildren}
            id={childrenId}
            hidden={!expanded}
          >
            {expanded &&
              children.map((child) => renderPermission(child, depth + 1))}
          </ul>
        )}
      </li>
    );
  }

  if (!allRows.length) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description="没有可配置的权限"
      />
    );
  }

  return (
    <div className={styles.editor}>
      <form
        aria-label="功能权限筛选"
        className={styles.filters}
        ref={filtersRef}
        onSubmit={(event) => event.preventDefault()}
      >
        <div className={styles.field}>
          <label htmlFor={`${id}-query`}>搜索权限</label>
          <Input
            id={`${id}-query`}
            name="permissionSearch"
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="权限名称"
            prefix={<SearchOutlined aria-hidden />}
            allowClear
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
          />
        </div>
        <div className={styles.selectField}>
          <label htmlFor={`${id}-group`}>权限分组</label>
          <Select
            id={`${id}-group`}
            value={isGlobal ? undefined : selectedGroup?.group.name}
            placeholder="全部分组"
            onChange={selectGroup}
            options={groupRows.map(({ group }) => ({
              value: group.name,
              label: group.displayName || group.name,
            }))}
          />
        </div>
        <div className={styles.toolbar}>
          <Segmented
            aria-label="权限筛选"
            name={`${id}-filter`}
            options={filterOptions}
            value={filter}
            onChange={(value) => {
              if (value === 'pending') showPending();
              else setFilter(value);
            }}
          />
          <span className={styles.summary}>共 {visibleRows.length} 项</span>
        </div>
      </form>
      {(addingVoucherReverse || removingRequiredView) && (
        <Alert
          type="warning"
          title={
            addingVoucherReverse ? '将新增红冲凭证权限' : '红冲凭证仍需查看权限'
          }
          description={
            <div>
              执行红冲并生成红字凭证还需「查看凭证」。
              {missingVoucherView && (
                <div>
                  当前角色的草稿缺少此权限。用户也可以通过其他角色获得查看凭证权限。
                  {viewPermission && viewPermission.isEditable !== false && (
                    <div>
                      <Button
                        size="small"
                        disabled={disabled}
                        onClick={() =>
                          onChange([{ name: voucherView, isGranted: true }])
                        }
                      >
                        同时授予查看凭证
                      </Button>
                    </div>
                  )}
                </div>
              )}
            </div>
          }
          showIcon
        />
      )}
      <div className={styles.columns}>
        <nav className={styles.groupNavigation} aria-label="权限分组导航">
          {groupRows.map(({ group, rows, granted }) => (
            <Button
              key={group.name}
              type="text"
              className={styles.groupButton}
              aria-label={`${group.displayName || group.name}，已授权 ${granted} 项，共 ${rows.length} 项`}
              aria-current={
                !isGlobal && selectedGroup?.group.name === group.name
                  ? 'true'
                  : undefined
              }
              onClick={() => selectGroup(group.name)}
            >
              <span>{group.displayName || group.name}</span>
              <span
                className={styles.groupCount}
                title="已授权 / 总数"
                aria-hidden
              >
                {granted}/{rows.length}
              </span>
            </Button>
          ))}
        </nav>
        <div className={styles.workspace}>
          {browsingTree && (
            <div className={styles.toolbar}>
              <Button
                disabled={disabled || !grantable.length}
                onClick={() => changeGroup(true)}
              >
                授予本组 {grantable.length} 项
              </Button>
              <Button
                disabled={disabled || !revocable.length}
                onClick={() => changeGroup(false)}
              >
                撤销本组 {revocable.length} 项
              </Button>
            </div>
          )}
          <section className={styles.permissionPane} aria-label="功能权限">
            {visibleRows.length ? (
              <ul className={styles.permissionList}>
                {(browsingTree
                  ? (selectedGroup?.roots ?? [])
                  : visibleRows
                ).map((row) => renderPermission(row))}
              </ul>
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={
                  filter === 'pending' && !hasPending
                    ? '还没有改动'
                    : '没有符合条件的权限'
                }
              />
            )}
          </section>
        </div>
      </div>
      <div className={styles.review}>
        {hasPending && (
          <>
            <span className={styles.summary} role="status">
              {changes.size} 项改动尚未保存，保存后才会生效
            </span>
            <Button
              type="link"
              onClick={() => {
                showPending();
                filtersRef.current?.scrollIntoView?.({ block: 'start' });
              }}
            >
              只看这些改动
            </Button>
          </>
        )}
      </div>
    </div>
  );
}
