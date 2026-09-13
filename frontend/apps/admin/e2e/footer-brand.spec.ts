import { expect, request, test, type Page } from '@playwright/test';

/**
 * Footer → Brand block: adding and editing the marks beside the subtitle.
 *
 * A mark is a row plus a file, and the file needs a row to belong to, so saving one is two calls to
 * the API with a moment in between. That moment is what this file is about. It used to be visible
 * and permanent — the row appeared in the list as soon as it was created, and if the upload then
 * failed it stayed there for ever as a mark "needing an image", left behind by a save the operator
 * had watched fail. The rule now is that a mark is saved whole or not at all.
 *
 * Two tests rather than eight, each signing in once: the panel allows ten authentications every
 * five minutes, and a test per assertion would spend that budget on this one file.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };
const API = 'http://localhost:5088/api/v1/';

/** A real 1x1 PNG, so the server's signature check sees genuine PNG bytes. */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

/** Unique per run, so a mark left by an earlier run can neither pass nor fail this one. */
const RUN = Date.now();
const name = (stem: string) => `${stem}-${RUN}.png`;

/** One sign-in for every count check in the file, for the same rate-limit reason. */
let apiContext: Promise<import('@playwright/test').APIRequestContext> | null = null;

function adminApi() {
  apiContext ??= (async () => {
    const context = await request.newContext({ baseURL: API });
    const login = await context.post('admin/auth/login', { data: SUPER_ADMIN });
    const { accessToken } = (await login.json()) as { accessToken: string };

    return request.newContext({
      baseURL: API,
      extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
    });
  })();

  return apiContext;
}

/** What the server holds, so the panel cannot vouch for itself. */
async function markCount(): Promise<number> {
  const response = await (await adminApi()).get('admin/content/footer');
  return (((await response.json()).logos ?? []) as unknown[]).length;
}

async function openBrandBlock(page: Page) {
  await page.goto('/login');
  await page.locator('#identifier').fill(SUPER_ADMIN.email);
  await page.locator('#password').fill(SUPER_ADMIN.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/$/);

  const group = page.getByTestId('nav-group-footer');
  if ((await group.getAttribute('aria-expanded')) === 'false') await group.click();
  await page.getByTestId('nav-footer-brand').click();
  await expect(page.getByTestId('new-logo')).toBeVisible();
}

/**
 * Picks a file the way an operator does — through the button, not by writing to the hidden input.
 *
 * This matters more than it looks. The button lives inside the dialog's form, and a button in a
 * form submits it unless it says otherwise; when it did, choosing an image saved an imageless mark
 * before the file had even been picked. Setting the input directly bypasses the button and sees
 * none of that.
 */
async function chooseFile(page: Page, fileName: string) {
  const chooser = page.waitForEvent('filechooser');
  await page.getByTestId('choose-logo').click();

  await (await chooser).setFiles({
    name: fileName,
    mimeType: 'image/png',
    buffer: PNG,
  });
}

function rowFor(page: Page, fileName: string) {
  return page.getByTestId('logo-list').getByRole('listitem').filter({ hasText: fileName });
}

async function deleteRow(page: Page, fileName: string) {
  const row = rowFor(page, fileName);
  await row.getByLabel('Delete').click();
  await page.getByTestId('confirm-submit').click();
  await expect(row).toHaveCount(0);
}

test.describe('footer brand block', () => {
  test('a mark is saved whole, or not at all', async ({ page }) => {
    await openBrandBlock(page);
    const before = await markCount();

    // --- choosing a file puts nothing in the list ---------------------------
    const dismissed = name('dismissed');
    await page.getByTestId('new-logo').click();
    await chooseFile(page, dismissed);

    await expect(page.getByTestId('logo-preview')).toBeVisible();
    await expect(page.getByTestId('logo-list')).not.toContainText(dismissed);

    // --- and dismissing it leaves nothing behind ---------------------------
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toBeHidden();
    expect(await markCount()).toBe(before);

    // A file remembered from a dismissed dialog would be saved by somebody who thought they were
    // creating a different mark.
    await page.getByTestId('new-logo').click();
    await expect(page.getByTestId('logo-preview')).toHaveCount(0);
    await expect(page.getByTestId('dialog-submit')).toBeDisabled();
    await page.getByRole('button', { name: /cancel/i }).click();

    // --- a save whose upload fails leaves nothing behind either -------------
    const doomed = name('doomed');
    const isUpload = (url: URL) =>
      url.pathname.includes('/footer/logos/') && url.pathname.endsWith('/image');

    // Only the upload is broken. Matching on the method too keeps the GETs that draw the list's
    // thumbnails working, so this fails for the reason it says it does.
    let aborted = 0;
    await page.route(isUpload, (route) => {
      if (route.request().method() !== 'POST') return route.continue();
      aborted += 1;
      return route.abort('failed');
    });

    await page.getByTestId('new-logo').click();
    await chooseFile(page, doomed);

    const attempted = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/image'));
    await page.getByTestId('dialog-submit').click();
    await attempted;

    // The dialog stays open reporting the failure, rather than closing on a mark that is half made.
    await expect(page.getByRole('dialog').getByRole('alert')).toBeVisible();
    await expect(page.getByTestId('dialog-submit')).toBeEnabled();

    await page.unroute(isUpload);
    expect(aborted, 'the upload was never actually broken').toBeGreaterThan(0);

    expect(await markCount()).toBe(before);
    await page.getByRole('button', { name: /cancel/i }).click();
    await expect(rowFor(page, doomed)).toHaveCount(0);
    await expect(page.getByTestId('logo-list')).not.toContainText(/no image yet/i);

    // --- a save that works adds exactly one row, with its image -------------
    const saved = name('saved');
    await page.getByTestId('new-logo').click();
    await chooseFile(page, saved);
    await page.getByTestId('dialog-submit').click();

    const row = rowFor(page, saved);
    await expect(row).toBeVisible();
    await expect(row).toContainText(/active/i);
    await expect(row).not.toContainText(/needs an image/i);
    await expect(row.locator('img')).toBeVisible();
    expect(await markCount()).toBe(before + 1);

    await deleteRow(page, saved);
    expect(await markCount()).toBe(before);
  });

  test('a replacement only takes effect when it is saved', async ({ page }) => {
    await openBrandBlock(page);
    const before = await markCount();

    const original = name('original');
    const replacement = name('replacement');

    await page.getByTestId('new-logo').click();
    await chooseFile(page, original);
    await page.getByTestId('dialog-submit').click();
    await expect(rowFor(page, original)).toBeVisible();

    // Picked, previewed, then abandoned: the stored mark is the one it was saved with.
    await rowFor(page, original).getByLabel('Edit').click();
    await chooseFile(page, replacement);
    await expect(page.getByRole('dialog')).toContainText(replacement);
    await page.getByRole('button', { name: /cancel/i }).click();

    await expect(rowFor(page, original)).toBeVisible();
    await expect(rowFor(page, replacement)).toHaveCount(0);

    // Picked and saved: it replaces the file rather than adding a second mark.
    await rowFor(page, original).getByLabel('Edit').click();
    await chooseFile(page, replacement);
    await page.getByTestId('dialog-submit').click();

    await expect(rowFor(page, replacement)).toBeVisible();
    await expect(rowFor(page, original)).toHaveCount(0);
    expect(await markCount()).toBe(before + 1);

    await deleteRow(page, replacement);
    expect(await markCount()).toBe(before);
  });
});
