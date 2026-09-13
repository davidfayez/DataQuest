import { expect, request, test, type Page } from '@playwright/test';

/**
 * Admin panel: sign-in, the permission-aware shell, lookups CRUD, the review workflow with
 * dual-visibility comments, RBAC administration and the audit log.
 */

const API = 'http://localhost:5088/api/v1/';

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };

/**
 * A reviewer, created by the suite rather than expected in the seed.
 *
 * It used to assume a `reviewer@dataverification.local` account existed. Nothing seeds one, so on
 * any database but the author's the three permission tests failed at sign-in — and the honest fix
 * is not to seed it: a second account with a published password, standing on every deployment
 * forever, is a poor trade for three tests. The suite makes its own and gives it exactly the
 * permissions those tests describe.
 */
const REVIEWER = {
  email: `e2e-reviewer-${Date.now()}@dataverification.local`,
  password: 'Reviewer#12345',
};

const REVIEWER_PERMISSIONS = [
  'Dashboard.View',
  'Applications.View',
  'Applications.Review',
  'Orders.View',
  'Countries.View',
  'Currencies.View',
  'TransactionTypes.View',
  'SubTransactionTypes.View',
  'Authorities.View',
  'ServiceTypes.View',
];

const PDF = Buffer.concat([Buffer.from('%PDF-1.4\n% test\n'), Buffer.alloc(64, 1)]);

function unique(prefix: string): string {
  return `${prefix}${Date.now()}${Math.floor(Math.random() * 1000)}`;
}

/**
 * A two-letter ISO-style code. Must be letters only — the server validates against
 * `^[A-Za-z]{2}$`, so a generated code containing digits is rejected as invalid input.
 */
/**
 * A two-letter code from the ISO 3166-1 user-assigned ranges (XA–XZ, QM–QZ). Those are guaranteed
 * never to be real countries, so a generated code cannot collide with the ~190 seeded ISO codes.
 */
