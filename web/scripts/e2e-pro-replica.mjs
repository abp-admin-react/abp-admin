/**
 * Smoke ABP Pro replica admin pages.
 * Run: node scripts/e2e-pro-replica.mjs
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

async function openPage(page, pathName) {
  await page.goto(`${FRONT}${pathName}`);
  await page
    .locator('.ant-pro-page-container, .ant-pro-table, .ant-pro-card')
    .first()
    .waitFor({ timeout: 30000 });
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

    console.log('2) APIs');
    const checks = [
      ['GET organization-unit', '/api/app/organization-unit'],
      ['GET claim-type', '/api/app/claim-type'],
      ['GET security-log', '/api/app/security-log'],
      ['GET identity-session', '/api/app/identity-session'],
      ['GET audit-log', '/api/app/audit-log'],
      ['GET open-iddict-application', '/api/app/open-iddict-application'],
      ['GET open-iddict-scope', '/api/app/open-iddict-scope'],
      ['GET edition', '/api/app/edition'],
      ['GET edition/lookup', '/api/app/edition/lookup'],
      ['GET file-record', '/api/app/file-record'],
      ['GET text-template', '/api/app/text-template'],
      ['GET language', '/api/app/language'],
      ['GET personal-data', '/api/app/personal-data'],
      ['GET background-job', '/api/app/background-job'],
      ['GET virtual-file-explorer', '/api/app/virtual-file-explorer'],
    ];
    for (const [name, url] of checks) {
      const result = await api(page, url);
      console.log(name, result.status);
      if (result.status >= 400) {
        failures.push(
          `${name} ${result.status} ${JSON.stringify(result.body).slice(0, 400)}`,
        );
      }
    }

    console.log('3) pages');
    const pages = [
      '/identity/organization-units',
      '/identity/claim-types',
      '/identity/security-logs',
      '/identity/sessions',
      '/audit-logs',
      '/openiddict/applications',
      '/openiddict/scopes',
      '/saas/editions',
      '/saas/tenants',
      '/files',
      '/text-templates',
      '/settings',
      '/account/center',
      '/background-jobs',
      '/virtual-file-explorer',
      '/features',
    ];
    for (const pathName of pages) {
      console.log('open', pathName);
      await openPage(page, pathName);
      await shot(page, `pro${pathName.replaceAll('/', '-')}`);
    }

    console.log('4) create edition and bind tenant');
    await page.goto(`${FRONT}/saas/editions`);
    await page.getByRole('button', { name: /新/ }).first().click();
    await page.locator('.ant-modal').waitFor({ state: 'visible' });
    const editionName = `标准版${Date.now().toString().slice(-6)}`;
    await page
      .locator('.ant-modal .ant-form-item')
      .filter({ hasText: '名称' })
      .locator('input')
      .fill(editionName);
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    await page.getByText('已创建').waitFor({ timeout: 15000 });
    await page.getByText(editionName).waitFor({ timeout: 15000 });

    const create = await api(page, '/api/app/edition', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ displayName: `API版${Date.now().toString().slice(-4)}` }),
    });
    console.log('POST edition', create.status);
    if (create.status >= 400) {
      failures.push(`POST edition ${create.status} ${JSON.stringify(create.body).slice(0, 300)}`);
    }

    const ou = await api(page, '/api/app/organization-unit', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ displayName: `OU${Date.now().toString().slice(-4)}` }),
    });
    console.log('POST organization-unit', ou.status);
    if (ou.status >= 400) {
      failures.push(`POST OU ${ou.status} ${JSON.stringify(ou.body).slice(0, 300)}`);
    }

    const claim = await api(page, '/api/app/claim-type', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: `ct${Date.now().toString().slice(-4)}`, valueType: 0 }),
    });
    console.log('POST claim-type', claim.status);
    if (claim.status >= 400) {
      failures.push(`POST claim-type ${claim.status} ${JSON.stringify(claim.body).slice(0, 300)}`);
    }

    const templates = await api(page, '/api/app/text-template');
    const firstTemplate = Array.isArray(templates.body)
      ? templates.body[0]
      : templates.body?.items?.[0];
    if (firstTemplate?.name) {
      const content = await api(
        page,
        `/api/app/text-template/content?name=${encodeURIComponent(firstTemplate.name)}`,
      );
      console.log('GET text-template/content', content.status);
      if (content.status >= 400) {
        failures.push(`GET template content ${content.status}`);
      }
    }

    if (failures.length) {
      throw new Error(failures.join('\n'));
    }
    console.log('OK pro replica passed');
  } catch (err) {
    await shot(page, 'pro-zz-failure').catch(() => undefined);
    console.error('FAIL', err);
    console.error('url', page.url());
    console.error(
      'body',
      (await page.locator('body').innerText().catch(() => '')).slice(0, 1500),
    );
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
