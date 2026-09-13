import { expect, test, type Page } from '@playwright/test';
import { fillContactPerson } from './support/contact-person';
import { fillApplicantContact } from './support/applicant-contact';

/**
 * Starting a new application must start blank.
 *
 * The wizard used to mirror its state into sessionStorage so a refresh would not cost the
 * applicant their typing. Abandoning the wizard left that state behind, so the next "New
 * application" opened pre-filled with the previous attempt — and if the abandoned attempt had
 * reached the upload step it carried its application id too, which made the next application
 * silently overwrite the earlier draft instead of creating one.
 */

const SEEDED = {
  countryEn: 'Egypt',
  transactionTypeEn: 'Educational Certificate Verification',
  subTypeEn: "Bachelor's Degree",
  authorityEn: 'Supreme Council of Universities',
  standardServiceEn: 'Standard Certificate Verification',
};

function uniqueEmail(prefix: string): string {
  return `${prefix}${Date.now()}${Math.floor(Math.random() * 1000)}@example.com`;
}

/**
 * Country and currency are `SearchableSelect`s — a button plus a listbox, not a native `<select>`,
 * so they are driven by clicking rather than by `selectOption`. The button stays disabled until
 * its options have loaded, which is what `toBeEnabled` waits for.
 */
async function pickOption(page: Page, id: string, name: RegExp) {
  const trigger = page.locator(`#${id}`);
  await expect(trigger).toBeEnabled({ timeout: 20_000 });
  await trigger.click();
  await page.locator(`#${id}-listbox`).getByRole('option', { name }).first().click();
}

async function signInWithReadyOrder(page: Page) {
  const email = uniqueEmail('fresh');

  await page.goto('/en/register');
  await page.getByLabel(/email/i).fill(email);
  await page.locator('form button[type="submit"]').click();

  await expect(page.locator('dd').first()).toBeVisible({ timeout: 20_000 });
  const [orderNumber, password] = (await page.locator('dd').allTextContents()).map((v) => v.trim());

  await page.goto(`/en/login?order=${orderNumber}`);
  await page.locator('#password').fill(password);
  await page.locator('form button[type="submit"]').click();

  await expect(page).toHaveURL(/\/en\/setup$/);
  await pickOption(page, 'verificationCountryId', new RegExp(SEEDED.countryEn));
  await pickOption(page, 'currencyId', /EGP/);
  await fillContactPerson(page);
  await page.locator('form button[type="submit"]').click();

  await expect(page).toHaveURL(/\/en\/applications$/, { timeout: 20_000 });
}

/**
 * Fills the "Addressed to" step. The field is a dropdown of the addressees an operator configured,
 * with an "Other" entry that reveals a free-text box — these tests use their own wording, so they
 * take the "Other" route unless they are exercising the list itself.
 */
async function fillAddressedTo(page: Page, addressedTo: string) {
  await page.getByTestId('addressedToChoice').selectOption('__other__');
  await page.locator('#addressedTo').fill(addressedTo);
}

/** Fills the addressee and personal steps, leaving the wizard on the details step. */
async function fillFirstTwoSteps(page: Page, addressedTo: string, englishFirstName: string) {
  await fillAddressedTo(page, addressedTo);
  await page.getByRole('button', { name: 'Next' }).click();

  await page.locator('#ar-first').fill('أحمد');
  await page.locator('#ar-last').fill('علي');
  await page.locator('#en-first').fill(englishFirstName);
  await page.locator('#en-last').fill('Ali');
  await page.locator('#birthDate').fill('1990-05-17');
  await fillApplicantContact(page);
  await page.getByRole('button', { name: 'Next' }).click();
}

test('an abandoned wizard does not pre-fill the next new application', async ({ page }) => {
  await signInWithReadyOrder(page);

  await page.getByTestId('new-application').click();
  await fillFirstTwoSteps(page, 'Abandoned Ministry', 'Abandoned');

  // Leave without saving — the case the old autosave was built for, and the one it got wrong.
  await page.goto('/en/applications');
  await expect(page).toHaveURL(/\/en\/applications$/);

  await page.getByTestId('new-application').click();
  await expect(page).toHaveURL(/\/applications\/new$/);

  await expect(page.getByTestId('addressedToChoice')).toHaveValue('');

  // And the steps behind it are blank too, not merely the one on screen.
  await fillAddressedTo(page, 'Second Ministry');
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.locator('#en-first')).toHaveValue('');
  await expect(page.locator('#ar-first')).toHaveValue('');
  await expect(page.locator('#birthDate')).toHaveValue('');
  await expect(page.locator('#applicantEmail')).toHaveValue('');
  await expect(page.locator('#applicantPhoneCountryNumber')).toHaveValue('');
});

test('a reload no longer carries the wizard over, and nothing is left in storage', async ({ page }) => {
  await signInWithReadyOrder(page);

  await page.getByTestId('new-application').click();
  await fillFirstTwoSteps(page, 'Reloaded Ministry', 'Reloaded');

  const stored = await page.evaluate(() => window.sessionStorage.getItem('dv.wizard'));
  expect(stored).toBeNull();

  await page.reload();
  await expect(page.getByTestId('addressedToChoice')).toHaveValue('');
});

/**
 * Saves a draft addressed to `addressedTo`, then leaves for the list.
 *
 * Saving deliberately keeps the applicant on the step — it is a checkpoint, not an exit — so the
 * navigation is explicit here rather than a side effect of the button.
 */
async function saveDraftFrom(page: Page, addressedTo: string) {
  await page.getByTestId('new-application').click();
  await fillFirstTwoSteps(page, addressedTo, 'Draft');
  await page.getByRole('button', { name: /save draft/i }).click();

  await expect(page.getByTestId('draft-saved')).toBeVisible({ timeout: 20_000 });
  await expect(page).toHaveURL(/\/applications\/new$/);

  await page.goto('/en/applications');
  await expect(page).toHaveURL(/\/en\/applications$/, { timeout: 20_000 });
}

test('a new application after editing one creates a second application', async ({ page }) => {
  await signInWithReadyOrder(page);

  await saveDraftFrom(page, 'First Ministry');
  await expect(page.getByText('First Ministry')).toBeVisible();

  // Open it for editing, change nothing, leave. This is what used to poison the next new
  // application: the edited draft's id stayed behind and the next save updated it in place.
  await page.locator('[data-testid^="edit-"]').first().click();
  // Reopened from a saved draft: not on the configured list, so it comes back in the
  // free-text box with the wording kept.
  await expect(page.locator('#addressedTo')).toHaveValue('First Ministry');
  await page.goto('/en/applications');

  // count() does not wait, so the table has to be on screen before it is read — otherwise the
  // baseline is zero and the assertion below is measured against nothing.
  await page.locator('tbody tr').first().waitFor({ timeout: 20_000 });
  const before = await page.locator('tbody tr').count();

  await page.getByTestId('new-application').click();
  await expect(page.getByTestId('addressedToChoice')).toHaveValue('');

  await fillFirstTwoSteps(page, 'Second Ministry', 'Draft');
  await page.getByRole('button', { name: /save draft/i }).click();
  await expect(page.getByTestId('draft-saved')).toBeVisible({ timeout: 20_000 });
  await page.goto('/en/applications');

  // A second row, not a rewritten first one.
  await expect(page.locator('tbody tr')).toHaveCount(before + 1);
  await expect(page.getByText('First Ministry')).toBeVisible();
  await expect(page.getByText('Second Ministry')).toBeVisible();
});
