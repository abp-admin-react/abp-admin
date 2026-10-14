import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

const BASE = process.env.ABP_BASE || 'http://127.0.0.1:8100';
const OUT = process.env.ABP_SHOT_DIR || 'C:\\Users\\Administrator\\agent-tools\\abp-shots';
fs.mkdirSync(OUT, { recursive: true });

const pages = [
  ['overview', '/finance/overview'],
  ['accounts', '/finance/accounts'],
  ['rules', '/finance/rules'],
  ['vouchers', '/finance/vouchers'],
  ['period', '/finance/period'],
  ['reconcile', '/finance/reconcile'],
  ['biz-docs', '/finance/biz-docs'],
  ['balances', '/finance/balances'],
];

const financePolicies = {
  'Finance.Ledger.View': true,
  'Finance.Ledger.Export': true,
  'Finance.Account.Manage': true,
  'Finance.Rule.View': true,
  'Finance.Voucher.View': true,
  'Finance.Voucher.Reverse': true,
  'Finance.Adjust.Create': true,
  'Finance.Adjust.Approve': true,
  'Finance.Reconcile.Run': true,
  'Finance.Period.View': true,
  'Finance.BizDoc.View': true,
  'Finance.Balance.View': true,
};

const appConfig = {
  currentUser: {
    isAuthenticated: true,
    id: 'user-boss',
    userName: 'boss',
    name: '老板',
    email: 'boss@local',
    roles: ['admin'],
  },
  auth: { grantedPolicies: { ...financePolicies, 'AbpIdentity.Users': true } },
  currentTenant: { isAvailable: false },
  localization: { currentCulture: { cultureName: 'zh-CN' }, values: {} },
  setting: { values: {} },
  features: { values: {} },
  multiTenancy: { isEnabled: true },
  clock: { kind: 'Unspecified' },
  timing: {},
};

const oidcUser = {
  access_token: 'shell-mock-token',
  token_type: 'Bearer',
  profile: { sub: 'user-boss', name: '老板', preferred_username: 'boss' },
  expires_at: Math.floor(Date.now() / 1000) + 3600 * 8,
  scope: 'openid profile email roles AbpAdmin',
};

async function main() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    locale: 'zh-CN',
  });
  const page = await context.newPage();

  await page.route('**/api/abp/application-configuration**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(appConfig),
    });
  });
  await page.route('**/api/abp/application-localization**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ resources: {} }),
    });
  });
  // swallow other /api that might 404 during shell shot
  await page.route('**/api/**', async (route) => {
    const url = route.request().url();
    if (url.includes('application-configuration') || url.includes('application-localization')) {
      return route.fallback();
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({}),
    });
  });

  await page.addInitScript(
    ({ user, policies }) => {
      localStorage.setItem('finance.shellAccess', '1');
      localStorage.setItem('finance.useMock', '1');
      const origin = window.location.origin;
      const key = `oidc.user:${origin}:AbpAdmin_App`;
      localStorage.setItem(key, JSON.stringify(user));
      // belt-and-suspenders for access.ts
      window.__FINANCE_SHELL__ = policies;
    },
    { user: oidcUser, policies: financePolicies },
  );

  const results = [];
  for (const [name, routePath] of pages) {
    const url = BASE + routePath;
    console.log('goto', url);
    try {
      await page.goto(url, { waitUntil: 'networkidle', timeout: 60000 });
      await page.waitForTimeout(1500);
      // If still on login, record
      const title = await page.title();
      const bodyText = await page.locator('body').innerText().catch(() => '');
      const shot = path.join(OUT, `A-${name}.png`);
      await page.screenshot({ path: shot, fullPage: true });
      results.push({ name, routePath, title, shot, snippet: bodyText.slice(0, 120).replace(/\s+/g, ' ') });
      console.log('ok', name, title);
    } catch (e) {
      console.error('fail', name, e.message);
      results.push({ name, routePath, error: e.message });
    }
  }

  fs.writeFileSync(path.join(OUT, 'shots-report.json'), JSON.stringify(results, null, 2), 'utf8');
  await browser.close();
  console.log('done', OUT);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
