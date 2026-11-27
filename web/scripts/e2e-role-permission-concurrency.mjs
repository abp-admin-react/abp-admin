// From web/: node scripts/e2e-role-permission-concurrency.mjs
// ROLE_TEST_ORIGIN and ROLE_TEST_DB_CONTAINER select the local test stack. A non-default
// seed account can be supplied via ROLE_TEST_ADMIN_USER and ROLE_TEST_ADMIN_PASSWORD.
import assert from 'node:assert/strict';
import { execFile, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createInterface } from 'node:readline';
import { setTimeout as delay } from 'node:timers/promises';
import { promisify } from 'node:util';
import { chromium } from 'playwright';

const origin = process.env.ROLE_TEST_ORIGIN ?? 'http://localhost:18126';
const dbContainer = process.env.ROLE_TEST_DB_CONTAINER ?? 'abp-role-qa-db-1';
const adminLogin = process.env.ROLE_TEST_ADMIN_USER ?? 'admin';
const adminPassword = process.env.ROLE_TEST_ADMIN_PASSWORD ?? '1q2w3E*';
const permission = 'AbpIdentity.Users';
const rolesUrl = '/api/identity/roles';
const usersUrl = '/api/identity/users';
const suffix = randomUUID().slice(0, 8);
const run = promisify(execFile);
const psql =
  'exec psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" "$@"';

async function sql(statement) {
  const { stdout } = await run('docker', [
    'exec',
    dbContainer,
    'sh',
    '-c',
    psql,
    'psql',
    '-c',
    statement,
  ]);
  return stdout.trim();
}

async function login(browser, name, password, canManage = true) {
  const context = await browser.newContext({ baseURL: origin });
  const page = await context.newPage();
  await page.goto('/user/login');
  await page
    .locator('input[name$="UserNameOrEmailAddress"]')
    .fill(name, { timeout: 60_000 });
  await page.locator('input[type=password]').fill(password);
  await page.locator('form button[type=submit]').first().click();
  await page.waitForFunction(
    () =>
      Object.keys(localStorage).some(
        (key) =>
          key.includes('oidc.user:') &&
          JSON.parse(localStorage[key]).access_token,
      ) ||
      Boolean(document.querySelector('.alert-danger')?.textContent?.trim()),
    undefined,
    { timeout: 60_000 },
  );
  // The OIDC callback immediately navigates after storing the account. Read via the
  // browser context so a redirect cannot destroy a page.evaluate() execution context.
  const storage = await context.storageState();
  const account = storage.origins
    .find((entry) => entry.origin === new URL(origin).origin)
    ?.localStorage.find((entry) => entry.name.includes('oidc.user:'));
  if (!account) {
    const message = await page.locator('.alert-danger').first().innerText();
    throw new Error(`Test account login failed: ${message.trim()}`);
  }
  const token = JSON.parse(account.value).access_token;
  const api = async (method, path, data) => {
    const cookies = await context.cookies(origin);
    const xsrf = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN');
    const response = await context.request.fetch(path, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        ...(xsrf && {
          RequestVerificationToken: decodeURIComponent(xsrf.value),
        }),
      },
      data,
      timeout: 60_000,
    });
    const text = await response.text();
    const body =
      text && response.headers()['content-type']?.includes('json')
        ? JSON.parse(text)
        : null;
    return { status: response.status(), body };
  };
  const session = success(
    await api('GET', '/api/abp/application-configuration'),
  );
  assert.equal(session.currentUser.userName, name);
  assert.equal(
    Boolean(
      session.auth.grantedPolicies['AbpIdentity.Roles.ManagePermissions'],
    ),
    canManage,
  );
  assert.equal(
    Boolean(session.auth.grantedPolicies['AbpIdentity.Roles.Update']),
    canManage,
  );
  return { api, userId: session.currentUser.id };
}

function success({ status, body }) {
  assert.ok(
    status >= 200 && status < 300,
    `HTTP ${status}: ${body?.error?.message ?? '无响应正文'}`,
  );
  return body;
}

