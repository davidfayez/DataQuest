import { expect, request, test } from '@playwright/test';

/**
 * Application wizard: the headings above the steps of the applicant's New application form.
 *
 * One test, one sign-in: the panel allows ten authentications every five minutes, and this page is
 * a handful of text boxes that either reach the applicant's form or do not.
 *
 * What it has to prove is that the copy leaves the panel — the public endpoint the web app reads is
 * checked directly, not the boxes the page has just filled in for itself.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };
const API = 'http://localhost:5088/api/v1/';

/** Unique per run, so a leftover from an earlier run can neither pass nor fail this one. */
const TITLE = `Who is this for? (${Date.now()})`;

async function adminApi() {
  const context = await request.newContext({ baseURL: API });
  const login = await context.post('admin/auth/login', { data: SUPER_ADMIN });
  const { accessToken } = (await login.json()) as { accessToken: string };

  return request.newContext({
    baseURL: API,
    extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
  });
}

/** What the applicant's wizard would draw for a step, in English. */
async function publicTitle(step: string): Promise<string | null> {
  const context = await request.newContext({ baseURL: API });
  const response = await context.get('content/wizard', {
    headers: { 'Accept-Language': 'en' },
  });

  const body = (await response.json()) as {
    steps: Record<string, { title: string | null } | undefined>;
  };

  await context.dispose();
  return body.steps[step]?.title ?? null;
}

test.describe('application wizard content', () => {
  test('a step heading typed here is what the applicant reads', async ({ page }) => {
    const api = await adminApi();
    const original = await publicTitle('addressee');
    expect(original, 'the wizard ships with seeded copy').toBeTruthy();

    await page.goto('/login');
    await page.locator('#identifier').fill(SUPER_ADMIN.email);
    await page.locator('#password').fill(SUPER_ADMIN.password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/$/);

    await page.getByTestId('nav-wizard-content').click();

    // The panel opens on the copy that is live, rather than on empty boxes.
    await expect(page.getByTestId('wizard-addressee-title')).toHaveValue(original!);

    await page.getByTestId('wizard-addressee-title').fill(TITLE);
    await page.getByTestId('save-addressee').click();
    await expect(page.getByTestId('lookup-notice')).toBeVisible();

    expect(await publicTitle('addressee')).toBe(TITLE);

    // Emptying a box is not an empty heading: the step goes back to the app's own wording, which
    // is the only way an operator can undo a change they did not want.
    await page.getByTestId('wizard-addressee-title').fill('');
    await page.getByTestId('save-addressee').click();
    await expect.poll(() => publicTitle('addressee')).toBeNull();

    await api.put('admin/content/wizard/heading', {
      data: { step: 'addressee', title: { en: original } },
    });
    expect(await publicTitle('addressee')).toBe(original);

    await api.dispose();
  });
});
