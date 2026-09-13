import { expect, request, test, type APIRequestContext, type Page } from '@playwright/test';
import { choose, chooseByIndex, chooseLanguage } from './support/searchable-select';
import { fillContactPerson } from './support/contact-person';
import { fillApplicantContact } from './support/applicant-contact';

/**
 * Order dashboard: multi-application payment, refunds, the wallet ledger, and the review
 * conversation. Admin-side actions are driven straight through the API, because they are what
 * puts the applicant's UI into the states under test.
 */

const API = 'http://localhost:5088/api/v1/';

const SEEDED = {
  countryEn: 'Egypt',
  transactionTypeEn: 'Educational Certificate Verification',
  subTypeEn: "Bachelor's Degree",
  authorityEn: 'Supreme Council of Universities',
  standardServiceEn: 'Standard Certificate Verification',
};

const PDF = Buffer.concat([Buffer.from('%PDF-1.4\n% test\n'), Buffer.alloc(64, 1)]);
const PNG = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  Buffer.alloc(64, 1),
]);

const INTERNAL_NOTE = 'INTERNAL-NOTE-MUST-NEVER-REACH-THE-APPLICANT-9931';
const REVIEWER_MESSAGE = 'Please upload a clearer copy of your certificate.';

function uniqueEmail(prefix: string): string {
  return `${prefix}${Date.now()}${Math.floor(Math.random() * 1000)}@example.com`;
}

async function adminContext(): Promise<APIRequestContext> {
  const context = await request.newContext({ baseURL: API });
  const response = await context.post('admin/auth/login', {
    data: { email: 'admin@dataverification.local', password: 'Admin#12345' },
  });
  const { accessToken } = (await response.json()) as { accessToken: string };

  return request.newContext({
    baseURL: API,
    extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
  });
}

interface Session {
  orderId: string;
  orderNumber: string;
  password: string;
}

/** Credits the wallet and asserts it worked, so a failed setup step cannot pass silently. */
async function creditWallet(admin: APIRequestContext, orderId: string, amount: number) {
  expect(orderId, 'order id must be known before crediting the wallet').not.toBe('');

  // The note deliberately avoids the words used as column values, so assertions on the ledger's
  // "Top-up" type cell are not ambiguous with the note cell.
  const response = await admin.post(`admin/orders/${orderId}/wallet/credit`, {
    data: { amount, note: 'automated seed credit' },
  });

  expect(response.ok(), `wallet credit failed: ${response.status()} ${await response.text()}`).toBe(
    true,
  );
}

/** Registers, signs in and completes setup through the UI, returning the order identifiers. */
async function signInWithReadyOrder(page: Page): Promise<Session> {
  const email = uniqueEmail('dash');

  await page.goto('/en/register');
  await page.getByLabel(/email/i).fill(email);
  await page.locator('form button[type="submit"]').click();
  await expect(page.locator('dd').first()).toBeVisible({ timeout: 20_000 });

  const [orderNumber, password] = (await page.locator('dd').allTextContents()).map((v) => v.trim());

  await page.goto(`/en/login?order=${orderNumber}`);
  await page.locator('#password').fill(password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/en\/setup$/);

  await choose(page, 'verificationCountryId', SEEDED.countryEn);
  await chooseByIndex(page, 'currencyId', 0);
  await fillContactPerson(page);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/en\/applications$/);

  const orderId = await page.evaluate(
    () => (JSON.parse(localStorage.getItem('dv.session') ?? '{}') as { orderId?: string }).orderId ?? '',
  );

  return { orderId, orderNumber, password };
}

/**
 * Reloads and signs back in, so the client re-reads server state after an out-of-band admin action.
 *
 * The session now survives a page load, so `/login` bounces straight back to the dashboard while
 * one is live — signing out first is what makes the sign-in form reachable at all.
 */
async function reloadAndSignIn(page: Page, session: Session) {
  // The session outlives a page load, so `/login` bounces straight back to the dashboard while one
  // is live. It survives in two places — the order details in localStorage and the refresh cookie
  // that revives the token — so both have to go. Clicking Sign out instead races the app's first
  // render, which is what made this flaky.
  await page.goto('/en/applications');
  await page.evaluate(() => window.localStorage.removeItem('dv.session'));
  await page.context().clearCookies();

  await page.goto(`/en/login?order=${session.orderNumber}`);
  await page.locator('#password').fill(session.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/en\/applications$/);
}

