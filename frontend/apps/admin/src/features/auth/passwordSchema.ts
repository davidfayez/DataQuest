import type { TFunction } from 'i18next';
import { z } from 'zod';

/**
 * The client-side mirror of the server's administrator password policy. The server validates the
 * same rules regardless — this exists so the message appears as the admin types, not only after a
 * round trip. If the policy changes, both sides must change together.
 */
export function passwordSchema(t: TFunction) {
  return z
    .string()
    .min(8, t('password.tooShort'))
    .max(128)
    .regex(/[A-Z]/, t('password.needUpper'))
    .regex(/[a-z]/, t('password.needLower'))
    .regex(/[0-9]/, t('password.needDigit'));
}