// Keep one real row lock open. pg_blocking_pids follows both direct lock holders and
// earlier waiters, so the test only releases the barrier after *both* HTTP transactions wait.
async function lockRole(id) {
  assert.match(id, /^[0-9a-f-]{36}$/i);
  const child = spawn(
    'docker',
    ['exec', '-i', dbContainer, 'sh', '-c', psql, 'psql'],
    {
      stdio: ['pipe', 'pipe', 'inherit'],
    },
  );
  const exit = new Promise((resolve) => child.on('exit', resolve));
  const lines = createInterface({ input: child.stdout })[
    Symbol.asyncIterator
  ]();
  child.stdin.write(
    `BEGIN; SET LOCAL idle_in_transaction_session_timeout = '40s'; SELECT pg_backend_pid() FROM "AbpRoles" WHERE "Id" = '${id}' FOR UPDATE;\n`,
  );
  const { value } = await lines.next();
  const pid = Number(value);
  assert.ok(Number.isInteger(pid) && pid > 0, '无法建立角色行锁');
  return {
    async waitForRequests(count) {
      const query = `WITH RECURSIVE blocked(pid) AS (
        SELECT ${pid}::int
        UNION
        SELECT a.pid FROM pg_stat_activity a JOIN blocked b ON b.pid = ANY(pg_blocking_pids(a.pid))
      ) SELECT count(*) - 1 FROM blocked`;
      const deadline = Date.now() + 12_000;
      do {
        if (Number(await sql(query)) >= count) return;
        await delay(75);
      } while (Date.now() < deadline);
      throw new Error(`未观察到 ${count} 个请求等待同一角色行锁`);
    },
    async release() {
      child.stdin.end('ROLLBACK;\n');
      assert.equal(await exit, 0, '锁屏障未正常释放');
    },
  };
}

async function runScenario(order, renamer, grantor, roleIds) {
  const name = `qa-role-${suffix}-${order}`;
  const newName = `${name}-renamed`;
  const create = async (roleName) => {
    const created = success(
      await renamer.api('POST', rolesUrl, { name: roleName }),
    );
    roleIds.push(created.id);
    return created;
  };
  const role = await create(name);
  const url = `${rolesUrl}/${role.id}`;
  const editUrl = `${url}/permissions`;
  const write = () =>
    grantor.api('PUT', editUrl, {
      expectedRoleName: name,
      permissions: [{ name: permission, isGranted: true }],
    });
  const rename = () =>
    renamer.api('PUT', url, {
      name: newName,
      isDefault: role.isDefault,
      isPublic: role.isPublic,
      concurrencyStamp: role.concurrencyStamp,
    });
  const read = async (id, expectedName) => {
    const list = success(
      await grantor.api(
        'GET',
        `${rolesUrl}/${id}/permissions?expectedRoleName=${encodeURIComponent(expectedName)}`,
      ),
    );
    const item = list.groups
      .flatMap((group) => group.permissions)
      .find((p) => p.name === permission);
    assert.ok(item, '测试权限应对授权管理员可见');
    return item.isGranted;
  };
  assert.equal(await read(role.id, name), false);
  const lock = await lockRole(role.id);
  let first;
  let second;
  try {
    first = order === 'rename' ? rename() : write();
    await lock.waitForRequests(1);
    second = order === 'rename' ? write() : rename();
    await lock.waitForRequests(2);
  } finally {
    await lock.release();
    // A failing overlap assertion can leave the first HTTP request queued. Drain it
    // after releasing the barrier and before the outer fixture cleanup deletes the role.
    await Promise.allSettled([first, second]);
  }
  const results = await Promise.all([first, second]);
  const [renameResult, grantResult] =
    order === 'rename' ? results : results.reverse();
  success(renameResult);
  if (order === 'rename') {
    assert.equal(grantResult.status, 409);
    assert.equal(
      grantResult.body?.error?.code,
      'AbpAdmin:RolePermissionTargetChanged',
    );
  } else {
    assert.equal(grantResult.status, 204);
  }
  const replacement = await create(name);
  assert.notEqual(replacement.id, role.id);
  assert.equal(success(await renamer.api('GET', url)).name, newName);
  assert.equal(await read(role.id, newName), order === 'grant');
  const stale = await write();
  assert.equal(stale.status, 409);
  assert.equal(stale.body?.error?.code, 'AbpAdmin:RolePermissionTargetChanged');
  assert.equal(await read(replacement.id, name), false);
  const legacy = await grantor.api(
    'PUT',
    `/api/permission-management/permissions?providerName=R&providerKey=${encodeURIComponent(name)}`,
    { permissions: [{ name: permission, isGranted: true }] },
  );
  assert.equal(legacy.status, 400);
  assert.equal(legacy.body?.error?.code, 'AbpAdmin:RolePermissionIdRequired');
  // Verify the stored provider key for this permission after the replacement role reuses the old name.
  const stored =
    await sql(`SELECT "ProviderKey" FROM "AbpPermissionGrants" WHERE "ProviderName"='R'
    AND "TenantId" IS NULL AND "Name"='${permission}' AND "ProviderKey" IN ('${name}', '${newName}')`);
  assert.equal(stored, order === 'grant' ? newName : '');
  console.log(
    `${order} first: 2 blocked HTTP transactions; rename ${renameResult.status}, grant ${grantResult.status}, stale replay ${stale.status}, legacy role write ${legacy.status}; replacement has no ${permission} grant; PostgreSQL verified.`,
  );
}

