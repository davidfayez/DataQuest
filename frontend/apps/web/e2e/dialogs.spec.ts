import { expect, test, type Page } from '@playwright/test';
import { choose, chooseByIndex } from './support/searchable-select';
import { fillContactPerson } from './support/contact-person';

/**
 * Every <dialog> on a page must stay shut until something opens it.
 *
 * This exists because it once did not. The shared Dialog put `display: flex` on the <dialog>
 * element to pin its footer, and a `display` from an author stylesheet overrides the user-agent's
 * `dialog:not([open]) { display: none }` outright — origin beats specificity, so the rule that
 * hides a closed dialog simply stopped applying. Every confirmation prompt on the page rendered
 * inline, including "Refund this application?" reading "EGP 0.00 will be returned to your wallet",
 * on a page where nobody had asked to refund anything.
 *
 * A screenshot of the whole page is what surfaced it, so the assertion here is the one a screenshot
 * makes: does this element occupy space. Asserting on the class list instead would have passed —
 * the classes were exactly as intended.
 */

const SEEDED = {
  countryEn: 'Egypt',
};

/** Every dialog that is rendering, and the text it is showing. Empty is the only healthy answer. */
async function visibleDialogs(page: Page): Promise<string[]> {
  return page.locator('dialog').evaluateAll((nodes) =>
    nodes
      .filter((node) => {
        if (node.hasAttribute('open')) return false;
        const rect = node.getBoundingClientRect();
        return rect.width > 0 || rect.height > 0;
      })
      .map((node) => (node.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 120)),
  );
}

async function signInWithReadyOrder(page: Page) {
  const email = `dialogs${Date.now()}${Math.floor(Math.random() * 1000)}@example.com`;

  await page.goto('/en/register');
  await page.getByLabel(/email/i).fill(email);
  await page.locator('form button[type="submit"]').click();
  await expect(page.locator('dd').first()).toBeVisible({ timeout: 20_000 });

  const [orderNumber, password] = (await page.locator('dd').allTextContents()).map((v) => v.trim());

  await page.goto(`/en/login?order=${orderNumber}`);
  await page.locator('#password').fill(password!);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/en\/setup$/);

  await choose(page, 'verificationCountryId', SEEDED.countryEn);
  await chooseByIndex(page, 'currencyId', 0);
  await fillContactPerson(page);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/en\/applications$/);
}

test('no closed dialog renders on the pages that carry them', async ({ page }) => {
  await signInWithReadyOrder(page);

  for (const path of ['/en/applications', '/en/wallet', '/en/wallet/requests']) {
    await page.goto(path);
    await page.waitForLoadState('networkidle');

    expect(await visibleDialogs(page), `a closed dialog is rendering on ${path}`).toEqual([]);
  }
});

test('opening a dialog still works, and keeps its actions in view', async ({ page }) => {
  await signInWithReadyOrder(page);

  await page.goto('/en/wallet');
  await page.waitForLoadState('networkidle');

  await page.getByTestId('wallet-deposit').click();

  const dialog = page.locator('dialog[open]');
  await expect(dialog).toBeVisible();

  // Choosing a type is what reaches the confirm step, which is the longest the form ever gets and
  // therefore the one whose actions used to scroll out of reach.
  const types = page.locator('[data-testid^="payment-type-"]');
  if ((await types.count()) > 0) {
    await types.first().locator('xpath=ancestor::label').click();

    const submit = page.getByTestId('confirm-deposit');
    await expect(submit).toBeVisible();
    expect(
      await submit.evaluate((el) => {
        const rect = el.getBoundingClientRect();
        return rect.top >= 0 && rect.bottom <= window.innerHeight;
      }),
      'the submit button must sit inside the viewport, not below the fold',
    ).toBe(true);
  }

  // And closing it puts it away again.
  await page.getByTestId('deposit-back').click();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  expect(await visibleDialogs(page)).toEqual([]);
});
