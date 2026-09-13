import { expect, test, type Page } from '@playwright/test';

/**
 * Sorting, across the admin lists.
 *
 * The bug these guard against: sorting used to run in the browser over the rows already fetched.
 * A table shows one page of twenty-five out of however many rows exist, so clicking a header
 * reordered that page and nothing else — asking for countries Z-first gave you the last of the
 * A's. Sorting now happens in the database, over the whole list.
 */

const SUPER_ADMIN = { email: 'admin@dataverification.local', password: 'Admin#12345' };

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#identifier').fill(SUPER_ADMIN.email);
  await page.locator('#password').fill(SUPER_ADMIN.password);
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/$/);
}

/**
 * The first row's text, once the table holds real data.
 *
 * The loading state is a single cell spanning the table, so a row with more than one cell is a
 * data row. Waiting on the word "Loading" instead races: the text can be read in the gap between
 * the check and the next render.
 */
async function firstRowText(page: Page): Promise<string> {
  const row = page.locator('tbody tr').first();
  let text = '';

  // The polled value is the one returned, so the text cannot change between the check and the
  // read — which it does, because sorting refetches and the table blinks back to its loading row.
  await expect
    .poll(async () => (text = (await row.textContent()) ?? ''), { timeout: 20_000 })
    .not.toContain('Loading');

  return text;
}

/** The first row once it differs from `previous` — i.e. once a refetch has actually landed. */
async function firstRowAfterChange(page: Page, previous: string): Promise<string> {
  const row = page.locator('tbody tr').first();
  let text = '';

  // Both conditions in one poll: "Loading…" also differs from the previous row, so waiting only
  // for a change stops on the placeholder the refetch puts there.
  await expect
    .poll(
      async () => {
        text = (await row.textContent()) ?? '';
        return text !== previous && text !== '' && !text.includes('Loading');
      },
      { timeout: 20_000 },
    )
    .toBe(true);

  return text;
}

/** Clicks a column header by its visible name. */
function header(page: Page, name: RegExp) {
  return page.getByRole('columnheader').filter({ hasText: name }).first();
}

test('sorting a multi-page list reorders the whole list, not just the page on screen', async ({
  page,
}) => {
  await signIn(page);
  await page.goto('/lookups/countries');

  const ascending = await firstRowText(page);

  const name = header(page, /name \(en\)|english/i);
  await name.click();
  await name.click();

  const descending = await firstRowAfterChange(page, ascending);

  // There are ~190 countries over eight pages. Sorting the page on screen could only ever have
  // produced something from the front of the alphabet.
  expect(descending).toMatch(/Zimbabwe|Zambia/);
  expect(descending).not.toMatch(/Afghanistan|Albania|Brunei/);

  // And back again, which is the click most likely to leave a list stuck.
  await name.click();
  expect(await firstRowAfterChange(page, descending)).toBe(ascending);
});

test('the first click on a heading sorts ascending, even while the list is still loading', async ({
  page,
}) => {
  await signIn(page);

  // Hold the list's first response until the heading has been clicked, so the click is certain
  // to land while the table has no rows. That is the state in which the table used to guess the
  // direction from an empty row set, pick descending, and leave the second click to clear the
  // sort instead of reversing it. Left to chance, the click usually lands after the rows arrive
  // and the test passes whether or not the bug is there.
  let release!: () => void;
  const clicked = new Promise<void>((resolve) => {
    release = resolve;
  });
  let held = false;

  await page.route('**/api/v1/admin/lookups/addressees**', async (route) => {
    if (!held && !route.request().url().includes('sortBy=')) {
      held = true;
      await clicked;
    }
    await route.continue();
  });

  const firstSortedRequest = page.waitForRequest((request) => request.url().includes('sortBy='));

  await page.goto('/lookups/addressees');

  const heading = header(page, /name \(en\)|english/i);
  await expect(heading).toBeVisible();
  await expect(page.locator('tbody tr').first()).toContainText('Loading');

  await heading.click();
  release();

  const url = new URL((await firstSortedRequest).url());
  expect(url.searchParams.get('sortBy')).toBe('nameEn');
  expect(url.searchParams.get('sortDescending')).not.toBe('true');
});

test('sorting returns to the first page', async ({ page }) => {
  await signIn(page);
  await page.goto('/lookups/countries');
  await firstRowText(page);

  // Walk to a later page, then sort. The row that sorts first belongs on page one, so staying on
  // page four would show the reader a slice with no relation to what they asked for.
  const next = page.getByRole('button', { name: 'Next', exact: true });
  await next.click();
  await firstRowText(page);

  const name = header(page, /name \(en\)|english/i);
  await name.click();

  await firstRowText(page);
  await expect(page.getByRole('button', { name: 'Previous', exact: true })).toBeDisabled();
});

/**
 * One list per shape of query, rather than all twenty-one: they share the table, the parameters
 * and the server-side ordering, so a break shows up in any of them.
 */
const LISTS = [
  { name: 'currencies', url: '/lookups/currencies', column: /name \(en\)|english/i },
  { name: 'addressees', url: '/lookups/addressees', column: /name \(en\)|english/i },
  { name: 'clients', url: '/clients', column: /name|code/i },
  { name: 'banks', url: '/banks', column: /name \(en\)|english/i },
];

for (const list of LISTS) {
  test(`${list.name} sorts on the server`, async ({ page }) => {
    await signIn(page);
    await page.goto(list.url);

    const rows = page.locator('tbody tr');
    await expect(rows.first()).not.toContainText('Loading', { timeout: 20_000 });

    // A list with one row proves nothing about ordering; skip rather than pass vacuously.
    // The table can pass through its empty state between loading and data, so a count taken
    // the instant the loading row goes can be 0 or 1 for a long list. Wait for rows first, and
    // only call the list short if they never come.
    const count = await expect
      .poll(() => rows.count(), { timeout: 5_000 })
      .toBeGreaterThan(1)
      .then(
        () => rows.count(),
        () => rows.count(),
      );
    test.skip(count < 2, `${list.name} has too few rows to order`);

    const ascending = await firstRowText(page);

    const column = header(page, list.column);
    await column.click();
    await column.click();

    // Waiting for the row to change is the assertion: if the sort never reached the server, the
    // list never reorders and this times out.
    await firstRowAfterChange(page, ascending);
  });
}