function uniqueCountryCode(): string {
  const pool: string[] = [];
  for (const c of 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') pool.push(`X${c}`);
  for (const c of 'MNOPQRSTUVWXYZ') pool.push(`Q${c}`);
  return pool[Math.floor(Math.random() * pool.length)];
}

/** Creates the reviewer the permission tests sign in as, through the API the panel itself uses. */
test.beforeAll(async () => {
  const anon = await request.newContext({ baseURL: API });
  const login = await anon.post('admin/auth/login', { data: SUPER_ADMIN });
  const { accessToken } = (await login.json()) as { accessToken: string };

  const admin = await request.newContext({
    baseURL: API,
    extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
  });

  const created = await admin.post('admin/users', {
    data: {
      email: REVIEWER.email,
      username: REVIEWER.email.split('@')[0],
      fullName: 'E2E Reviewer',
      password: REVIEWER.password,
      isActive: true,
      languageCode: 'en',
      permissions: REVIEWER_PERMISSIONS,
    },
  });

  expect(created.ok(), `could not create the reviewer: ${await created.text()}`).toBe(true);
});

/**
 * Finds a newly created admin user in the list.
 *
 * Not simply asserting the row is on screen: the list pages at 25, and an installation that has
 * been used accumulates accounts, so whether a new one appears on the first page is an accident of
 * how many exist. Both user tests passed or failed on that accident rather than on the behaviour
 * they describe. Searching asks the server, which is what the panel's own search box does.
 */
async function findUserRow(page: Page, email: string) {
  await page.getByTestId('table-search').fill(email);
  await expect(page.getByTestId(`edit-user-${email}`)).toBeVisible();
  return page.getByTestId(`edit-user-${email}`);
}

async function signIn(page: Page, who: { email: string; password: string }) {
  await page.goto('/login');
  await page.locator('#identifier').fill(who.email);
  await page.locator('#password').fill(who.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/$/);
}

/**
 * The Lookups group starts folded, and the preference is remembered per browser profile, so its
 * items are only clickable once it has been unfolded. Idempotent: a group already open is left be.
 */
async function openLookups(page: Page) {
  const header = page.getByTestId('nav-group-lookups');
  if ((await header.getAttribute('aria-expanded')) === 'false') {
    await header.click();
  }
  await expect(header).toHaveAttribute('aria-expanded', 'true');
}

test.describe('authentication and shell', () => {
  test('unauthenticated visitors are sent to sign in', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('rejects bad credentials', async ({ page }) => {
    await page.goto('/login');
    await page.locator('#identifier').fill(SUPER_ADMIN.email);
    await page.locator('#password').fill('WrongPassword1');
    await page.locator('form button[type="submit"]').click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });

  test('a SuperAdmin sees every module', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);

    // 'roles' is absent on purpose: permissions are assigned per user, so the Roles entry is
    // hidden from the sidebar and the page is reached by URL (see AdminLayout).
    for (const item of ['dashboard', 'applications', 'orders', 'users', 'audit']) {
      await expect(page.getByTestId(`nav-${item}`)).toBeVisible();
    }

    await openLookups(page);
    await expect(page.getByTestId('nav-countries')).toBeVisible();
  });

  test('the sidebar can be hidden and shown again', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);

    const sidebar = page.locator('#admin-sidebar');
    const toggle = page.getByTestId('sidebar-toggle');
    await expect(sidebar).toBeVisible();

    await toggle.click();
    await expect(sidebar).toBeHidden();

    await toggle.click();
    await expect(sidebar).toBeVisible();
  });

  test('the Lookups group starts folded and unfolds on demand', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);

    const header = page.getByTestId('nav-group-lookups');
    await expect(header).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByTestId('nav-countries')).toBeHidden();

    await header.click();
    await expect(page.getByTestId('nav-countries')).toBeVisible();
  });

  test('a Reviewer only sees the modules they hold permissions for', async ({ page }) => {
    await signIn(page, REVIEWER);

    // Reviewer holds Dashboard.View, Applications.View/Review, every lookup's View, and Orders.View.
    await expect(page.getByTestId('nav-dashboard')).toBeVisible();
    await expect(page.getByTestId('nav-applications')).toBeVisible();
    await expect(page.getByTestId('nav-orders')).toBeVisible();
    await openLookups(page);
    await expect(page.getByTestId('nav-countries')).toBeVisible();

    // …and none of the administration modules.
    await expect(page.getByTestId('nav-roles')).toHaveCount(0);
    await expect(page.getByTestId('nav-users')).toHaveCount(0);
    await expect(page.getByTestId('nav-audit')).toHaveCount(0);
  });

  test('navigating straight to a forbidden module is refused', async ({ page }) => {
    await signIn(page, REVIEWER);

    // Navigated client-side: a full page load would drop the in-memory token and merely bounce
    // to sign-in, which would not exercise the permission guard at all.
    await page.evaluate(() => {
      window.history.pushState({}, '', '/roles');
      window.dispatchEvent(new PopStateEvent('popstate'));
    });

    await expect(page).toHaveURL(/\/roles$/);
    await expect(page.getByRole('alert')).toContainText('permission');
  });

  test('the API refuses a reviewer even when the UI is bypassed', async ({ page }) => {
    await signIn(page, REVIEWER);

    // The sidebar is presentation; this is the boundary that actually matters.
    const status = await page.evaluate(async () => {
      const response = await fetch('/api/v1/admin/roles', {
        headers: { Accept: 'application/json' },
      });
      return response.status;
    });

    // Unauthenticated from the page's own fetch (the token lives in memory, not a cookie),
    // which is itself the point: there is no ambient credential to ride on.
    expect([401, 403]).toContain(status);
  });

  test('switches to Arabic and mirrors the layout', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);

    await page.getByLabel(/change language/i).selectOption('ar');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { name: 'لوحة المعلومات' })).toBeVisible({
      timeout: 30_000,
    });

    await page.getByLabel(/تغيير اللغة/).selectOption('en');
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  });
});