/** Walks the wizard to produce one submitted (PendingPayment) application. */
async function createSubmittedApplication(page: Page): Promise<string> {
  // The list briefly renders from cache after submitting, so the new row is counted rather than
  // assumed — otherwise reading "the first row" can return the previous application.
  const before = await page.locator('td a[dir="ltr"]').count();

  await page.getByTestId('new-application').click();

  await page.getByTestId('addressedToChoice').selectOption('__other__');

  await page.locator('#addressedTo').fill('Ministry of Higher Education');
  await page.getByRole('button', { name: 'Next' }).click();

  await page.locator('#ar-first').fill('أحمد');
  await page.locator('#ar-last').fill('علي');
  await page.locator('#en-first').fill('Ahmed');
  await page.locator('#en-last').fill('Ali');
  await page.locator('#birthDate').fill('1990-05-17');
  await fillApplicantContact(page);
  await page.getByRole('button', { name: 'Next' }).click();

  await choose(page, 'transactionTypeId', SEEDED.transactionTypeEn);
  await choose(page, 'subTransactionTypeId', SEEDED.subTypeEn);
  await choose(page, 'verificationAuthorityId', SEEDED.authorityEn);
  await choose(page, 'service-type-0', SEEDED.standardServiceEn);
  await page.getByRole('button', { name: 'Next' }).click();

  await page.getByRole('button', { name: 'Next' }).click(); // summary → files (saves the draft)
  await expect(page.getByText(/draft saved/i)).toBeVisible({ timeout: 20_000 });

  const dropzones = page.locator('[data-testid^="dropzone-"]');
  await dropzones.nth(0).locator('input[type="file"]').setInputFiles({
    name: 'certificate.pdf',
    mimeType: 'application/pdf',
    buffer: PDF,
  });
  await expect(dropzones.nth(0).getByText('Uploaded')).toBeVisible({ timeout: 20_000 });

  await dropzones.nth(1).locator('input[type="file"]').setInputFiles({
    name: 'id-card.png',
    mimeType: 'image/png',
    buffer: PNG,
  });
  await expect(dropzones.nth(1).getByText('Uploaded')).toBeVisible({ timeout: 20_000 });

  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByTestId('submit-application').click();
  await expect(page).toHaveURL(/\/en\/applications$/, { timeout: 20_000 });

  // Newest first, so once the count has grown the first link is the application just created.
  await expect(page.locator('td a[dir="ltr"]')).toHaveCount(before + 1, { timeout: 20_000 });

  const reference = await page.locator('td a[dir="ltr"]').first().textContent();
  return (reference ?? '').trim();
}

test.describe('dashboard basics', () => {
  test('shows an empty state before anything is created', async ({ page }) => {
    await signInWithReadyOrder(page);
    await expect(page.getByText(/have not created any applications/i)).toBeVisible();
  });

  test('lists a submitted application awaiting payment', async ({ page }) => {
    await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    const row = page.getByTestId(`row-${reference}`);
    await expect(row).toBeVisible();
    await expect(row).toContainText('Awaiting payment');
    await expect(row).toContainText('750');
  });

  test('offers edit and delete while unpaid, and no refund', async ({ page }) => {
    await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    const row = page.getByTestId(`row-${reference}`);
    await expect(row.getByRole('link', { name: 'Edit' })).toBeVisible();
    await expect(page.getByTestId(`delete-${reference}`)).toBeVisible();
    await expect(page.getByTestId(`refund-${reference}`)).toHaveCount(0);
  });

  test('deletes an unpaid application after confirmation', async ({ page }) => {
    await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    await page.getByTestId(`delete-${reference}`).click();
    await expect(page.getByText(/will be removed/i)).toBeVisible();
    await page.getByTestId('confirm-delete').click();

    await expect(page.getByTestId(`row-${reference}`)).toHaveCount(0, { timeout: 20_000 });
  });
});

