/**
 * Smoke ABP OSS admin pages: settings, host features, account center, lock.
 * Run: node scripts/e2e-abp-modules.mjs
 */
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.join(__dirname, '..', 'e2e-output');
fs.mkdirSync(outDir, { recursive: true });

const FRONT = 'http://localhost:8000';
const ADMIN_USER = 'admin';
const ADMIN_PASS = '1q2w3E*';

function shot(page, name) {
  return page.screenshot({
    path: path.join(outDir, `${name}.png`),
    fullPage: true,
  });
}

async function loginViaAbp(page, origin = FRONT) {
  await page.goto(`${origin}/user/login`, {
    waitUntil: 'domcontentloaded',
    timeout: 120000,
  });
  if (!page.url().includes('/user/login')) {
    await page
      .locator('.ant-pro-global-header, .ant-layout-header')
      .first()
      .waitFor({ timeout: 60000 });
    return;
  }
  await page.getByTestId('abp-login').click();
  await page.waitForURL(/\/Account\/Login|\/user\/callback|\/welcome/i, {
    timeout: 60000,
  });
  if (/Account\/Login/i.test(page.url())) {
    const user = page.locator(
      'input[name$="UserNameOrEmailAddress"], input[id$="UserNameOrEmailAddress"]',
    );
    const password = page.locator('input[type="password"]');
    await user.waitFor({ timeout: 30000 });
    await user.fill(ADMIN_USER);
    await password.fill(ADMIN_PASS);
    await page.locator('form button[type="submit"], button[type="submit"]').first().click();
  }
  await page.waitForURL(
    (url) =>
      url.hostname === new URL(origin).hostname &&
      url.port === '8000' &&
      !url.pathname.includes('/user/callback') &&
      !url.pathname.includes('/user/login') &&
      !url.pathname.includes('/Account/'),
    { timeout: 60000 },
  );
  await page.waitForFunction(() => {
    for (let i = 0; i < localStorage.length; i += 1) {
      const key = localStorage.key(i);
      if (key && key.includes('oidc.user') && localStorage.getItem(key)) {
        return true;
      }
    }
    return false;
  }, { timeout: 60000 });
}

async function api(page, url, options = {}) {
  return page.evaluate(
    async ({ url, options }) => {
      const headers = { ...(options.headers || {}) };
      for (let i = 0; i < localStorage.length; i += 1) {
        const key = localStorage.key(i);
        if (key && key.includes('oidc.user')) {
          const raw = localStorage.getItem(key);
          if (!raw) continue;
          const user = JSON.parse(raw);
          if (user.access_token) {
            headers.Authorization = `Bearer ${user.access_token}`;
          }
        }
      }
      const res = await fetch(url, { ...options, headers });
      const text = await res.text();
      let body = text;
      try {
        body = text ? JSON.parse(text) : null;
      } catch {
        // keep text
      }
      return { status: res.status, body };
    },
    { url, options },
  );
}

async function main() {
  const browser = await chromium.launch({
    channel: 'chrome',
    headless: true,
    args: ['--ignore-certificate-errors'],
  });
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    locale: 'zh-CN',
    viewport: { width: 1440, height: 900 },
  });
  const page = await context.newPage();
  page.setDefaultTimeout(20000);
  const failures = [];

  try {
    console.log('1) login');
    await loginViaAbp(page);
    await page.locator('.ant-pro-global-header').waitFor({ timeout: 30000 });
    await shot(page, 'm01-welcome');

    console.log('2) APIs');
    const checks = [
      ['GET account-settings', '/api/app/account-settings'],
      ['GET identity-settings', '/api/app/identity-settings'],
      ['GET emailing', '/api/setting-management/emailing'],
      ['GET timezone', '/api/setting-management/timezone'],
      ['GET host features', '/api/feature-management/features?providerName=T&providerKey='],
    ];
    for (const [name, url] of checks) {
      const result = await api(page, url);
      console.log(name, result.status);
      if (result.status >= 400) {
        failures.push(`${name} ${result.status} ${JSON.stringify(result.body).slice(0, 300)}`);
      }
    }

    console.log('3) settings page');
    await page.goto(`${FRONT}/settings`);
    await page.getByText('邮件').first().waitFor({ timeout: 30000 });
    for (const tab of ['邮件', '时区', '账户', '身份']) {
      const tabNode = page.getByRole('tab', { name: tab, exact: true });
      if (!(await tabNode.count())) {
        failures.push(`missing settings tab: ${tab}`);
        continue;
      }
      await tabNode.click();
      await page.waitForTimeout(400);
    }
    await shot(page, 'm02-settings');

    await page.getByRole('tab', { name: '账户' }).click();
    await page.getByRole('button', { name: '保存账户设置' }).waitFor({ timeout: 15000 });
    await page.locator('.ant-form-item').filter({ hasText: '启用本地登录' }).locator('.ant-switch-checked').waitFor({ timeout: 15000 });
    await page.getByRole('button', { name: '保存账户设置' }).click();
    await page.getByText('账户设置已保存').waitFor({ timeout: 15000 });

    console.log('4) account center');
    await page.goto(`${FRONT}/account/center`);
    await page.getByText('个人资料').first().waitFor({ timeout: 30000 });
    await page.getByText('修改密码').first().waitFor({ timeout: 15000 });
    await shot(page, 'm03-account-center');

    console.log('5) tenants host features');
    await page.goto(`${FRONT}/tenants`);
    await page.getByRole('button', { name: '管理 Host 功能' }).waitFor({ timeout: 30000 });
    await page.getByRole('button', { name: '管理 Host 功能' }).click();
    await page.locator('.ant-modal').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(800);
    await shot(page, 'm04-host-features');
    await page.locator('.ant-modal-close').click();

    console.log('6) users lock');
    await page.goto(`${FRONT}/identity/users`);
    await page.getByRole('button', { name: '新建用户' }).waitFor({ timeout: 30000 });
    const stamp = Date.now().toString().slice(-6);
    const userName = `lockuser${stamp}`;
    await page.getByRole('button', { name: '新建用户' }).click();
    await page.locator('.ant-modal').waitFor({ state: 'visible' });
    const fill = async (label, value, password = false) => {
      const item = page.locator('.ant-modal .ant-form-item').filter({ hasText: label }).first();
      const input = password
        ? item.locator('input[type="password"]')
        : item.locator('input:not([type="password"])');
      await input.fill(value);
    };
    await fill('用户名', userName);
    await fill('邮箱', `${userName}@example.com`);
    await fill('密码', ADMIN_PASS, true);
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    await page.getByText('已保存').waitFor({ timeout: 15000 });
    const row = page.locator('.ant-table-row').filter({ hasText: userName });
    await row.getByText('冻结').click();
    await page.locator('.ant-popconfirm').getByRole('button', { name: /确/ }).click();
    await page.getByText('已冻结').waitFor({ timeout: 15000 });
    await row.getByText('解冻').click();
    await page.locator('.ant-popconfirm').getByRole('button', { name: /确/ }).click();
    await page.getByText('已解冻').waitFor({ timeout: 15000 });
    await shot(page, 'm05-users-lock');

    if (failures.length) {
      throw new Error(failures.join('\n'));
    }
    console.log('OK abp modules passed');
  } catch (err) {
    await shot(page, 'mzz-failure').catch(() => undefined);
    console.error('FAIL', err);
    console.error('url', page.url());
    console.error(
      'body',
      (await page.locator('body').innerText().catch(() => '')).slice(0, 1200),
    );
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
