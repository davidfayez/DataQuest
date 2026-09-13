import { z } from 'zod';
import type { TFunction } from 'i18next';
import { NameLanguageType } from '@/entities/application/types';

/**
 * One schema per step, built with the active translator so every message is localized. The server
 * re-validates all of this — these schemas exist to give immediate, in-language feedback, not to
 * be the enforcement point.
 */

export function addresseeSchema(t: TFunction) {
  return z.object({
    addressedTo: z.string().trim().min(1, t('wizard.validation.addressedToRequired')).max(500),
  });
}

function nameSchema(t: TFunction) {
  return z.object({
    languageType: z.nativeEnum(NameLanguageType),
    // First and last are mandatory in both scripts; the middle name is optional throughout.
    firstName: z.string().trim().min(1, t('wizard.validation.firstNameRequired')).max(100),
    middleName: z.string().trim().max(100).optional().or(z.literal('')),
    lastName: z.string().trim().min(1, t('wizard.validation.lastNameRequired')).max(100),
  });
}

export function personalSchema(t: TFunction) {
  return z.object({
    arabicName: nameSchema(t),
    englishName: nameSchema(t),
    birthDate: z
      .string()
      .min(1, t('wizard.validation.birthDateRequired'))
      .refine((value) => new Date(value) < new Date(), t('wizard.validation.birthDatePast')),
    email: z
      .string()
      .trim()
      .min(1, t('wizard.validation.emailRequired'))
      .email(t('wizard.validation.emailInvalid'))
      .max(320),
    // The dial prefix is carried apart from the national number, so `+1` cannot be mistaken
    // between the countries that share it.
    phoneCountry: z.string().min(1, t('wizard.validation.phoneRequired')),
    phoneNumber: z
      .string()
      .trim()
      .min(1, t('wizard.validation.phoneRequired'))
      .regex(/^\d{4,15}$/, t('wizard.validation.phoneInvalid')),
  });
}

export function detailsSchema(t: TFunction) {
  return z.object({
    transactionTypeId: z.string().min(1, t('wizard.validation.transactionTypeRequired')),
    subTransactionTypeId: z.string().min(1, t('wizard.validation.subTransactionTypeRequired')),
    verificationAuthorityId: z.string().min(1, t('wizard.validation.authorityRequired')),
    services: z
      .array(
        z.object({
          serviceTypeId: z.string().min(1, t('wizard.validation.serviceTypeRequired')),
          quantity: z.coerce.number().int().min(1, t('wizard.validation.quantityMin')),
          languageCode: z.string().min(1),
          isExpress: z.boolean(),
        }),
      )
      .min(1, t('wizard.validation.serviceRequired')),
  });
}

export type AddresseeValues = z.infer<ReturnType<typeof addresseeSchema>>;
export type PersonalValues = z.infer<ReturnType<typeof personalSchema>>;
export type DetailsValues = z.infer<ReturnType<typeof detailsSchema>>;
