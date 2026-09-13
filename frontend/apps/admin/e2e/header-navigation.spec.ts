import { expect, request, test } from '@playwright/test';

/**
 * Header navigation: the page that arranges the site's top bar.
 *
 * One test, one sign-in — the panel allows ten authentications every five minutes. What it has to
 * prove is that the arrows write the order through to the public endpoint the web app reads, not
 * merely that the list on screen redraws.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };
const API = 'http://localhost:5088/api/v1/';

interface HeaderLink {
  id: string;
  key: string | null;
  labels?: Record<string, string>;
  url: string;
  visibility: string;
  sortOrder: number;
  isActive: boolean;
}

async function adminApi() {
  const context = await request.newContext({ baseURL: API });
  const login = await context.post('admin/auth/login', {
    data: { usernameOrEmail: SUPER_ADMIN.email, password: SUPER_ADMIN.password },
  });
  const { accessToken } = (await login.json()) as { accessToken: string };

  return request.newContext({
    baseURL: API,
    extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
  });
}

/** The keys the public endpoint reports, in the order the site would draw them. */
async function publicOrder(): Promise<(string | null)[]> {
  const context = await request.newContext({ baseURL: API });
  const response = await context.get('content/header', {
    headers: { 'Accept-Language': 'en' },
  });

  const body = (await response.json()) as { links: { key: string | null }[] };
  await context.dispose();

  return body.links.map((link) => link.key);
}

test.describe('header navigation', () => {
  test('the arrows change the order the site draws', async ({ page }) => {
    const api = await adminApi();
    const before = await publicOrder();
    expect(before.slice(0, 2)).toEqual(['home', 'how']);

    await page.goto('/login');
    await page.locator('#identifier').fill(SUPER_ADMIN.email);
    await page.locator('#password').fill(SUPER_ADMIN.password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/$/);

    await page.getByTestId('nav-header-navigation').click();
    await expect(page.getByTestId('header-link-home')).toBeVisible();

    try {
      // Home moves one place later, which puts "How it works" first.
      await page.getByTestId('move-down-home').click();
      await expect(page.getByTestId('lookup-notice')).toBeVisible();

      await expect.poll(async () => (await publicOrder()).slice(0, 2)).toEqual(['how', 'home']);
    } finally {
      const response = await api.get('admin/content/header');
      const links = ((await response.json()) as { links: HeaderLink[] }).links;

      // Put the shipped arrangement back, whatever the test left behind.
      for (const [index, key] of ['home', 'how', 'services', 'coverage', 'knowledge', 'contact'].entries()) {
        const link = links.find((l) => l.key === key);
        if (!link) continue;

        await api.post('admin/content/header/links', {
          data: {
            id: link.id,
            key: link.key,
            labels: link.labels ?? {},
            url: link.url,
            visibility: link.visibility,
            sortOrder: index,
            isActive: link.isActive,
          },
        });
      }
    }

    expect((await publicOrder()).slice(0, 2)).toEqual(['home', 'how']);
    await api.dispose();
  });
});
