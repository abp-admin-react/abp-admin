import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import { getBalanceProjections } from './finance';

// A7 余额投影镜像层聚合锁（OCR 补）：后端逐子户行 → 每 (用户,币种) 一行三列，
// 科目码 2201/2202/2205 的映射与平台行保留是本页唯一的展示口径——改码/改形态必须过这关。
describe('aggregateBalanceRows via getBalanceProjections (live)', () => {
  beforeEach(() => {
    localStorage.clear();
    request.mockReset();
  });

  const subRows = [
    { accountCode: '2201', ownerType: 1, ownerId: 'u-1', currency: 'USDT', balance: 5000 },
    { accountCode: '2202', ownerType: 1, ownerId: 'u-1', currency: 'USDT', balance: 100 },
    { accountCode: '2205', ownerType: 1, ownerId: 'u-2', currency: 'CNY', balance: 810 },
    { accountCode: '1001', ownerType: 0, ownerId: '', currency: 'USDT', balance: 5000 },
  ];

  it('用户行按 (用户,币种) 聚合并填三列；平台行保留且 ownerName=科目码', async () => {
    request.mockResolvedValue({ totalCount: subRows.length, items: subRows });
    const result = await getBalanceProjections({ skipCount: 0, maxResultCount: 20 });
    const items = result.items ?? [];

    const u1 = items.find((x) => x.ownerId === 'u-1');
    expect(u1).toMatchObject({ available: 5000, frozen: 100, prepayment: 0, ledgerDerived: 5100 });

    const u2 = items.find((x) => x.ownerId === 'u-2');
    expect(u2).toMatchObject({ prepayment: 810, available: 0 });

    const platform = items.find((x) => x.ownerId === '' );
    expect(platform).toMatchObject({ ownerName: '1001', available: 5000 });
  });

  it('分页拉全：满页继续翻页直到不满页；不满页立即停（末页语义）', async () => {
    const filler = Array.from({ length: 199 }, (_, i) => ({
      accountCode: '2201', ownerType: 1, ownerId: `fill-${i}`, currency: 'USDT', balance: 1,
    }));
    request.mockImplementation(async (_url: string, opts?: { params?: Record<string, unknown> }) => {
      const skip = (opts?.params?.SkipCount as number) ?? 0;
      // 第一页满 200（filler + u-1），第二页只剩 u-2/平台（不满页 = 末页，停止翻页）
      const all = [...filler, subRows[0], subRows[1], subRows[2], subRows[3]];
      return { totalCount: all.length, items: all.slice(skip, skip + 200) };
    });
    const result = await getBalanceProjections({ maxResultCount: 100 });
    expect(request).toHaveBeenCalledTimes(2);
    // 跨页拉全的证据：totalCount 是聚合前行数派生（202 行 = 199 filler + u-1 + u-2 + 平台），
    // 且第一页末尾的 u-1（跨页边界行）确实进了聚合（pageOf 上限 100，u-2 在尾部由 totalCount 覆盖）
    expect(result.totalCount).toBe(202); // 203 行拉全 → 202 聚合行（若第二页没拉，只会是 200）
  });
});