test.describe('multi-application payment', () => {
  test('blocks payment when the wallet cannot cover the total', async ({ page }) => {
    await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    await page.getByTestId(`select-${reference}`).check();
    await expect(page.getByTestId('selected-count')).toContainText('1 application selected');
    await page.getByTestId('pay-selected').click();

    // Wallet is empty, so the dialog refuses before any request is sent.
    await expect(page.getByText(/not enough balance/i)).toBeVisible();
    await expect(page.getByTestId('confirm-payment')).toBeDisabled();
  });

  test('pays two applications in a single transaction', async ({ page }) => {
    const session = await signInWithReadyOrder(page);
    const first = await createSubmittedApplication(page);
    const second = await createSubmittedApplication(page);

    const admin = await adminContext();
    await creditWallet(admin, session.orderId, 5000);

    await reloadAndSignIn(page, session);

    await page.getByTestId(`select-${first}`).check();
    await page.getByTestId(`select-${second}`).check();

    await expect(page.getByTestId('selected-count')).toContainText('2 applications selected');
    await expect(page.getByTestId('selected-total')).toContainText('1,500');

    await page.getByTestId('pay-selected').click();
    await expect(page.getByTestId('dialog-total')).toContainText('1,500');
    await expect(page.getByTestId('dialog-balance')).toContainText('5,000');

    await page.getByTestId('confirm-payment').click();
    await expect(page.getByTestId('payment-success')).toContainText('2 applications have been paid');

    // Both flip to paid, and the capability flags change with them.
    for (const reference of [first, second]) {
      const row = page.getByTestId(`row-${reference}`);
      await expect(row).toContainText('Paid');
      await expect(row).toContainText('Paid — in queue');
      await expect(page.getByTestId(`delete-${reference}`)).toHaveCount(0);
      await expect(page.getByTestId(`refund-${reference}`)).toBeVisible();
    }

    await admin.dispose();
  });

  test('select-all picks every unpaid application', async ({ page }) => {
    await signInWithReadyOrder(page);
    const first = await createSubmittedApplication(page);
    const second = await createSubmittedApplication(page);

    // Both rows have to be listed before the header box is touched: "select all" compares the
    // selection against the payable rows it knew about, so ticking it mid-refresh selects the old
    // set and then reads as unchecked once the new row arrives.
    await expect(page.getByTestId(`select-${first}`)).toBeVisible();
    await expect(page.getByTestId(`select-${second}`)).toBeVisible();

    await page.getByTestId('select-all').check();
    await expect(page.getByTestId('selected-count')).toContainText('2 applications selected');
  });
});

test.describe('refund and wallet', () => {
  test('refunds a paid application and credits the wallet', async ({ page }) => {
    const session = await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    const admin = await adminContext();
    await creditWallet(admin, session.orderId, 2000);
    await reloadAndSignIn(page, session);

    await page.getByTestId(`select-${reference}`).check();
    await page.getByTestId('pay-selected').click();
    await page.getByTestId('confirm-payment').click();
    await expect(page.getByTestId('payment-success')).toBeVisible();

    // 2000 − 750 = 1250 before the refund.
    await page.getByTestId(`refund-${reference}`).click();
    await expect(page.getByText(/returned to your wallet/i)).toBeVisible();
    await page.getByTestId('confirm-refund').click();

    await expect(page.getByTestId(`row-${reference}`)).toContainText('Refunded', { timeout: 20_000 });

    await page.getByTestId('wallet-link').click();
    await expect(page.getByTestId('wallet-balance')).toContainText('2,000');

    // Top-up, payment and refund all appear on the statement.
    await expect(page.getByRole('cell', { name: 'Top-up' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Payment' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Refund' })).toBeVisible();
    await expect(page.getByText(reference).first()).toBeVisible();

    await admin.dispose();
  });
});

