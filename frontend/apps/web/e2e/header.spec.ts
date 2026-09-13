import { expect, request, test, type Page } from '@playwright/test';

/**
 * The site header, which an administrator arranges in the admin panel.
 *
 * One test, one sign-in for the API: the panel allows ten authentications every five minutes, and
 * what matters here is a single claim — the order stored is the order a visitor reads, and the
 * words come from the app's own translations rather than from whoever did the arranging.
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

async function storedLinks(api: Awaited<ReturnType<typeof adminApi>>): Promise<HeaderLink[]> {
  const response = await api.get('admin/content/header');
  return ((await response.json()) as { links: HeaderLink[] }).links;
}

/**
 * Saves one row back, unchanged apart from the fields given.
 *
 * The response is checked here on purpose: a save the server refuses would otherwise surface much
 * later as "the header is in the wrong order", which reads like a bug in the site rather than in
 * the request this test made.
 */
async function save(
  api: Awaited<ReturnType<typeof adminApi>>,
  link: HeaderLink,
  patch: Partial<HeaderLink> = {},
) {
  const response = await api.post('admin/content/header/links', {
    data: {
      id: link.id,
      key: link.key,
      labels: link.labels ?? {},
      url: link.url,
      visibility: link.visibility,
      sortOrder: link.sortOrder,
      isActive: link.isActive,
      ...patch,
    },
  });

  expect(response.ok(), `saving ${link.key}: ${response.status()} ${await response.text()}`).toBe(
    true,
  );

  return response;
}

/** The header's own navigation, in the order it is drawn. */
async function headerItems(page: Page): Promise<string[]> {
  const nav = page.getByRole('banner').getByRole('navigation').first();
  await expect(nav.getByRole('link').first()).toBeVisible();

  return (await nav.getByRole('link').allTextContents()).map((text) => text.trim());
}

test.describe('site header', () => {
  test('is drawn in the order an administrator arranged', async ({ page }) => {
    const api = await adminApi();
    const before = await storedLinks(api);
    const knowledge = before.find((l) => l.key === 'knowledge');
    const home = before.find((l) => l.key === 'home');
    expect(knowledge, 'the header ships with a Knowledge entry').toBeTruthy();
    expect(home, 'the header ships with a Home entry').toBeTruthy();

    await page.goto('/en');

    // The entries the site ships with, in the order it ships them.
    expect(await headerItems(page)).toEqual([
      'Home',
      'How it works',
      'Services',
      'Coverage',
      'Knowledge',
      'Contact us',
    ]);

    try {
      // Swapped with Home, which is the entry currently in front. Positions are swapped rather
      // than one pushed below zero: the server refuses a negative position, and a refused save
      // would look from here like the site ignoring the arrangement.
      await save(api, knowledge!, { sortOrder: home!.sortOrder });
      await save(api, home!, { sortOrder: knowledge!.sortOrder });

      await page.reload();
      expect((await headerItems(page))[0]).toBe('Knowledge');
    } finally {
      await save(api, knowledge!);
      await save(api, home!);
    }

    await page.reload();
    expect((await headerItems(page))[0]).toBe('Home');

    // A label written in the panel replaces the app's wording for that language only.
    try {
      await save(api, knowledge!, { labels: { en: 'Guides' } });

      await page.reload();
      expect(await headerItems(page)).toContain('Guides');

      await page.goto('/ar');
      // Arabic was not written, so it keeps its own translation rather than reading "Guides".
      expect(await headerItems(page)).not.toContain('Guides');
    } finally {
      await save(api, knowledge!);
    }

    await api.dispose();
  });
});
