import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery } from './gridQuery';
import { exportIdentityUserQuery, queryIdentityUsers } from './identity';

const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('@umijs/max', () => ({ history: { replace: vi.fn() }, request }));

const query: GridQuery = {
  skipCount: 0,
  maxResultCount: 20,
  keyword: 'alice',
  filters: [
    {
      field: 'isLocked',
      join: 'and',
      conditions: [{ op: 'eq', value: 'false' }],
    },
  ],
  sort: [{ field: 'creationTime', direction: 'desc' }],
};

describe('用户网格 API', () => {
  beforeEach(() => request.mockReset());
  afterEach(() => vi.restoreAllMocks());

  it('远程数据源请求使用用户搜索服务并透传中止信号', async () => {
    const signal = new AbortController().signal;
    const result = { items: [], totalCount: 15 };
    request.mockResolvedValue(result);
    await expect(queryIdentityUsers(query, signal)).resolves.toEqual(result);
    expect(request).toHaveBeenCalledWith(
      '/api/app/identity-user-search/query',
      {
        method: 'POST',
        data: query,
        signal,
        skipErrorHandler: true,
      },
    );
  });

  it('用相同查询导出 Excel 并触发浏览器下载', async () => {
    const contentType =
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    const file = new Blob(['demo-xlsx'], { type: contentType });
    request.mockResolvedValue({
      status: 200,
      headers: { 'content-type': contentType },
      data: file,
    });
    const url = vi
      .spyOn(URL, 'createObjectURL')
      .mockReturnValue('blob:user-export');
    const downloads: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      expect(this.href).toBe('blob:user-export');
      downloads.push(this.download);
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    await expect(exportIdentityUserQuery(query)).resolves.toMatchObject({
      isQueued: false,
    });
    expect(request).toHaveBeenCalledExactlyOnceWith(
      '/api/app/identity-user-admin/export-query',
      {
        method: 'POST',
        data: query,
        responseType: 'blob',
        getResponse: true,
      },
    );
    expect(url).toHaveBeenCalledWith(file);
    expect(downloads).toEqual([expect.stringMatching(/\.xlsx$/i)]);
  });

  it('大结果集读取排队通知，不下载 JSON 或二次提交任务', async () => {
    const queued = { isQueued: true, message: '导出任务已排队' };
    request.mockResolvedValue({
      status: 202,
      headers: { 'content-type': 'application/json; charset=utf-8' },
      data: new Blob([JSON.stringify(queued)], { type: 'application/json' }),
    });
    const download = vi.spyOn(HTMLAnchorElement.prototype, 'click');
    await expect(exportIdentityUserQuery(query)).resolves.toEqual(queued);
    expect(request).toHaveBeenCalledTimes(1);
    expect(download).not.toHaveBeenCalled();
  });
});
