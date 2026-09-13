import { expect, test, type Page } from '@playwright/test';
import { choose, chooseByIndex, chooseLanguage, selectedLabel } from './support/searchable-select';
import { fillContactPerson } from './support/contact-person';

/**
 * Onboarding smoke tests: registration, sign-in and order setup, exercised in both a
 * left-to-right locale (en) and a right-to-left one (ar).
 */

const SEEDED = {
  countryName: { en: 'Egypt', ar: 'مصر' },
  currencyCode: 'EGP',
};

function uniqueEmail(prefix: string): string {
  return `${prefix}${Date.now()}${Math.floor(Math.random() * 1000)}@example.com`;
}

/** Registers a new order and returns the credentials echoed back in development mode. */
async function register(page: Page, lang: string, email: string) {
  await page.goto(`/${lang}/register`);

  await page.getByLabel(/email|البريد/i).fill(email);
  await page.locator('form button[type="submit"]').click();

  // The success screen echoes the credentials because SMTP is disabled in development.
  const orderNumber = page.locator('dd').first();
  await expect(orderNumber).toBeVisible({ timeout: 20_000 });

  const values = await page.locator('dd').allTextContents();
  return { orderNumber: values[0].trim(), password: values[1].trim() };
}

test.describe('header', () => {
  test('the logo takes you back to the top of the page', async ({ page }) => {
    await page.goto('/en');

    // The landing header links jump down the page; this is the state the mark has to undo.
    await page.locator('header a[href="/en#services"]').click();
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(200);

    await page.getByRole('link', { name: 'NEN Verification' }).first().click();

    // The scroll is smooth, so poll rather than read once.
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
    // Still on the landing page — the mark scrolls, it does not navigate somewhere else.
    await expect(page).toHaveURL(/\/en$/);
  });
});

test.describe('language and direction', () => {
  test('bare path redirects into a locale', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/(ar|en|ru|tr|uz|de|hi|zh|ja|pl)$/);
  });

  test('English renders left-to-right', async ({ page }) => {
    await page.goto('/en');
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Verify official documents');
  });

  test('Arabic renders right-to-left with translated copy', async ({ page }) => {
    await page.goto('/ar');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    // Matched on an undiacritised phrase so the assertion does not hinge on shadda placement.
    await expect(page.getByRole('heading', { level: 1 })).toContainText('المستندات الرسمية');
  });

  test('an unsupported locale falls back to the default', async ({ page }) => {
    // Urdu was dropped from the shipped set; Arabic is now the only right-to-left locale.
    await page.goto('/ur');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  });

  for (const lang of ['ru', 'de', 'hi', 'zh']) {
    test(`${lang} renders left-to-right and translated`, async ({ page }) => {
      await page.goto(`/${lang}`);
      await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
      // A missing bundle would leave the raw key visible instead of a sentence.
      await expect(page.getByRole('heading', { level: 1 })).not.toContainText('landing.title');
    });
  }

  test('switching language rewrites the URL and flips direction', async ({ page }) => {
    await page.goto('/en');
    await chooseLanguage(page, 'العربية');

    await expect(page).toHaveURL(/\/ar$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  });

  test('an unknown language segment falls back to English', async ({ page }) => {
    await page.goto('/xx');
    await expect(page).toHaveURL(/\/en$/);
  });
});

test.describe('registration and validation', () => {
  test('rejects an invalid email without calling the API', async ({ page }) => {
    await page.goto('/en/register');
    await page.getByLabel(/email/i).fill('not-an-email');
    await page.locator('form button[type="submit"]').click();

    await expect(page.getByRole('alert')).toContainText('valid email address');
  });

  test('registers and shows the credentials screen', async ({ page }) => {
    const email = uniqueEmail('e2e');
    const credentials = await register(page, 'en', email);

    // The client code prefixes the number, so the seeded NEN client yields NEN + 9 digits.
    expect(credentials.orderNumber).toMatch(/^NEN\d{9}$/);
    expect(credentials.password).toHaveLength(8);
    await expect(page.getByText(email)).toBeVisible();
  });
});