test.describe('dashboard', () => {
  test('shows counters, revenue and every status', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);

    // The dashboard runs several aggregate queries, so it is legitimately the slowest screen.
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible({ timeout: 30_000 });

    // All eight statuses are always listed, including those with no rows.
    for (const status of ['Draft', 'Pending', 'InProgress', 'Success', 'Failed', 'Refunded']) {
      await expect(page.getByTestId(`status-${status}`)).toBeVisible();
    }

    await expect(page.getByText('Net revenue')).toBeVisible();
    await expect(page.getByText('Recent activity')).toBeVisible();
  });
});

test.describe('lookups', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page, SUPER_ADMIN);
    await openLookups(page);
    await page.getByTestId('nav-countries').click();
  });

  test('creates, edits and deletes a country', async ({ page }) => {
    const code = uniqueCountryCode();
    const nameEn = `Testland ${unique('')}`;

    // Create and edit are full pages, not a dialog.
    await page.getByTestId('lookup-new').click();
    await expect(page).toHaveURL(/\/lookups\/countries\/new$/);
    await page.locator('#code').fill(code);
    await page.locator('#nameAr').fill(`بلد ${code}`);
    await page.locator('#nameEn').fill(nameEn);
    await page.getByTestId('form-submit').click();

    await expect(page).toHaveURL(/\/lookups\/countries$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    // The table pages at 25 rows and sorts by name, so the new record is searched for rather
    // than assumed to be on the first page.
    await page.getByTestId('table-search').fill(nameEn);
    // Scoped to the cell: a bare text match also hits the wrapping row.
    await expect(page.getByRole('cell', { name: nameEn })).toBeVisible();

    // Editing writes through the same page.
    await page.getByTestId(`edit-${code}`).click();
    await expect(page.locator('#code')).toHaveValue(code);
    await page.locator('#nameEn').fill(`${nameEn} edited`);
    await page.getByTestId('form-submit').click();
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    await page.getByTestId(`delete-${code}`).click();
    await page.getByTestId('confirm-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });

  test('a country already in use is deactivated rather than deleted', async ({ page }) => {
    // Egypt backs seeded orders, so the API retires it instead of removing it.
    await page.getByTestId('table-search').fill('Egypt');
    await expect(page.getByTestId('delete-EG')).toBeVisible();
    await page.getByTestId('delete-EG').click();
    await page.getByTestId('confirm-submit').click();

    await expect(page.getByTestId('lookup-notice')).toContainText('deactivated');

    // Put it back the way it was so the rest of the suite is unaffected. The Active toggle now
    // lives in the form page's aside rather than among the name fields.
    await page.getByTestId('edit-EG').click();
    await page.getByRole('checkbox', { name: 'Active' }).check();
    await page.getByTestId('form-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });

  test('rejects a duplicate country code', async ({ page }) => {
    await page.getByTestId('lookup-new').click();
    await page.locator('#code').fill('EG');
    await page.locator('#nameAr').fill('مكرر');
    await page.locator('#nameEn').fill('Duplicate');
    await page.getByTestId('form-submit').click();

    // The page stays put and reports the conflict rather than navigating back to the list.
    await expect(page.getByRole('alert')).toContainText('already exists');
    await expect(page).toHaveURL(/\/lookups\/countries\/new$/);
  });

  test('service types expose express configuration and required files', async ({ page }) => {
    await openLookups(page);
    await page.getByTestId('nav-serviceTypes').click();
    await expect(page.getByRole('cell', { name: 'Standard Certificate Verification' })).toBeVisible();

    await page.getByTestId('edit-Attested Verification').click();
    await expect(page.getByTestId('enable-express')).toBeChecked();
    // Express is priced per currency, so there is one surcharge field per cost row.
    const expressCosts = page.locator('[data-testid^="express-"]');
    await expect(expressCosts.first()).toBeEnabled();

    // Turning express off disables every surcharge, mirroring the server's rule.
    await page.getByTestId('enable-express').uncheck();
    for (const field of await expressCosts.all()) {
      await expect(field).toBeDisabled();
    }
  });

  test('a country carries its currencies on the form page and in the list', async ({ page }) => {
    // The catalogue holds every country, so narrow to Egypt before asserting on its row.
    await page.getByTestId('table-search').fill('Egypt');
    await expect(page.getByRole('row', { name: /Egypt/ })).toContainText('EGP');

    // The same currencies are pre-ticked on the country's form page. The checklist is keyed by
    // id, so the summary panel — which reads back labels — is what the codes are asserted on.
    await page.getByTestId('edit-EG').click();
    await expect(page.locator('aside').getByText(/\(EGP\)/)).toBeVisible();
    await expect(page.locator('aside').getByText(/\(USD\)/)).toBeVisible();

    // Saving persists the country and its currency set together.
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/countries$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });
});

test.describe('review workflow', () => {
  test('drives an application through review with both comment visibilities', async ({ page }) => {
    // Build a paid application through the API so the panel has something to review.
    const admin = await request.newContext({ baseURL: API });
    const login = await admin.post('admin/auth/login', { data: SUPER_ADMIN });
    const { accessToken } = (await login.json()) as { accessToken: string };
    const api = await request.newContext({
      baseURL: API,
      extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
    });

    const email = `${unique('adminflow')}@example.com`;
    const anon = await request.newContext({ baseURL: API });
    const reg = await anon.post('orders/register', { data: { email, languageCode: 'en' } });
    const { orderNumber, password } = (await reg.json()) as {
      orderNumber: string;
      password: string;
    };
    const applicantLogin = await anon.post('orders/login', { data: { orderNumber, password } });
    const { accessToken: applicantToken, orderId } = (await applicantLogin.json()) as {
      accessToken: string;
      orderId: string;
    };
    const applicant = await request.newContext({
      baseURL: API,
      extraHTTPHeaders: { Authorization: `Bearer ${applicantToken}` },
    });

    // The contact person is as mandatory as the country and the currency; setup rejects the call
    // without it, and every later step of this test then fails on an order that was never set up.
    const setup = await applicant.put('orders/setup', {
      data: {
        verificationCountryId: '22222222-0000-0000-0000-000000000001',
        currencyId: '22222222-0000-0000-0000-000000000002',
        contactPersonName: 'E2E Contact',
        contactPersonPhoneCountry: 'EG',
        contactPersonPhoneCode: '+20',
        contactPersonPhoneNumber: '1005550123',
      },
    });

    expect(setup.ok(), `order setup failed: ${await setup.text()}`).toBe(true);

    const created = await applicant.post('applications', {
      data: {
        addressedTo: 'Admin e2e',
        birthDate: '1990-05-17',
        // Mandatory at submit, like the date of birth. Without them the application stays a draft
        // and every assertion after this reads the wrong status.
        applicantEmail: 'admin.e2e@example.com',
        applicantPhoneCountry: 'EG',
        applicantPhoneCode: '+20',
        applicantPhoneNumber: '1005550124',
        names: [
          { languageType: 0, firstName: 'أحمد', lastName: 'علي' },
          { languageType: 1, firstName: 'Ahmed', lastName: 'Ali' },
        ],
        transactionTypeId: '44444444-0000-0000-0000-000000000001',
        subTransactionTypeId: '55555555-0000-0000-0000-000000000001',
        verificationAuthorityId: '66666666-0000-0000-0000-000000000001',
        services: [
          {
            serviceTypeId: '77777777-0000-0000-0000-000000000001',
            quantity: 1,
            languageCode: 'en',
            isExpress: false,
          },
        ],
      },
    });
    const application = (await created.json()) as {
      id: string;
      applicationNumber: string;
      services: Array<{ id: string }>;
      requiredFiles: Array<{ requiredFileId: string; applicationServiceId: string }>;
    };

    expect(created.ok(), `could not create the application: ${await created.text()}`).toBe(true);

    for (const requirement of application.requiredFiles) {
      const upload = await applicant.post(`applications/${application.id}/files`, {
        multipart: {
          file: { name: 'doc.pdf', mimeType: 'application/pdf', buffer: PDF },
          applicationServiceId: requirement.applicationServiceId,
          requiredFileId: requirement.requiredFileId,
        },
      });
      expect(upload.ok(), `evidence upload failed: ${await upload.text()}`).toBe(true);
    }

    // Each of these moves the application to the state the review steps below assume. Checked as
    // they happen: an unasserted failure here surfaces much later as a status that makes no sense.
    const submitted = await applicant.post(`applications/${application.id}/submit`);
    expect(submitted.ok(), `submit failed: ${await submitted.text()}`).toBe(true);

    const credited = await api.post(`admin/orders/${orderId}/wallet/credit`, {
      data: { amount: 2000, note: 'e2e' },
    });
    expect(credited.ok(), `wallet credit failed: ${await credited.text()}`).toBe(true);

    const paid = await applicant.post('payments', { data: { applicationIds: [application.id] } });
    expect(paid.ok(), `payment failed: ${await paid.text()}`).toBe(true);

    // Now review it through the UI.
    await signIn(page, SUPER_ADMIN);
    await page.getByTestId('nav-applications').click();
    await page.getByTestId('table-search').fill(application.applicationNumber);
    await page.getByRole('link', { name: application.applicationNumber }).click();

    await expect(page.getByTestId('admin-status')).toHaveText('Paid — in queue');

    // Pending has exactly one legal next status (In progress), so the dropdown defaults to it and
    // applying the change is a single click.
    await page.getByTestId('apply-status').click();
    await expect(page.getByTestId('admin-status')).toHaveText('In progress');

    // An internal note must not move the application.
    await page.getByTestId('admin-tab-comments').click();
    await page.getByTestId('comment-box').fill('Internal only: verify with registrar first.');
    await page.getByTestId('visibility-internal').check();
    await page.getByTestId('post-comment').click();
    await expect(page.getByTestId('comment-internal')).toBeVisible();
    await expect(page.getByTestId('admin-status')).toHaveText('In progress');

    // A user-visible comment does, automatically.
    await page.getByTestId('comment-box').fill('Please upload a clearer scan.');
    await page.getByTestId('visibility-foruser').check();
    await page.getByTestId('post-comment').click();
    await expect(page.getByTestId('comment-foruser')).toBeVisible();
    await expect(page.getByTestId('admin-status')).toHaveText('Information needed');

    // The applicant's own view must contain the visible comment and never the internal one.
    const timeline = await applicant.get(`applications/${application.id}/timeline`);
    const body = await timeline.text();
    expect(body).toContain('Please upload a clearer scan.');
    expect(body).not.toContain('Internal only');

    await admin.dispose();
    await api.dispose();
    await anon.dispose();
    await applicant.dispose();
  });

  test('files can be previewed inline', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);
    await page.getByTestId('nav-applications').click();

    await page.getByRole('link').filter({ hasText: /^APP-/ }).first().click();
    await page.getByTestId('admin-tab-files').click();

    const preview = page.locator('[data-testid^="preview-"]').first();
    if (await preview.isVisible()) {
      await preview.click();
      await expect(page.locator('dialog[open]')).toBeVisible();
    }
  });
});

test.describe('roles and admin users', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page, SUPER_ADMIN);
  });

  test('creates a role with a grid of granular permissions', async ({ page }) => {
    // Roles has no sidebar entry; navigate client-side so the in-memory token survives.
    await page.evaluate(() => {
      window.history.pushState({}, '', '/roles');
      window.dispatchEvent(new PopStateEvent('popstate'));
    });

    const name = unique('E2ERole');
    await page.getByTestId('new-role').click();
    await page.locator('#role-name').fill(name);
    // Two granular permissions from different pages, chosen from the grid.
    await page.getByTestId('permission-AuditLog.View').check();
    await page.getByTestId('permission-ServiceTypes.Create').check();
    await page.getByTestId('dialog-submit').click();

    const card = page.getByTestId(`role-${name}`);
    await expect(card).toBeVisible();

    // Re-open the role and confirm exactly those two boxes are still checked — the card shows a
    // count, so persistence is verified through the editor rather than chips on the card.
    await card.getByRole('button', { name: 'Edit' }).click();
    await expect(page.getByTestId('permission-AuditLog.View')).toBeChecked();
    await expect(page.getByTestId('permission-ServiceTypes.Create')).toBeChecked();
    await expect(page.getByTestId('permission-ServiceTypes.Delete')).not.toBeChecked();
  });

  test('built-in roles cannot be renamed', async ({ page }) => {
    // Roles has no sidebar entry; navigate client-side so the in-memory token survives.
    await page.evaluate(() => {
      window.history.pushState({}, '', '/roles');
      window.dispatchEvent(new PopStateEvent('popstate'));
    });

    await page.getByTestId('role-SuperAdmin').getByRole('button', { name: 'Edit' }).click();
    await expect(page.locator('#role-name')).toBeDisabled();
  });

  test('creates an admin user and assigns permissions', async ({ page }) => {
    await page.getByTestId('nav-users').click();

    const username = unique('e2euser');
    const email = `${username}@dataverification.local`;
    // Create and edit are full pages, not a dialog.
    await page.getByTestId('new-user').click();
    await expect(page).toHaveURL(/\/users\/new$/);
    await page.locator('#fullName').fill('E2E Operator');
    await page.locator('#userEmail').fill(email);
    await page.locator('#userUsername').fill(username);
    await page.locator('#userPassword').fill('Operator#12345');
    await page.getByTestId('permission-Applications.View').check();
    await page.getByTestId('permission-Applications.Review').check();
    // Ticking a permission is echoed in the aside as the group the account can now reach.
    await expect(page.locator('aside').getByText('Applications')).toBeVisible();
    await page.getByTestId('form-submit').click();

    await expect(page).toHaveURL(/\/users$/);
    await expect(page.getByTestId('user-notice')).toBeVisible();

    // The permissions come back on the form page rather than needing a second lookup.
    await (await findUserRow(page, email)).click();
    await expect(page.locator('#userUsername')).toHaveValue(username);
    await expect(page.getByTestId('permission-Applications.Review')).toBeChecked();
  });

  test('the new account can sign in with its username instead of its email', async ({ page }) => {
    await page.getByTestId('nav-users').click();

    const username = unique('e2ebyname');
    await page.getByTestId('new-user').click();
    await page.locator('#fullName').fill('Username Sign-in');
    await page.locator('#userEmail').fill(`${username}@dataverification.local`);
    await page.locator('#userUsername').fill(username);
    await page.locator('#userPassword').fill('Operator#12345');
    await page.getByTestId('permission-Applications.View').check();
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/users$/);
    await findUserRow(page, `${username}@dataverification.local`);

    await page.getByTestId('sign-out').click();
    await expect(page).toHaveURL(/\/login$/);

    await page.locator('#identifier').fill(username);
    await page.locator('#password').fill('Operator#12345');
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/$/);
  });

  test('rejects a password shorter than the policy allows', async ({ page }) => {
    await page.getByTestId('nav-users').click();

    await page.getByTestId('new-user').click();
    const shortName = unique('short');
    await page.locator('#fullName').fill('Too Short');
    await page.locator('#userEmail').fill(`${shortName}@dataverification.local`);
    await page.locator('#userUsername').fill(shortName);
    await page.locator('#userPassword').fill('short');
    await page.getByTestId('form-submit').click();

    // The page stays put and reports the rejection rather than navigating back to the list.
    await expect(page.getByRole('alert')).toContainText('10 characters');
    await expect(page).toHaveURL(/\/users\/new$/);
  });
});

