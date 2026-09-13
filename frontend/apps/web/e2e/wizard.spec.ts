import { expect, test, type Page } from '@playwright/test';
import { choose, chooseByIndex, chooseLanguage, selectedLabel } from './support/searchable-select';
import { fillContactPerson } from './support/contact-person';
import { fillApplicantContact } from './support/applicant-contact';

/**
 * Drives the six-step application wizard end to end, including a real multipart upload, and
 * checks the rules that must hold at each step.
 */

const SEEDED = {
  countryEn: 'Egypt',
  transactionTypeEn: 'Educational Certificate Verification',
  subTypeEn: "Bachelor's Degree",
  authorityEn: 'Supreme Council of Universities',
  standardServiceEn: 'Standard Certificate Verification', // 750, no express, 2 required files
  expressServiceEn: 'Attested Verification', // 1500 + 600 express
};

/** A genuine PDF signature, so the server's magic-byte check accepts it. */
const PDF = Buffer.concat([Buffer.from('%PDF-1.4\n% test\n'), Buffer.alloc(64, 1)]);
/**
 * A genuinely decodable 2x2 PNG.
 *
 * A bare PNG signature followed by filler is enough for the server's magic-byte check, but a
 * browser cannot render it — and the review step asserts on what a preview actually shows, so the
 * fixture has to be a real image.
 */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFElEQVR4nGP8z4AATAxQxhArAQCE8QEDs4vJqQAAAABJRU5ErkJggg==',
  'base64',
);

function uniqueEmail(prefix: string): string {
  return `${prefix}${Date.now()}${Math.floor(Math.random() * 1000)}@example.com`;
}

/** Registers, signs in and completes order setup, leaving the browser on the dashboard. */
async function signInWithReadyOrder(page: Page) {
  const email = uniqueEmail('wizard');

  await page.goto('/en/register');
  await page.getByLabel(/email/i).fill(email);
  await page.locator('form button[type="submit"]').click();

  // Wait for the success screen before reading — the credentials only exist once the
  // registration response has come back.
  await expect(page.locator('dd').first()).toBeVisible({ timeout: 20_000 });

  const values = await page.locator('dd').allTextContents();
  const [orderNumber, password] = values.map((value) => value.trim());

  await page.goto(`/en/login?order=${orderNumber}`);
  await page.locator('#password').fill(password);
  await page.locator('form button[type="submit"]').click();

  await expect(page).toHaveURL(/\/en\/setup$/);
  await choose(page, 'verificationCountryId', SEEDED.countryEn);
  await chooseByIndex(page, 'currencyId', 0);
  await fillContactPerson(page);
  await page.locator('form button[type="submit"]').click();

  await expect(page).toHaveURL(/\/en\/applications$/);
  return { orderNumber, password };
}

/** Fills steps 1 and 2 and leaves the wizard on step 3. */
async function completeStepsOneAndTwo(page: Page) {
  await page.getByTestId('new-application').click();
  await expect(page).toHaveURL(/\/applications\/new$/);

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
}

/** Fills the cascade on step 3 with the given service and continues to the summary. */
async function completeCascade(page: Page, serviceLabel: string, express = false) {
  await choose(page, 'transactionTypeId', SEEDED.transactionTypeEn);
  await choose(page, 'subTransactionTypeId', SEEDED.subTypeEn);
  await choose(page, 'verificationAuthorityId', SEEDED.authorityEn);
  await choose(page, 'service-type-0', serviceLabel);

  if (express) {
    await page.locator('#service-express-0').check();
  }
}