const browser = await chromium.launch({
  channel: process.env.PLAYWRIGHT_CHANNEL,
});
const roleIds = [];
let renamer;
let grantorId;
try {
  renamer = await login(browser, adminLogin, adminPassword);
  const name = `qarole${suffix}`;
  const password = `Q1!a${randomUUID()}`;
  grantorId = success(
    await renamer.api('POST', usersUrl, {
      userName: name,
      email: `${name}@example.test`,
      password,
      isActive: true,
      roleNames: ['admin'],
    }),
  ).id;
  const grantor = await login(browser, name, password);
  assert.equal(grantor.userId, grantorId);
  assert.notEqual(renamer.userId, grantor.userId);
  await runScenario('rename', renamer, grantor, roleIds);
  await runScenario('grant', renamer, grantor, roleIds);
  const clientUrl =
    '/api/permission-management/permissions?providerName=C&providerKey=AbpAdmin_App';
  assert.ok(Array.isArray(success(await renamer.api('GET', clientUrl)).groups));
  success(await renamer.api('PUT', clientUrl, { permissions: [] }));
  success(
    await renamer.api('PUT', `${usersUrl}/${grantorId}/roles`, {
      roleNames: [],
    }),
  );
  const reader = await login(browser, name, password, false);
  const missingUrl = `${rolesUrl}/${randomUUID()}/permissions`;
  const missingGrant = { expectedRoleName: 'missing', permissions: [] };
  for (const result of [
    await renamer.api('GET', `${missingUrl}?expectedRoleName=missing`),
    await renamer.api('PUT', missingUrl, missingGrant),
  ]) {
    assert.equal(result.status, 404);
  }
  console.log('Unknown role IDs returned 404 on GET and PUT.');
  const userUrl = `/api/permission-management/permissions?providerName=U&providerKey=${grantorId}`;
  const readUserGrant = async () => {
    const result = success(await renamer.api('GET', userUrl));
    return result.groups
      .flatMap((group) => group.permissions)
      .find((item) => item.name === permission).isGranted;
  };
  assert.equal(await readUserGrant(), false);
  for (const value of [true, false]) {
    success(
      await renamer.api('PUT', userUrl, {
        permissions: [{ name: permission, isGranted: value }],
      }),
    );
    assert.equal(await readUserGrant(), value);
  }
  console.log(
    'Existing providers verified: U direct grant false → true → false, C read and empty update succeeded.',
  );
  const target = success(await renamer.api('GET', `${rolesUrl}/${roleIds[0]}`));
  const path = `${rolesUrl}/${target.id}/permissions`;
  const query = `?expectedRoleName=${encodeURIComponent(target.name)}`;
  const data = {
    expectedRoleName: target.name,
    permissions: [{ name: permission, isGranted: true }],
  };
  assert.equal((await reader.api('GET', path + query)).status, 403);
  assert.equal((await reader.api('PUT', path, data)).status, 403);
  const anonymous = await browser.newContext({ baseURL: origin });
  assert.equal((await anonymous.request.get(path + query)).status(), 401);
  assert.equal((await anonymous.request.put(path, { data })).status(), 401);
  console.log(
    'Scoped GET/PUT denied for both anonymous (401) and authenticated reader (403).',
  );
} finally {
  try {
    const removals = await Promise.allSettled([
      ...roleIds.map((id) =>
        renamer.api('DELETE', `${rolesUrl}/${id}`).then(success),
      ),
      ...(grantorId
        ? [renamer.api('DELETE', `${usersUrl}/${grantorId}`).then(success)]
        : []),
    ]);
    for (const result of removals) {
      if (result.status === 'rejected') {
        console.error('Fixture cleanup failed:', result.reason.message);
        process.exitCode = 1;
      }
    }
  } finally {
    await browser.close();
  }
}
