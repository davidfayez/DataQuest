import { type Page } from '@playwright/test';

/**
 * The wizard's personal step also asks how the applicant is reached. The dial code opens on the
 * default, so only the address and the national number have to be typed — every spec that gets
 * past step 2 goes through here so the shape of that form lives in one place.
 */
export async function fillApplicantContact(
  page: Page,
  email = 'applicant@example.com',
  number = '1005550101',
): Promise<void> {
  await page.locator('#applicantEmail').fill(email);
  await page.locator('#applicantPhoneCountryNumber').fill(number);
}
