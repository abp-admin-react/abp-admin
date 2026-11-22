import { describe, expect, it, vi } from 'vitest';
import { postGridQuery } from './gridQuery';

const { replace, request } = vi.hoisted(() => ({
  replace: vi.fn(),
  request: vi.fn(),
}));
vi.mock('@umijs/max', () => ({ history: { replace }, request }));

describe('网格请求的登录状态', () => {
  it.each([false, true])(
    '登录过期时只为活动请求跳转（已取消=%s）',
    async (aborted) => {
      replace.mockClear();
      const controller = new AbortController();
      if (aborted) controller.abort();
      const error = { response: { status: 401 } };
      request.mockRejectedValueOnce(error);
      await expect(
        postGridQuery(
          '/grid',
          { skipCount: 0, maxResultCount: 20, filters: [], sort: [] },
          controller.signal,
        ),
      ).rejects.toBe(error);
      expect(replace).toHaveBeenCalledTimes(aborted ? 0 : 1);
      if (!aborted) expect(replace).toHaveBeenCalledWith('/user/login?retry=1');
    },
  );
});