test.describe('wizard navigation and validation', () => {
  test.beforeEach(async ({ page }) => {
    await signInWithReadyOrder(page);
  });

  test('step 1 requires an addressee', async ({ page }) => {
    await page.getByTestId('new-application').click();
    await page.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByRole('alert')).toContainText('Say who this request is addressed to');
  });

  test('step 1 offers the configured addressee list', async ({ page }) => {
    await page.getByTestId('new-application').click();

    const choice = page.getByTestId('addressedToChoice');
    await expect(choice).toBeVisible();

    // The list an operator maintains in the admin panel, seeded with a starting set.
    await expect(choice.locator('option')).toContainText(['Ministry of Foreign Affairs']);

    // Picking one is the whole answer: no second field appears, and the step moves on.
    await choice.selectOption('Ministry of Foreign Affairs');
    await expect(page.locator('#addressedTo')).toBeHidden();

    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByRole('heading', { name: 'Personal details' })).toBeVisible();
  });

  test('step 1 accepts an addressee that is not on the list', async ({ page }) => {
    await page.getByTestId('new-application').click();

    // "Other" is what makes the list a convenience rather than a restriction.
    await page.getByTestId('addressedToChoice').selectOption('__other__');

    const custom = page.locator('#addressedTo');
    await expect(custom).toBeVisible();
    await expect(custom).toHaveValue('');

    await custom.fill('A body that is deliberately not on the list');
    await page.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByRole('heading', { name: 'Personal details' })).toBeVisible();
  });

  test('step 2 requires both Arabic and English names', async ({ page }) => {
    await page.getByTestId('new-application').click();
    await page.getByTestId('addressedToChoice').selectOption('__other__');
    await page.locator('#addressedTo').fill('Ministry');
    await page.getByRole('button', { name: 'Next' }).click();

    // Only the English name is supplied, so the Arabic fields must complain.
    await page.locator('#en-first').fill('Ahmed');
    await page.locator('#en-last').fill('Ali');
    await page.locator('#birthDate').fill('1990-05-17');
    await fillApplicantContact(page);
    await page.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByRole('alert').first()).toBeVisible();
    await expect(page.locator('#ar-first')).toHaveAttribute('aria-invalid', 'true');
  });

  test('step 2 rejects a future date of birth', async ({ page }) => {
    await page.getByTestId('new-application').click();
    await page.getByTestId('addressedToChoice').selectOption('__other__');
    await page.locator('#addressedTo').fill('Ministry');
    await page.getByRole('button', { name: 'Next' }).click();

    await page.locator('#ar-first').fill('أحمد');
    await page.locator('#ar-last').fill('علي');
    await page.locator('#en-first').fill('Ahmed');
    await page.locator('#en-last').fill('Ali');
    await page.locator('#birthDate').fill('2099-01-01');
    await fillApplicantContact(page);
    await page.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByText('The date of birth must be in the past')).toBeVisible();
  });

  test('step 2 requires the applicant email and phone, and keeps them through the wizard', async ({
    page,
  }) => {
    await page.getByTestId('new-application').click();
    await page.getByTestId('addressedToChoice').selectOption('__other__');
    await page.locator('#addressedTo').fill('Ministry');
    await page.getByRole('button', { name: 'Next' }).click();

    await page.locator('#ar-first').fill('أحمد');
    await page.locator('#ar-last').fill('علي');
    await page.locator('#en-first').fill('Ahmed');
    await page.locator('#en-last').fill('Ali');
    await page.locator('#birthDate').fill('1990-05-17');

    // Names and date are complete, so only the two new fields can hold the step back.
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Enter an email address.')).toBeVisible();
    await expect(page.getByText('Enter a phone number.')).toBeVisible();

    await page.locator('#applicantEmail').fill('not-an-email');
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Enter a valid email address.')).toBeVisible();

    await page.locator('#applicantEmail').fill('ahmed@example.com');
    // Non-digits are dropped as typed, so the dial code cannot be entered twice.
    await page.locator('#applicantPhoneCountryNumber').fill('+20 100 555 0101');
    await expect(page.locator('#applicantPhoneCountryNumber')).toHaveValue('201005550101');

    await page.locator('#applicantPhoneCountryNumber').fill('1005550101');
    await page.getByRole('button', { name: 'Next' }).click();

    // Past step 2 — the cascade step is what comes next.
    await expect(page.locator('#transactionTypeId')).toBeVisible();

    // And back on step 2 the values are still there, not reset by the round trip.
    await page.getByRole('button', { name: 'Previous' }).click();
    await expect(page.locator('#applicantEmail')).toHaveValue('ahmed@example.com');
    await expect(page.locator('#applicantPhoneCountryNumber')).toHaveValue('1005550101');
    await expect(page.locator('#applicantPhoneCountry')).toContainText('+20');
  });
});

