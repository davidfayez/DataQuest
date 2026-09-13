import { expect, test, type Page } from '@playwright/test';

/**
 * Lookups → Services: a service must be priced in every currency it can be sold in.
 *
 * The applicant's service list is filtered by their order's currency, so a service missing one
 * currency does not merely lack a price for those applicants — it never appears for them, and
 * nothing anywhere says why. The form therefore generates one row per currency in scope rather
 * than letting an operator add the currencies they happen to remember.
 *
 * How many currencies that is depends on which countries the transaction type runs in, which an
 * operator can change — so these tests count the rows the form renders rather than assuming a
 * number.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#identifier').fill(SUPER_ADMIN.email);
  await page.locator('#password').fill(SUPER_ADMIN.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/$/);
}

async function openNewServiceForm(page: Page) {
  await signIn(page);

  await page.goto('/lookups/serviceTypes');
  await page.getByTestId('lookup-new').click();

  // Until a sub-type is chosen there are no currencies to price — the scope decides them.
  await expect(page.getByTestId('cost-progress')).toBeHidden();

  await page.locator('#authorityId').selectOption({ label: 'Supreme Council of Universities' });
  await page.locator('#subTransactionTypeId').selectOption({ label: "Bachelor's Degree" });
  await page.getByTestId('lookup-code').fill(`SVC-${Date.now()}`);

  await expect(page.getByTestId('cost-progress')).toBeVisible();
}

/** The currency codes the form generated a row for. */
async function pricedCurrencies(page: Page): Promise<string[]> {
  const rows = page.locator('[data-testid^="cost-row-"]');
  await expect(rows.first()).toBeVisible();

  const codes = await rows.evaluateAll((elements) =>
    elements.map((element) => element.getAttribute('data-testid')!.replace('cost-row-', '')),
  );

  expect(codes.length).toBeGreaterThan(1);
  return codes;
}

test('a service cannot be saved until every currency has a price', async ({ page }) => {
  await openNewServiceForm(page);

  const codes = await pricedCurrencies(page);
  const save = page.getByTestId('dialog-submit');

  await expect(page.getByTestId('cost-progress')).toHaveText(new RegExp(`0 of ${codes.length}`));
  await expect(save).toBeDisabled();

  // Every unpriced currency is named, so the operator is never left hunting for the empty one.
  for (const code of codes) {
    await expect(page.getByTestId('costs-incomplete')).toContainText(code);
  }

  await page.locator('#nameEn').fill('Pricing e2e');
  await page.locator('#nameAr').fill('اختبار التسعير');

  // A service needs a document too; this test is about the prices, so satisfy that and move on.
  await page.getByTestId('add-required-file').click();

  // Filling all but the last leaves it unsaveable, and the warning narrows to what is left.
  for (const code of codes.slice(0, -1)) {
    await page.getByTestId(`cost-${code}`).fill('750');
  }

  const last = codes[codes.length - 1]!;
  await expect(page.getByTestId('cost-progress')).toHaveText(
    new RegExp(`${codes.length - 1} of ${codes.length}`),
  );
  await expect(save).toBeDisabled();
  await expect(page.getByTestId('costs-incomplete')).toContainText(last);

  // Zero is a real price, not a blank — a free service has to be expressible.
  await page.getByTestId(`cost-${last}`).fill('0');

  await expect(page.getByTestId('cost-progress')).toHaveText(
    new RegExp(`${codes.length} of ${codes.length}`),
  );
  await expect(page.getByTestId('costs-incomplete')).toBeHidden();
  await expect(save).toBeEnabled();
});

test('enabling express asks for a surcharge in every currency', async ({ page }) => {
  await openNewServiceForm(page);

  const codes = await pricedCurrencies(page);

  await page.locator('#nameEn').fill('Express pricing e2e');
  await page.locator('#nameAr').fill('اختبار السريع');
  await page.getByTestId('add-required-file').click();

  for (const code of codes) {
    await page.getByTestId(`cost-${code}`).fill('750');
  }

  const save = page.getByTestId('dialog-submit');
  await expect(save).toBeEnabled();

  // Express amounts are inert until express is offered, and required the moment it is.
  const first = codes[0]!;
  await expect(page.getByTestId(`express-${first}`)).toBeDisabled();
  await page.getByTestId('enable-express').check();
  await expect(page.getByTestId(`express-${first}`)).toBeEnabled();

  await expect(save).toBeDisabled();
  await expect(page.getByTestId('missing-express')).toContainText(first);

  for (const code of codes) {
    await page.getByTestId(`express-${code}`).fill('200');
  }

  await expect(page.getByTestId('missing-express')).toBeHidden();
  await expect(save).toBeEnabled();
});

test('a service needs a document, and each document names the formats it takes', async ({ page }) => {
  await openNewServiceForm(page);

  const codes = await pricedCurrencies(page);
  for (const code of codes) {
    await page.getByTestId(`cost-${code}`).fill('750');
  }
  await page.locator('#nameEn').fill('Documents e2e');
  await page.locator('#nameAr').fill('اختبار المستندات');

  // Priced in full, but asking the applicant to upload nothing.
  const save = page.getByTestId('dialog-submit');
  await expect(page.getByTestId('documents-required')).toBeVisible();
  await expect(save).toBeDisabled();

  await page.getByTestId('add-required-file').click();
  await expect(page.getByTestId('documents-required')).toBeHidden();
  await expect(save).toBeEnabled();

  // A new document starts on the platform default rather than accepting nothing.
  await expect(page.getByTestId('doc-type-0-pdf')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('doc-type-0-jpg')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('doc-type-0-png')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('doc-type-0-word')).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByTestId('doc-type-0-excel')).toHaveAttribute('aria-pressed', 'false');

  await page.getByTestId('doc-type-0-word').click();
  await expect(page.getByTestId('doc-type-0-word')).toHaveAttribute('aria-pressed', 'true');

  // Narrow to Word alone, then confirm the last remaining format cannot be switched off — a
  // document that accepts nothing could never be satisfied.
  for (const code of ['pdf', 'jpg', 'png']) {
    await page.getByTestId(`doc-type-0-${code}`).click();
  }

  await expect(page.getByTestId('doc-type-0-word')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('doc-type-0-word')).toBeDisabled();
  await expect(save).toBeEnabled();
});

test('an existing service reopens showing what it is still missing', async ({ page }) => {
  await signIn(page);
  await page.goto('/lookups/serviceTypes');

  // A service configured before this rule existed may be priced in only some currencies.
  // Reopening it has to surface that rather than hide it behind the rows that are filled.
  await page.locator('[data-testid^="edit-"]').first().click();
  await expect(page.getByTestId('cost-progress')).toBeVisible();

  const codes = await pricedCurrencies(page);
  const progress = await page.getByTestId('cost-progress').textContent();
  const priced = Number(progress!.match(/(\d+)/)![1]);

  // Whichever state it is in, the count is honest and the button agrees with the count.
  if (priced < codes.length) {
    await expect(page.getByTestId('dialog-submit')).toBeDisabled();
    await expect(page.getByTestId('costs-incomplete')).toBeVisible();
  } else {
    await expect(page.getByTestId('costs-incomplete')).toBeHidden();
    await expect(page.getByTestId('dialog-submit')).toBeEnabled();
  }
});
