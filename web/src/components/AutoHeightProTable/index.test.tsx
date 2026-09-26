import { render, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

/**
 * AutoHeightProTable 可测纯逻辑：scroll.x 求和（含选择/展开隐形列宽）、
 * 列宽拖拽状态键、scroll 透传优先级。布局测量（ResizeObserver/Pointer）在
 * jsdom 下不可测，不在此覆盖。
 */

let capturedProps: Record<string, any> | undefined;

vi.mock('@ant-design/pro-components', () => ({
  ProTable: (props: any) => {
    capturedProps = props;
    return <div data-testid="pro-table" />;
  },
}));

vi.mock('antd-style', () => ({
  // 真实 useStyles() 返回 { styles, cx, theme }，styles 扁平承载各 key
  createStyles: () => () => ({
    styles: {
      wrap: 'wrap-class',
      tableFlex: 'table-flex-class',
      resizableTh: 'resizable-th-class',
      resizeHandle: 'resize-handle-class',
    },
  }),
}));

const { default: AutoHeightProTable } = await import('./index');

function renderTable(props: Record<string, any>) {
  capturedProps = undefined;
  const view = render(<AutoHeightProTable {...props} />);
  return {
    view,
    props: () => capturedProps as Record<string, any>,
  };
}

describe('AutoHeightProTable', () => {
  it('scroll.x 按列宽数字求和，字符串宽度不计入', () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b', width: '30%' },
        { title: 'C', dataIndex: 'c', width: 200 },
      ],
    });
    expect(props().scroll).toMatchObject({ x: 300 });
  });

  it('全列无数字宽度时不注入 scroll.x', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a' }],
    });
    expect(props().scroll?.x).toBeUndefined();
  });

  it('隐形列宽计入：rowSelection 默认 32、expandable 默认 48，可被 columnWidth 覆盖', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      rowSelection: {},
      expandable: { expandedRowRender: () => <div /> },
    });
    expect(props().scroll).toMatchObject({ x: 180 });

    const { props: props2 } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      rowSelection: { columnWidth: 60 },
    });
    expect(props2().scroll).toMatchObject({ x: 160 });
  });

  it('页面显式传入的 scroll.x / scroll.y 优先，不被组件覆盖', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      scroll: { x: 888, y: 666 },
    });
    expect(props().scroll).toMatchObject({ x: 888, y: 666 });
  });

  it('拖拽调宽：onResize 触发内部状态，该列 width 与 scroll.x 同步更新', async () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b', width: 100 },
      ],
    });
    const colA = props().columns.find((c: any) => c.dataIndex === 'a');
    colA.onHeaderCell(colA).onResize(260);

    await waitFor(() => {
      const current = props().columns.find((c: any) => c.dataIndex === 'a');
      expect(current.width).toBe(260);
      // scroll.x 同步：260 + 100
      expect(props().scroll).toMatchObject({ x: 360 });
    });
  });

  it('resizable=false 不注入 onHeaderCell', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      resizable: false,
    });
    expect(props().columns[0].onHeaderCell).toBeUndefined();
  });

  it('数字 width 列注入 onHeaderCell（width + onResize），无 width 列不注入', () => {
    const { props } = renderTable({
      columns: [
        { title: 'A', dataIndex: 'a', width: 100 },
        { title: 'B', dataIndex: 'b' },
      ],
    });
    const colA = props().columns[0];
    const headerProps = colA.onHeaderCell(colA);
    expect(headerProps.width).toBe(100);
    expect(typeof headerProps.onResize).toBe('function');
    expect(props().columns[1].onHeaderCell).toBeUndefined();
  });

  it('fillViewport=false 不注入 section 高度与内部滚动', () => {
    const { props } = renderTable({
      columns: [{ title: 'A', dataIndex: 'a', width: 100 }],
      fillViewport: false,
    });
    expect(props().styles?.section).toBeUndefined();
  });
});