test.describe('sign-in and order setup', () => {
  test('completes the full onboarding flow in English', async ({ page }) => {
    const email = uniqueEmail('e2eflow');
    const { orderNumber, password } = await register(page, 'en', email);

    await page.getByRole('link', { name: /^next$/i }).click();

    // The deep link from the success screen prefills the order number.
    await expect(page.locator('#orderNumber')).toHaveValue(orderNumber);
    await page.locator('#password').fill(password);
    await page.locator('form button[type="submit"]').click();

    // Setup is required before the dashboard becomes reachable.
    await expect(page).toHaveURL(/\/en\/setup$/);
    await expect(page.getByRole('heading', { name: /set up your order/i })).toBeVisible();

    await choose(page, 'verificationCountryId', SEEDED.countryName.en);
    await expect(page.locator('#currencyId')).toBeEnabled();
    await chooseByIndex(page, 'currencyId', 0);
    await fillContactPerson(page);
    await page.locator('form button[type="submit"]').click();

    await expect(page).toHaveURL(/\/en\/applications$/);
  });

  test('setup collects the contact person and submits a split phone number', async ({ page }) => {
    const email = uniqueEmail('e2econtact');
    const { orderNumber, password } = await register(page, 'en', email);

    await page.goto(`/en/login?order=${orderNumber}`);
    await page.locator('#password').fill(password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/en\/setup$/);

    await choose(page, 'verificationCountryId', SEEDED.countryName.en);
    await chooseByIndex(page, 'currencyId', 0);

    // Both contact fields are required: submitting without them must not leave the page.
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/en\/setup$/);
    await expect(page.getByText("Enter the contact person's name.")).toBeVisible();
    await expect(page.getByText("Enter the contact person's phone number.")).toBeVisible();

    // The dial code follows the verification country until it is chosen by hand.
    await expect(page.locator('#contactPhoneCountry')).toContainText('+20');

    await page.locator('#contactPersonName').fill('  Mona Fahmy  ');
    // Everything but digits is dropped as it is typed, so a pasted number cannot repeat the code.
    await page.locator('#contactPhoneCountryNumber').fill('+20 (100) 123-4567');
    await expect(page.locator('#contactPhoneCountryNumber')).toHaveValue('201001234567');

    await page.locator('#contactPhoneCountryNumber').fill('1001234567');

    const submitted = page.waitForRequest(
      (request) => request.url().includes('/orders/setup') && request.method() === 'PUT',
    );
    await page.locator('form button[type="submit"]').click();

    const body = JSON.parse((await submitted).postData() ?? '{}');
    expect(body.contactPersonName).toBe('Mona Fahmy');
    expect(body.contactPersonPhoneCountry).toBe('EG');
    expect(body.contactPersonPhoneCode).toBe('+20');
    expect(body.contactPersonPhoneNumber).toBe('1001234567');

    // A 200 from a server that validates all four fields is the proof they were accepted.
    await expect(page).toHaveURL(/\/en\/applications$/);
  });

  test('the dial-code list is searchable by country name and shows a flag', async ({ page }) => {
    const email = uniqueEmail('e2edial');
    const { orderNumber, password } = await register(page, 'en', email);

    await page.goto(`/en/login?order=${orderNumber}`);
    await page.locator('#password').fill(password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/en\/setup$/);

    const trigger = page.locator('#contactPhoneCountry');
    await trigger.click();
    const listbox = page.locator('#contactPhoneCountry-listbox');
    await expect(listbox).toBeVisible();

    // The search box sits above the listbox rather than inside it, so it is reached through the
    // control's own root — a bare `getByRole` would also match the header's language search.
    const control = page.locator('div', { has: trigger }).last();

    // The rows show dial codes, so the search has to match on the country name behind them.
    await control.getByRole('searchbox').fill('Japan');
    const options = listbox.getByRole('option');
    await expect(options).toHaveCount(1);
    await expect(options.first()).toContainText('+81');
    await expect(options.first().locator('img')).toHaveAttribute(
      'src',
      'https://flagcdn.com/w20/jp.png',
    );

    await options.first().click();
    await expect(trigger).toContainText('+81');
    await expect(trigger.locator('img')).toHaveAttribute('src', 'https://flagcdn.com/w20/jp.png');
  });

  test('changing country clears the previously selected currency', async ({ page }) => {
    const email = uniqueEmail('e2ecurrency');
    const { orderNumber, password } = await register(page, 'en', email);

    await page.goto(`/en/login?order=${orderNumber}`);
    await page.locator('#password').fill(password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/en\/setup$/);

    await choose(page, 'verificationCountryId', SEEDED.countryName.en);
    await chooseByIndex(page, 'currencyId', 0);
    const chosenCurrency = await selectedLabel(page, 'currencyId').textContent();
    expect(chosenCurrency?.trim()).toBeTruthy();

    // Switching to another country must reset the dependent field rather than leave a stale
    // pairing. The control has no blank row, so the reset shows as the placeholder returning.
    await choose(page, 'verificationCountryId', 'United States');
    await expect(selectedLabel(page, 'currencyId')).not.toHaveText(chosenCurrency!.trim());
  });

  test('completes onboarding in Arabic with RTL layout throughout', async ({ page }) => {
    const email = uniqueEmail('e2ear');
    const { orderNumber, password } = await register(page, 'ar', email);

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await page.goto(`/ar/login?order=${orderNumber}`);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.locator('#password').fill(password);
    await page.locator('form button[type="submit"]').click();

    await expect(page).toHaveURL(/\/ar\/setup$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await choose(page, 'verificationCountryId', SEEDED.countryName.ar);
    await chooseByIndex(page, 'currencyId', 0);
    await fillContactPerson(page, 'جهة الاتصال');
    await page.locator('form button[type="submit"]').click();

    await expect(page).toHaveURL(/\/ar\/applications$/);
  });

  test('rejects wrong credentials with a localized message', async ({ page }) => {
    const email = uniqueEmail('e2ebad');
    const { orderNumber } = await register(page, 'en', email);

    await page.goto(`/en/login?order=${orderNumber}`);
    await page.locator('#password').fill('WrongPa1');
    await page.locator('form button[type="submit"]').click();

    await expect(page.getByRole('alert')).toContainText('do not match');
  });
});

test.describe('route guards', () => {
  test('setup is unreachable while signed out', async ({ page }) => {
    await page.goto('/en/setup');
    await expect(page).toHaveURL(/\/en\/login$/);
  });

  test('the dashboard is unreachable while signed out', async ({ page }) => {
    await page.goto('/en/applications');
    await expect(page).toHaveURL(/\/en\/login$/);
  });

  test('an unknown path renders the not-found page', async ({ page }) => {
    await page.goto('/en/does-not-exist');
    await expect(page.getByRole('heading', { name: /page not found/i })).toBeVisible();
  });
});
