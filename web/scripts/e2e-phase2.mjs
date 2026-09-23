/**
 * Phase 2 browser loop: identity CRUD + subdomain tenant.
 * Run: node scripts/e2e-phase2.mjs
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
const stamp = Date.now().toString().slice(-6);
const userName = `e2euser${stamp}`;
const roleName = `e2erole${stamp}`;
const tenantName = 'demo';

function shot(page, name) {
  return page.screenshot({
    path: path.join(outDir, `${name}.png`),
    fullPage: true,
  });
}

async function waitReady(page, url, timeout = 120000) {
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout });
}

async function loginViaAbp(page, origin = FRONT) {
  await waitReady(page, `${origin}/user/login`);
  if (!page.url().includes('/user/login')) {
    await page.locator('.ant-pro-global-header, .ant-layout-header').first().waitFor({ timeout: 60000 });
    return;
  }
  await page.getByTestId('abp-login').click();
  await page.waitForURL(/\/Account\/Login|\/user\/callback|\/welcome/i, { timeout: 60000 });
  if (/Account\/Login/i.test(page.url())) {
    const expectedTenant = new URL(origin).hostname.replace(/\.localhost$/, '');
    if (expectedTenant && expectedTenant !== 'localhost' && expectedTenant !== '127.0.0.1') {
      if (await page.getByText('未选择').count()) {
        await page.getByText('切换', { exact: true }).click();
        const tenantInput = page.locator('input:visible').last();
        await tenantInput.waitFor({ timeout: 10000 });
        await tenantInput.fill(expectedTenant);
        await page.locator('button[type="submit"]:visible').last().click();
        await page.getByText(expectedTenant).first().waitFor({ timeout: 20000 });
      }
    }
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
  await page.locator('.ant-tag').filter({ hasText: /Host|demo/ }).waitFor({ timeout: 60000 });
}

async function openModalByButton(page, buttonText) {
  await page.getByRole('button', { name: buttonText }).click();
  await page.locator('.ant-modal').waitFor({ state: 'visible', timeout: 15000 });
}

async function fillFormItem(page, label, value, { password = false } = {}) {
  const item = page.locator('.ant-modal .ant-form-item').filter({ hasText: label }).first();
  const input = password
    ? item.locator('input[type="password"]')
    : item.locator('input:not([type="password"])');
  await input.fill(value);
}

async function submitModal(page) {
  await page.locator('.ant-modal-footer .ant-btn-primary').click();
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
  const errors = [];
  page.on('pageerror', (err) => errors.push(String(err)));

  try {
    console.log('1) Host login');
    await loginViaAbp(page);
    await page.locator('.ant-pro-global-header').waitFor({ timeout: 30000 });
    await shot(page, '01-host-welcome');

    console.log('2) Users CRUD');
    await page.goto(`${FRONT}/identity/users`);
    await page.getByRole('button', { name: '新建用户' }).waitFor({ timeout: 30000 });
    await page.getByText('admin').first().waitFor({ timeout: 30000 });
    await shot(page, '02-users');
    await openModalByButton(page, '新建用户');
    await fillFormItem(page, '用户名', userName);
    await fillFormItem(page, '邮箱', `${userName}@example.com`);
    await fillFormItem(page, '密码', ADMIN_PASS, { password: true });
    await submitModal(page);
    await page.getByText('已保存').waitFor({ timeout: 15000 });
    await page.getByText(userName).first().waitFor();
    await shot(page, '03-user-created');

    console.log('3) Roles CRUD');
    await page.goto(`${FRONT}/identity/roles`);
    await page.getByRole('button', { name: '新建角色' }).waitFor({ timeout: 30000 });
    await openModalByButton(page, '新建角色');
    await fillFormItem(page, '名称', roleName);
    await submitModal(page);
    await page.getByText('已创建').waitFor({ timeout: 15000 });
    await page.getByText(roleName).first().waitFor();
    await shot(page, '04-role-created');

    console.log('4) Role permissions');
    const roleRow = page.locator('.ant-table-row').filter({ hasText: roleName });
    await roleRow.getByText('权限').click();
    await page.locator('.ant-modal .ant-tree, .ant-modal .ant-spin').waitFor({ timeout: 20000 });
    await page.waitForTimeout(800);
    const treeNode = page.locator('.ant-modal .ant-tree-checkbox').first();
    if (await treeNode.count()) {
      await treeNode.click();
    }
    await page.locator('.ant-modal').getByRole('button', { name: /确\s*定|确定/ }).click();
    await page.getByText('权限已保存').waitFor({ timeout: 15000 });
    await shot(page, '05-role-permissions');

    console.log('5) Tenant create/ensure demo');
    await page.goto(`${FRONT}/tenants`);
    await page.getByRole('button', { name: '新建租户' }).waitFor({ timeout: 30000 });
    const existing = await page.getByText(tenantName, { exact: true }).count();
    if (!existing) {
      await openModalByButton(page, '新建租户');
      await fillFormItem(page, '租户名称', tenantName);
      await fillFormItem(page, '管理员邮箱', 'admin@demo.local');
      await fillFormItem(page, '管理员密码', ADMIN_PASS, { password: true });
      await submitModal(page);
      await page.getByText('已创建').waitFor({ timeout: 30000 });
    }
    await shot(page, '06-tenants');

    console.log('6) Missing subdomain');
    const missing = await context.newPage();
    await missing.goto('http://missing-tenant.localhost:8000/user/login', {
      waitUntil: 'domcontentloaded',
      timeout: 60000,
    });
    await missing.getByText('租户不存在').waitFor({ timeout: 30000 });
    await shot(missing, '07-tenant-not-found');
    await missing.close();

    console.log('7) demo.localhost tenant login');
    const tenantContext = await browser.newContext({
      ignoreHTTPSErrors: true,
      locale: 'zh-CN',
      viewport: { width: 1440, height: 900 },
    });
    const tenantPage = await tenantContext.newPage();
    tenantPage.setDefaultTimeout(20000);
    try {
      await loginViaAbp(tenantPage, 'http://demo.localhost:8000');
      await shot(tenantPage, '08-demo-welcome');
      await tenantPage.locator('.ant-tag').filter({ hasText: 'demo' }).waitFor({ timeout: 30000 });
      if (await tenantPage.getByRole('link', { name: '租户管理' }).count()) {
        throw new Error('Tenant admin should not see Host tenant management');
      }
      await tenantPage.goto('http://demo.localhost:8000/identity/users');
      await tenantPage.getByRole('button', { name: '新建用户' }).waitFor({ timeout: 30000 });
      const hostUserVisible = await tenantPage.getByText(userName).count();
      if (hostUserVisible) {
        throw new Error('Host user leaked into tenant user list');
      }
      await shot(tenantPage, '09-demo-users');
    } catch (tenantErr) {
      await shot(tenantPage, 'zz-tenant-failure').catch(() => undefined);
      console.error('tenant url', tenantPage.url());
      console.error(
        'tenant body',
        (await tenantPage.locator('body').innerText().catch(() => '')).slice(0, 800),
      );
      throw tenantErr;
    } finally {
      await tenantContext.close();
    }

    console.log('OK phase2 passed');
    console.log(JSON.stringify({ userName, roleName, tenantName, errors }, null, 2));
  } catch (err) {
    await shot(page, 'zz-failure').catch(() => undefined);
    console.error('FAIL', err);
    console.error('url', page.url());
    console.error('errors', errors);
    console.error('cookies', await context.cookies());
    console.error('body', (await page.locator('body').innerText().catch(() => '')).slice(0, 800));
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
