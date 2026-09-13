import { expect, type Locator, type Page } from '@playwright/test';

/**
 * The applicant app renders its long option lists — countries, currencies, the whole verification
 * cascade, the language switcher — with `SearchableSelect`: a button that opens a listbox, not a
 * native `<select>`. Playwright's `selectOption` only drives a real `<select>`, so every one of
 * these has to be opened and clicked instead.
 *
 * Everything here scopes options to the control's own listbox. A bare `getByRole('option')` also
 * matches the header's language list, which is open somewhere on nearly every page.
 */

/** The trigger for a control rendered with `id`, e.g. `verificationCountryId`. */
export function trigger(page: Page, id: string): Locator {
  return page.locator(`#${id}`);
}

/** Opens the control and returns its listbox, waiting for the options to be on screen. */
async function open(control: Locator): Promise<Locator> {
  const page = control.page();

  // Already open (a previous step left it that way) — reuse it rather than toggling it shut.
  if ((await control.getAttribute('aria-expanded')) !== 'true') {
    await control.click();
  }

  const listboxId = await control.getAttribute('aria-controls');
  const listbox = page.locator(`#${listboxId}`);
  await expect(listbox).toBeVisible();
  return listbox;
}

/** Picks the option whose visible label matches, by its trigger's `id`. */
export async function choose(page: Page, id: string, name: string | RegExp): Promise<void> {
  await chooseIn(trigger(page, id), name);
}

/** Picks the option whose visible label matches, for a control located some other way. */
export async function chooseIn(control: Locator, name: string | RegExp): Promise<void> {
  const listbox = await open(control);
  await listbox.getByRole('option', { name, exact: false }).first().click();
  await expect(control).toHaveAttribute('aria-expanded', 'false');
}

/**
 * Picks the option at `index`, for the cases where the test only cares that *something* valid was
 * chosen. Unlike a native `<select>` there is no blank first row, so 0 is the first real option.
 */
export async function chooseByIndex(page: Page, id: string, index = 0): Promise<void> {
  const control = trigger(page, id);
  const listbox = await open(control);
  await listbox.getByRole('option').nth(index).click();
  await expect(control).toHaveAttribute('aria-expanded', 'false');
}

/** The label the trigger currently shows — the stand-in for reading a `<select>`'s value. */
export function selectedLabel(page: Page, id: string): Locator {
  return trigger(page, id).locator('span').first();
}

/** Switches the interface language through the header control. */
export async function chooseLanguage(page: Page, name: string | RegExp): Promise<void> {
  await chooseIn(page.getByRole('button', { name: /change language|تغيير اللغة/i }), name);
}