test.describe('application details and activity', () => {
  test('shows details, services and documents across tabs', async ({ page }) => {
    await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    await page.getByRole('link', { name: reference }).click();
    await expect(page.getByRole('heading', { name: reference })).toBeVisible();

    await page.getByTestId('tab-services').click();
    await expect(page.getByText(SEEDED.standardServiceEn)).toBeVisible();

    await page.getByTestId('tab-files').click();
    await expect(page.getByText('certificate.pdf')).toBeVisible();
    await expect(page.getByText('id-card.png')).toBeVisible();

    await page.getByTestId('tab-timeline').click();
    await expect(page.getByTestId('timeline-status').first()).toBeVisible();
  });

  test('runs the missed-information conversation and never leaks internal notes', async ({ page }) => {
    const session = await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    const admin = await adminContext();
    await creditWallet(admin, session.orderId, 2000);
    await reloadAndSignIn(page, session);

    await page.getByTestId(`select-${reference}`).check();
    await page.getByTestId('pay-selected').click();
    await page.getByTestId('confirm-payment').click();
    await expect(page.getByTestId('payment-success')).toBeVisible();

    // Find the application id so the admin can act on it.
    const queue = await admin.get('admin/applications', { params: { search: reference } });
    const { items } = (await queue.json()) as { items: Array<{ id: string }> };
    const applicationId = items[0].id;

    await admin.post(`admin/applications/${applicationId}/status`, {
      data: { toStatus: 3, note: 'Started' },
    });
    await admin.post(`admin/applications/${applicationId}/comments`, {
      data: { body: INTERNAL_NOTE, visibility: 1 },
    });
    await admin.post(`admin/applications/${applicationId}/comments`, {
      data: { body: REVIEWER_MESSAGE, visibility: 0 },
    });

    // Navigated in-app: the token is in memory, so a full load would end the session.
    await reloadAndSignIn(page, session);
    await page.getByRole('link', { name: reference }).click();
    await expect(page.getByText(/we need more information/i)).toBeVisible();

    await page.getByTestId('tab-timeline').click();

    // The reviewer's message is on the leading edge; internal notes are absent entirely.
    await expect(page.getByTestId('comment-theirs')).toContainText(REVIEWER_MESSAGE);
    await expect(page.locator('body')).not.toContainText(INTERNAL_NOTE);

    const html = await page.content();
    expect(html).not.toContain(INTERNAL_NOTE);

    await page.getByTestId('reply-box').fill('I have uploaded a clearer copy.');
    await page.getByTestId('send-reply').click();
    await expect(page.getByTestId('comment-mine')).toContainText('I have uploaded a clearer copy.');

    await page.getByTestId('resubmit').click();
    // The badge, not free text: "In progress" also appears in two timeline status entries.
    await expect(page.getByTestId('application-status')).toHaveText('In progress', {
      timeout: 20_000,
    });

    // Once verified, the deliverables become downloadable.
    await admin.post(`admin/applications/${applicationId}/status`, {
      data: { toStatus: 5, note: 'Verified' },
    });
    await admin.post(`admin/applications/${applicationId}/results`, {
      multipart: {
        file: { name: 'verified.pdf', mimeType: 'application/pdf', buffer: PDF },
      },
    });

    await reloadAndSignIn(page, session);
    await page.getByRole('link', { name: reference }).click();
    await expect(page.getByTestId('results-section')).toBeVisible();
    await expect(page.getByTestId('results-section')).toContainText('verified.pdf');

    const finalHtml = await page.content();
    expect(finalHtml).not.toContain(INTERNAL_NOTE);

    await admin.dispose();
  });

  test('renders the conversation right-to-left in Arabic', async ({ page }) => {
    const session = await signInWithReadyOrder(page);
    const reference = await createSubmittedApplication(page);

    const admin = await adminContext();
    await creditWallet(admin, session.orderId, 2000);
    await reloadAndSignIn(page, session);
    await page.getByTestId(`select-${reference}`).check();
    await page.getByTestId('pay-selected').click();
    await page.getByTestId('confirm-payment').click();
    await expect(page.getByTestId('payment-success')).toBeVisible();

    const queue = await admin.get('admin/applications', { params: { search: reference } });
    const { items } = (await queue.json()) as { items: Array<{ id: string }> };
    await admin.post(`admin/applications/${items[0].id}/status`, { data: { toStatus: 3 } });
    await admin.post(`admin/applications/${items[0].id}/comments`, {
      data: { body: 'الصورة غير واضحة، يرجى رفعها مرة أخرى', visibility: 0 },
    });

    await chooseLanguage(page, 'العربية');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await page.getByRole('link', { name: reference }).click();
    await page.getByTestId('tab-timeline').click();

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByTestId('comment-theirs')).toContainText('الصورة غير واضحة');

    await admin.dispose();
  });
});
