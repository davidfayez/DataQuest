import { type Page } from '@playwright/test';

/**
 * Order setup also asks who to contact about the order. The dial code defaults to the verification
 * country, so only the name and the national number have to be typed — every spec that completes
 * setup goes through here so the shape of that form lives in one place.
 */
export async function fillContactPerson(
  page: Page,
  name = 'E2E Contact',
  number = '1001234567',
): Promise<void> {
  await page.locator('#contactPersonName').fill(name);
  await page.locator('#contactPhoneCountryNumber').fill(number);
}
