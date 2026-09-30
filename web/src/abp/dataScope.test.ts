import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * saveRoleDataScope 的 404 判别契约：仅 404（未配置过数据范围）走创建，其它读失败
 * （403/500/网络）必须上抛——读失败被当成"不存在"会把用户的更新静默变成新建，
 * 再撞角色名唯一键报一个错乱的错。dataScope.ts 与 requestErrorConfig.ts 之外的
 * isNotFound 分流此前零测试。
 */

vi.mock('@umijs/max', () => ({
  request: vi.fn(),
}));

import { request } from '@umijs/max';
import {
  DataScopeType,
  saveRoleDataScope,
} from './dataScope';

const mockRequest = vi.mocked(request);

/** umi request 的重载让 vi.mocked 的调用元组退化为 [url] 单元组——断言要读第二参，取值处放宽 */
const calls = () =>
  mockRequest.mock.calls as unknown as Array<
    [string, Record<string, unknown>?]
  >;

const payload = {
  roleName: 'operator',
  scopeType: DataScopeType.CurrentOu,
};

function httpError(status: number) {
  return Object.assign(new Error(`HTTP ${status}`), {
    response: { status },
  });
}

describe('saveRoleDataScope 404 判别', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('读到 404（未配置过）→ 走创建', async () => {
    mockRequest.mockRejectedValueOnce(httpError(404));
    mockRequest.mockResolvedValueOnce({ id: 'new' });

    await saveRoleDataScope(payload);

    expect(mockRequest).toHaveBeenCalledTimes(2);
    expect(calls()[1][1]).toMatchObject({ method: 'POST' });
  });

  it('读到已有记录 → 走更新', async () => {
    mockRequest.mockResolvedValueOnce({ id: 'existing-1' });
    mockRequest.mockResolvedValueOnce({ id: 'existing-1' });

    await saveRoleDataScope(payload);

    expect(mockRequest).toHaveBeenCalledTimes(2);
    expect(calls()[1][0]).toContain('/role-data-scope/existing-1');
    expect(calls()[1][1]).toMatchObject({ method: 'PUT' });
  });

  it.each([403, 500])(
    '读到 %d（读失败，不是不存在）→ 上抛而不是误创建',
    async (status) => {
      mockRequest.mockRejectedValueOnce(httpError(status));

      await expect(saveRoleDataScope(payload)).rejects.toThrow();

      // 只发起过读取，绝不能带着误判进入创建
      expect(mockRequest).toHaveBeenCalledTimes(1);
      expect(calls()[0][1]).toMatchObject({ method: 'GET' });
    },
  );

  it('读到无 response 形态的错误（网络层）→ 上抛', async () => {
    mockRequest.mockRejectedValueOnce(new Error('Network Error'));

    await expect(saveRoleDataScope(payload)).rejects.toThrow();
    expect(mockRequest).toHaveBeenCalledTimes(1);
  });
});
