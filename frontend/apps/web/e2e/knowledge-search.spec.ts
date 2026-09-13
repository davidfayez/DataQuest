import { expect, test } from '@playwright/test';

/**
 * The search box above the tools and guides.
 *
 * "DQ Knowledge" runs against the real API, because it searches this platform's own published
 * guides and nothing else is needed to exercise it.
 *
 * "AI Mode" is split. Its two silent states — no key configured, no answer — are checked live,
 * since those are exactly what an unconfigured deployment returns. Its web links cannot be: Google
 * Search grounding is billed separately from the model, a free-tier key is refused for it, and the
 * assistant then answers from the guides alone. So the response is stubbed for the part the site
 * is responsible for — that links render, point where they claim, and open without handing the
 * opener away.
 */

const ASK = '**/api/v1/content/knowledge/ask';

test.describe('DQ Knowledge', () => {
  test('finds a published guide and shows why it matched', async ({ page }) => {
    await page.goto('/en/tools');

    // Seeded: "How to create an order", described as a walkthrough of registration.
    await page.getByTestId('knowledge-input').fill('registration');
    await page.getByTestId('search-knowledge').click();

    const results = page.getByTestId('knowledge-results');
    await expect(results).toBeVisible();
    await expect(results).toContainText(/registration/i);
  });

  test('says so when nothing matches, and leaves no stale results', async ({ page }) => {
    await page.goto('/en/tools');

    await page.getByTestId('knowledge-input').fill('registration');
    await page.getByTestId('search-knowledge').click();
    await expect(page.getByTestId('knowledge-results')).toBeVisible();

    await page.getByTestId('knowledge-input').fill('zzzznotathinganywhere');
    await page.getByTestId('search-knowledge').click();

    await expect(page.getByText(/nothing in the guides/i)).toBeVisible();
    await expect(page.getByTestId('knowledge-results')).toHaveCount(0);
  });

  test('neither button does anything until something is typed', async ({ page }) => {
    await page.goto('/en/tools');

    await expect(page.getByTestId('search-knowledge')).toBeDisabled();
    await expect(page.getByTestId('search-ai')).toBeDisabled();

    await page.getByTestId('knowledge-input').fill('order');
    await expect(page.getByTestId('search-knowledge')).toBeEnabled();
    await expect(page.getByTestId('search-ai')).toBeEnabled();
  });
});

test.describe('AI Mode', () => {
  test('reports plainly when the assistant is not configured', async ({ page }) => {
    await page.route(ASK, (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ answer: '', isConfigured: false, sources: [], webSources: [] }),
      }),
    );

    await page.goto('/en/tools');
    await page.getByTestId('knowledge-input').fill('How do I pay?');
    await page.getByTestId('search-ai').click();

    // A visitor cannot fix a missing API key, so this is explained rather than shown as an error.
    await expect(page.getByText(/not switched on/i)).toBeVisible();
    await expect(page.getByTestId('knowledge-results')).toHaveCount(0);
  });

  test('web sources render as safe outbound links', async ({ page }) => {
    await page.route(ASK, (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          answer: 'An apostille authenticates a public document for use abroad.',
          isConfigured: true,
          sources: [],
          webSources: [
            { title: 'Apostille Convention — HCCH', url: 'https://www.hcch.net/en/instruments' },
            { title: 'Authentications — U.S. Department of State', url: 'https://travel.state.gov/records' },
          ],
        }),
      }),
    );

    await page.goto('/en/tools');
    await page.getByTestId('knowledge-input').fill('What is an apostille?');
    await page.getByTestId('search-ai').click();

    const links = page.getByTestId('ai-web-sources').getByRole('link');
    await expect(links).toHaveCount(2);

    const first = links.first();
    await expect(first).toHaveAttribute('href', /hcch\.net/);
    // Somebody else's page: a new tab, and no window.opener handed to it.
    await expect(first).toHaveAttribute('target', '_blank');
    await expect(first).toHaveAttribute('rel', /noopener/);
    await expect(first).toHaveAttribute('rel', /noreferrer/);

    // The host is shown so a reader can judge a link before clicking, with "www." dropped.
    await expect(page.getByTestId('ai-web-sources')).toContainText('hcch.net');
    await expect(page.getByTestId('ai-web-sources')).not.toContainText('www.hcch.net');
  });

  test('answering from the guides alone draws no empty web section', async ({ page }) => {
    await page.route(ASK, (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          answer: 'Answered from the guides alone.',
          isConfigured: true,
          sources: [{ id: '1', kind: 'Video', name: 'How to create an order', snippet: 'A walkthrough.', videoUrl: null, imageUrl: null }],
          webSources: [],
        }),
      }),
    );

    await page.goto('/en/tools');
    await page.getByTestId('knowledge-input').fill('anything');
    await page.getByTestId('search-ai').click();

    await expect(page.getByTestId('ai-answer')).toBeVisible();
    // The common case, not a failure — it gets no heading over an empty list.
    await expect(page.getByTestId('ai-web-sources')).toHaveCount(0);
    await expect(page.getByText(/answered from these guides/i)).toBeVisible();
  });
});