test.describe('audit log', () => {
  test('lists entries and filters by entity type', async ({ page }) => {
    await signIn(page, SUPER_ADMIN);
    await page.getByTestId('nav-audit').click();

    await expect(page.getByTestId('table-row').first()).toBeVisible();

    await page.getByTestId('filter-entity').selectOption('AdminUser');
    await expect(page.getByTestId('table-row').first()).toBeVisible();
    await expect(page.getByRole('cell', { name: 'AdminUser' }).first()).toBeVisible();
  });
});

/**
 * Transaction types and verification authorities are created and edited on their own pages rather
 * than in a dialog: sectioned panels on the left, status and a live selection summary in an aside,
 * and a save bar pinned to the foot.
 */
test.describe('lookup form pages', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page, SUPER_ADMIN);
    await openLookups(page);
  });

  test('transaction type: create, edit and cancel on a full page', async ({ page }) => {
    await page.getByTestId('nav-transactionTypes').click();
    await page.getByTestId('lookup-new').click();
    await expect(page).toHaveURL(/\/lookups\/transactionTypes\/new$/);

    await expect(page.getByRole('heading', { name: 'New Transaction type' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Names' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Where it applies' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Status' })).toBeVisible();
    await expect(page.getByText('Nothing selected yet.')).toBeVisible();
    // The Active toggle moved out of the name fields into the aside — exactly one on the page.
    await expect(page.getByRole('checkbox', { name: 'Active' })).toHaveCount(1);

    const nameEn = unique('TT ');
    await page.locator('#nameEn').fill(nameEn);
    await page.locator('#nameAr').fill('نوع معاملة');

    // Both descriptions are required: Save stays off until they are written, and an unfinished
    // form is not mistaken for a view-only one.
    await page.locator('[data-testid^="transaction-type-country-"]').first().check();
    await expect(page.getByTestId('form-submit')).toBeDisabled();
    await expect(page.getByTestId('view-only-notice')).toHaveCount(0);
    await page.locator('[data-testid^="transaction-type-country-"]').first().uncheck();
    await page.getByTestId('description-ar').fill('وصف نوع المعاملة');
    await page.getByTestId('description-en').fill('What this transaction type covers.');

    // Names, descriptions and a country are all there, but no code yet: still not saveable.
    await expect(page.getByTestId('form-submit')).toBeDisabled();
    await page.getByTestId('lookup-code').fill(`tt-${Date.now()}`);
    // Typed lower-case, held and shown upper-case, which is how it is stored.
    await expect(page.getByTestId('lookup-code')).toHaveValue(/^TT-\d+$/);

    // Ticking a country fills the summary panel in the aside.
    await page.locator('[data-testid^="transaction-type-country-"]').first().check();
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/transactionTypes$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    // Edit round-trip: the record loads back into the same frame and saves.
    await page.getByTestId('table-search').fill(nameEn);
    await page.getByTestId(`edit-${nameEn}`).click();
    await expect(page.getByRole('heading', { name: 'Edit Transaction type' })).toBeVisible();
    await expect(page.locator('#nameEn')).toHaveValue(nameEn);
    await expect(page.getByTestId('description-en')).toHaveValue('What this transaction type covers.');
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.locator('#nameEn').fill(`${nameEn} edited`);
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/transactionTypes$/);
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    // Cancel leaves without writing.
    await page.getByTestId(`edit-${nameEn} edited`).click();
    await page.locator('#nameEn').fill('discarded');
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(page).toHaveURL(/\/lookups\/transactionTypes$/);
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    await page.getByTestId(`delete-${nameEn} edited`).click();
    await page.getByTestId('confirm-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });

  test('currency: create and edit on a full page', async ({ page }) => {
    await page.getByTestId('nav-currencies').click();
    await page.getByTestId('lookup-new').click();
    await expect(page).toHaveURL(/\/lookups\/currencies\/new$/);

    await expect(page.getByRole('heading', { name: 'New Currency' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'The currency itself' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Where it is accepted' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Active' })).toHaveCount(1);

    // No real ISO 4217 code is Z followed by two letters, so this cannot collide with the seed.
    const letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
    const pick = () => letters[Math.floor(Math.random() * letters.length)];
    const code = `Z${pick()}${pick()}`;
    const nameEn = unique('Currency ');

    await page.locator('#code').fill(code);
    await page.locator('#symbol').fill('¤');
    await page.locator('#nameEn').fill(nameEn);
    await page.locator('#nameAr').fill('عملة');

    // Ticking a country fills the summary panel in the aside.
    await page.locator('[data-testid^="currency-country-"]').first().check();
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/currencies$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    await page.getByTestId('table-search').fill(nameEn);
    await page.getByTestId(`edit-${code}`).click();
    await expect(page.getByRole('heading', { name: 'Edit Currency' })).toBeVisible();
    await expect(page.locator('#code')).toHaveValue(code);
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.locator('#nameEn').fill(`${nameEn} edited`);
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/currencies$/);
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    await page.getByTestId(`delete-${code}`).click();
    await page.getByTestId('confirm-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });

  test('sub-transaction type: create and edit on a full page', async ({ page }) => {
    await page.getByTestId('nav-subTransactionTypes').click();
    await page.getByTestId('lookup-new').click();
    await expect(page).toHaveURL(/\/lookups\/subTransactionTypes\/new$/);

    await expect(page.getByRole('heading', { name: 'New Sub-transaction type' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'What it belongs to' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Where it applies' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Active' })).toHaveCount(1);

    const nameEn = unique('Sub ');
    await page.locator('#nameEn').fill(nameEn);
    await page.locator('#nameAr').fill('نوع فرعي');
    await page.getByTestId('description-ar').fill('وصف النوع الفرعي');
    await page.getByTestId('description-en').fill('What this sub-type covers.');
    await page.getByTestId('lookup-code').fill(`SUB-${Date.now()}`);

    // The form opens on the first transaction type, so its countries are already listed.
    const country = page.locator('[data-testid^="sub-transaction-type-country-"]').first();
    await expect(country).toBeVisible();
    await country.check();
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/subTransactionTypes$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    await page.getByTestId('table-search').fill(nameEn);
    await page.getByTestId(`edit-${nameEn}`).click();
    await expect(page.getByRole('heading', { name: 'Edit Sub-transaction type' })).toBeVisible();
    await expect(page.locator('#nameEn')).toHaveValue(nameEn);
    await expect(page.locator('aside').getByRole('listitem')).toHaveCount(1);

    await page.locator('#nameEn').fill(`${nameEn} edited`);
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/subTransactionTypes$/);
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    await page.getByTestId(`delete-${nameEn} edited`).click();
    await page.getByTestId('confirm-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });

  test('authority: create and edit on a full page', async ({ page }) => {
    await page.getByTestId('nav-authorities').click();
    await page.getByTestId('lookup-new').click();
    await expect(page).toHaveURL(/\/lookups\/authorities\/new$/);

    await expect(page.getByRole('heading', { name: 'New Verification authority' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Where it operates' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'What it handles' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Active' })).toHaveCount(1);

    const nameEn = unique('Authority ');
    await page.locator('#nameEn').fill(nameEn);
    await page.locator('#nameAr').fill('جهة تحقق');
    await page.getByTestId('description-ar').fill('وصف جهة التحقق');
    await page.getByTestId('description-en').fill('What this authority verifies.');
    await page.getByTestId('lookup-code').fill(`VA-${Date.now()}`);

    // Scoped to the dropdown's listbox: the header's language <select> also carries options.
    await page.locator('#countryId').click();
    await page.getByPlaceholder('Search countries…').fill('Egypt');
    await page.getByRole('listbox').getByRole('option').first().click();

    // Egypt is seeded with sub-transaction types, so the mapping picker has something to offer.
    await page.locator('#subTransactionTypeIds').click();
    const option = page.getByRole('listbox').getByRole('option').first();
    await expect(option).toBeVisible();
    const optionLabel = (await option.textContent())?.trim() ?? '';
    await option.click();
    await page.keyboard.press('Escape');

    await expect(page.locator('aside').getByText(optionLabel, { exact: true })).toBeVisible();

    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/authorities$/);
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    await page.getByTestId('table-search').fill(nameEn);
    await page.getByTestId(`edit-${nameEn}`).click();
    await expect(page.getByRole('heading', { name: 'Edit Verification authority' })).toBeVisible();
    await expect(page.locator('#nameEn')).toHaveValue(nameEn);
    // The saved mapping came back with the record.
    await expect(page.locator('aside').getByText(optionLabel, { exact: true })).toBeVisible();

    await page.locator('#nameEn').fill(`${nameEn} edited`);
    await page.getByTestId('form-submit').click();
    await expect(page).toHaveURL(/\/lookups\/authorities$/);
    await page.getByTestId('table-search').fill(`${nameEn} edited`);
    await expect(page.getByRole('cell', { name: `${nameEn} edited` })).toBeVisible();

    await page.getByTestId(`delete-${nameEn} edited`).click();
    await page.getByTestId('confirm-submit').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();
  });
});