test.describe('step 3 cascade', () => {
  test.beforeEach(async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
  });

  test('dependent selects stay disabled until their parent is chosen', async ({ page }) => {
    await expect(page.locator('#subTransactionTypeId')).toBeDisabled();
    await expect(page.locator('#verificationAuthorityId')).toBeDisabled();

    await choose(page, 'transactionTypeId', SEEDED.transactionTypeEn);
    await expect(page.locator('#subTransactionTypeId')).toBeEnabled();
    await expect(page.locator('#verificationAuthorityId')).toBeDisabled();

    await choose(page, 'subTransactionTypeId', SEEDED.subTypeEn);
    await expect(page.locator('#verificationAuthorityId')).toBeEnabled();
  });

  test('changing a parent clears its dependents immediately', async ({ page }) => {
    await completeCascade(page, SEEDED.standardServiceEn);

    // Switching the transaction type invalidates the sub-type, authority and services. This used
    // to raise a "Change this selection?" prompt; it now just applies.
    await chooseByIndex(page, 'transactionTypeId', 1);

    await expect(page.getByText('Change this selection?')).toHaveCount(0);

    // These are searchable selects, not inputs: a cleared one shows its placeholder again, and the
    // two that depend on a further choice fall back to disabled.
    await expect(selectedLabel(page, 'subTransactionTypeId')).not.toHaveText(SEEDED.subTypeEn);
    await expect(page.locator('#verificationAuthorityId')).toBeDisabled();
    await expect(page.locator('#service-type-0')).toBeDisabled();
  });

  test('the express toggle appears only for a service that offers it', async ({ page }) => {
    await completeCascade(page, SEEDED.standardServiceEn);
    await expect(page.locator('#service-express-0')).toHaveCount(0);
    await expect(page.getByText('Express delivery is not offered')).toBeVisible();

    await choose(page, 'service-type-0', SEEDED.expressServiceEn);
    await expect(page.locator('#service-express-0')).toBeVisible();
  });

  test('service rows can be added and removed', async ({ page }) => {
    await completeCascade(page, SEEDED.standardServiceEn);

    await page.getByRole('button', { name: /add another service/i }).click();
    await expect(page.locator('#service-type-1')).toBeVisible();

    await page.getByRole('button', { name: /remove this service/i }).first().click();
    await expect(page.locator('#service-type-1')).toHaveCount(0);
  });
});

