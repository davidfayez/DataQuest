import { expect, request, test } from '@playwright/test';

/**
 * Password resets → the log, and the details behind each row.
 *
 * The table can only ever show a handful of columns, and the point of the log is everything else:
 * whose network the request came from, whether it was a VPN or a data centre, where the address
 * geolocates to. That lives in the details dialog, so this file is mostly about the dialog.
 *
 * One sign-in for the file: the panel allows ten authentications every five minutes, and a test
 * per assertion would spend that budget here.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };
const API = 'http://localhost:5088/api/v1/';

/**
 * A real, public, geolocatable address, sent as X-Forwarded-For so the API sees it instead of
 * loopback. Without it every location field is empty and the dialog proves nothing.
 */
const PUBLIC_IP = '84.54.71.1';

const RUN = Date.now();

/** Registers an order and asks for a new password from a recognisable browser and address. */
async function requestAReset(): Promise<string> {
  const context = await request.newContext({ baseURL: API });

  const registration = await context.post('orders/register', {
    data: {
      email: `reset-e2e-${RUN}@example.com`,
      fullName: 'Reset E2E',
      phone: '+998908227561',
      languageCode: 'en',
      countryCode: 'UZ',
    },
  });

  const { orderNumber } = (await registration.json()) as { orderNumber: string };

  await context.post('orders/forgot-password', {
    data: { orderNumber },
    headers: {
      'X-Forwarded-For': PUBLIC_IP,
      'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
        + 'Chrome/141.0.0.0 Safari/537.36',
      'Accept-Language': 'en-GB,en;q=0.9',
    },
  });

  return orderNumber;
}

test('the log lists a reset and its details say where it came from', async ({ page }) => {
  const orderNumber = await requestAReset();

  await page.goto('/login');
  await page.locator('#identifier').fill(SUPER_ADMIN.email);
  await page.locator('#password').fill(SUPER_ADMIN.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/$/);

  await page.goto('/password-resets');
  await page.getByTestId('table-search').fill(orderNumber);

  const outcome = page.getByTestId(`outcome-${orderNumber}`);
  await expect(outcome).toBeVisible({ timeout: 15_000 });

  // Nobody has signed in with it, so it is an offer that still stands.
  await expect(outcome).toHaveText(/waiting/i);

  await page.getByTestId(`details-${orderNumber}`).click();

  const details = page.getByTestId('reset-details');
  await expect(details).toBeVisible();

  // The address itself, and the things only a lookup can add.
  await expect(details).toContainText(PUBLIC_IP);
  await expect(details).toContainText(/uzbek/i);
  await expect(details).toContainText('Asia/Samarkand');

  // Read out of the request rather than looked up, so these hold even if the provider is down.
  await expect(details).toContainText('Chrome 141');
  await expect(details).toContainText('Windows 10 or 11');
  await expect(details).toContainText('en-GB,en;');

  // The local part keeps its ends, the domain keeps only its tail: da*****93@**mail.com. The
  // mailbox is recognisable to whoever owns it and close to useless to anybody else.
  await expect(details).toContainText(/\*{5}.*@\*{2}[^@*\s]+\./);
  await expect(details).not.toContainText('example.com');
});