test.describe('summary, upload and submission', () => {
  test('computes the standard total and reaches the summary', async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
    await completeCascade(page, SEEDED.standardServiceEn);
    await page.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByRole('heading', { name: /services summary/i })).toBeVisible();
    await expect(page.getByTestId('grand-total')).toContainText('750');

    // The required-document checklist for this service is advertised up front.
    await expect(page.getByText(/documents you will need/i)).toBeVisible();
  });

  test('adds the express surcharge to the total', async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
    await completeCascade(page, SEEDED.expressServiceEn, true);
    await page.locator('#service-qty-0').fill('2');
    await page.getByRole('button', { name: 'Next' }).click();

    // (1500 + 600) × 2 = 4200
    await expect(page.getByTestId('grand-total')).toContainText('4,200');
  });

  test('completes the whole wizard and submits the application', async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
    await completeCascade(page, SEEDED.standardServiceEn);
    await page.getByRole('button', { name: 'Next' }).click();

    // Entering the upload step saves the draft, which is what creates the file targets.
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText(/draft saved/i)).toBeVisible({ timeout: 20_000 });

    // Both documents for this service are mandatory, so Next stays disabled until both land.
    await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled();

    const dropzones = page.locator('[data-testid^="dropzone-"]');
    await expect(dropzones).toHaveCount(2);

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

    await expect(page.getByText(/all required documents have been uploaded/i)).toBeVisible();
    await page.getByRole('button', { name: 'Next' }).click();

    // The review step reads everything back from the server.
    await expect(page.getByRole('heading', { name: /review your application/i })).toBeVisible();
    await expect(page.getByTestId('review-total')).toContainText('750');
    await expect(page.getByText('certificate.pdf')).toBeVisible();

    // The documents are shown, not merely named: this is the last moment to notice the wrong
    // page went up, and a filename cannot show that.
    const thumbnail = page.locator('[data-testid="review-file-id-card.png"] img');
    await expect(thumbnail).toBeVisible({ timeout: 20_000 });

    // Polled rather than read once: the element appears as soon as the object URL is set, a
    // moment before the browser has finished decoding it.
    await expect
      .poll(
        async () => thumbnail.evaluate((image: HTMLImageElement) => image.naturalWidth),
        { timeout: 20_000 },
      )
      .toBeGreaterThan(0);

    // A PDF keeps its icon — a browser cannot thumbnail one without a rendering library, and a
    // broken image would say less than a clear label.
    await expect(page.locator('[data-testid="review-file-certificate.pdf"] img')).toHaveCount(0);

    // ...and opens full size for a proper look.
    await page.locator('[data-testid="review-file-id-card.png"]').click();
    await expect(page.locator('dialog[open] img')).toBeVisible({ timeout: 20_000 });
    await page.keyboard.press('Escape');

    await page.getByTestId('submit-application').click();
    await expect(page).toHaveURL(/\/en\/applications$/, { timeout: 20_000 });
  });

  test('rejects a disallowed file type before uploading', async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
    await completeCascade(page, SEEDED.standardServiceEn);
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText(/draft saved/i)).toBeVisible({ timeout: 20_000 });

    const first = page.locator('[data-testid^="dropzone-"]').first();
    await first.locator('input[type="file"]').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('plain text'),
    });

    // The formats are chosen per document now, so the message names the document's own list
    // rather than a fixed platform-wide sentence.
    await expect(first.getByRole('alert')).toContainText('.pdf');
    await expect(first.getByRole('alert')).toContainText('accepts only');
  });

  test('rejects a file over 5 MB before uploading', async ({ page }) => {
    await signInWithReadyOrder(page);
    await completeStepsOneAndTwo(page);
    await completeCascade(page, SEEDED.standardServiceEn);
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText(/draft saved/i)).toBeVisible({ timeout: 20_000 });

    const first = page.locator('[data-testid^="dropzone-"]').first();
    await first.locator('input[type="file"]').setInputFiles({
      name: 'huge.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.concat([PDF, Buffer.alloc(5 * 1024 * 1024, 1)]),
    });

    await expect(first.getByRole('alert')).toContainText('larger than 5 MB');
  });
});

test.describe('wizard in Arabic', () => {
  test('renders right-to-left and completes the cascade', async ({ page }) => {
    await signInWithReadyOrder(page);

    // Switched in-app rather than via page.goto: the access token is held in memory only, so a
    // full page load would legitimately sign the user out.
    await chooseLanguage(page, 'العربية');
    await expect(page).toHaveURL(/\/ar\/applications$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await page.getByTestId('new-application').click();
    await expect(page).toHaveURL(/\/ar\/applications\/new$/);
    await expect(page.getByRole('heading', { name: 'طلب تحقق جديد' })).toBeVisible();

    await page.getByTestId('addressedToChoice').selectOption('__other__');

    await page.locator('#addressedTo').fill('وزارة التعليم العالي');
    await page.getByRole('button', { name: 'التالي' }).click();

    await page.locator('#ar-first').fill('أحمد');
    await page.locator('#ar-last').fill('علي');
    await page.locator('#en-first').fill('Ahmed');
    await page.locator('#en-last').fill('Ali');
    await page.locator('#birthDate').fill('1990-05-17');
    await fillApplicantContact(page);
    await page.getByRole('button', { name: 'التالي' }).click();

    // Lookup names arrive from the API in Arabic because Accept-Language follows the UI. The
    // control shows its placeholder until something is picked, so the options are what to read.
    await page.locator('#transactionTypeId').click();
    await expect(
      page
        .locator('#transactionTypeId-listbox')
        .getByRole('option', { name: 'التحقق من الشهادات الدراسية' }),
    ).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  });
});
