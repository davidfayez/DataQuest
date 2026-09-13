import { useCallback, useState } from 'react';
import {
  NameLanguageType,
  type ApplicationDetailsDto,
  type ApplicationWriteModel,
} from '@/entities/application/types';
import { DEFAULT_PHONE_COUNTRY, findPhoneCountry } from '@/shared/lib/phoneCountries';
import type { AddresseeValues, DetailsValues, PersonalValues } from './schemas';

export const WIZARD_STEPS = [
  'addressee',
  'personal',
  'details',
  'summary',
  'files',
  'review',
] as const;

export type WizardStep = (typeof WIZARD_STEPS)[number];

export interface WizardState {
  addressee: AddresseeValues | null;
  personal: PersonalValues | null;
  details: DetailsValues | null;
  /** Set once the draft has been saved, which is what makes file upload possible. */
  applicationId: string | null;
}

const EMPTY: WizardState = {
  addressee: null,
  personal: null,
  details: null,
  applicationId: null,
};

/**
 * Wizard state, held in memory for as long as the wizard is open and no longer.
 *
 * It was previously mirrored into sessionStorage so an accidental refresh would not cost the
 * applicant their typing. That protection was not worth what it did to the far more common path:
 * abandoning the wizard left the whole form behind, so the next "New application" opened
 * pre-filled with someone's earlier attempt. Worse, an abandoned session that had reached the
 * upload step carried its <c>applicationId</c> forward, and the next application the applicant
 * started would silently overwrite that draft instead of creating a new one.
 *
 * Starting a new application therefore always starts blank. Anyone who needs to stop partway has
 * "Save draft and exit" on every step, which persists to the server where it belongs.
 */
export function useWizardState() {
  const [state, setState] = useState<WizardState>(EMPTY);

  const patch = useCallback((changes: Partial<WizardState>) => {
    setState((previous) => ({ ...previous, ...changes }));
  }, []);

  /** Swaps the whole state at once — used when an existing application is loaded for editing. */
  const replace = useCallback((next: WizardState) => {
    setState(next);
  }, []);

  const reset = useCallback(() => setState(EMPTY), []);

  return { state, patch, replace, reset };
}

/**
 * Projects whatever has been collected so far into the request body.
 *
 * Deliberately tolerant of missing steps: "save as draft" can be pressed on any tab, and the
 * server stores a Draft with only the parts the applicant has actually reached. The addressee is
 * the one thing always required, because it is captured on the very first step.
 */
export function toWriteModel(state: WizardState): ApplicationWriteModel | null {
  const { addressee, personal, details } = state;
  if (!addressee?.addressedTo?.trim()) return null;

  const names = personal
    ? [
        {
          languageType: NameLanguageType.Arabic,
          firstName: personal.arabicName.firstName,
          middleName: personal.arabicName.middleName?.trim() || null,
          lastName: personal.arabicName.lastName,
        },
        {
          languageType: NameLanguageType.English,
          firstName: personal.englishName.firstName,
          middleName: personal.englishName.middleName?.trim() || null,
          lastName: personal.englishName.lastName,
        },
      ]
    : [];

  // A half-filled service row would fail validation, so only complete ones are sent.
  const services = (details?.services ?? [])
    .filter((service) => service.serviceTypeId)
    .map((service) => ({
      serviceTypeId: service.serviceTypeId,
      quantity: Number(service.quantity) || 1,
      languageCode: service.languageCode,
      isExpress: service.isExpress,
    }));

  return {
    addressedTo: addressee.addressedTo,
    birthDate: personal?.birthDate || null,
    applicantEmail: personal?.email?.trim() || null,
    // The three phone parts travel together: a number without its prefix is meaningless, and the
    // server refuses one that arrives on its own.
    applicantPhoneCountry: personal?.phoneNumber ? personal.phoneCountry : null,
    applicantPhoneCode: personal?.phoneNumber
      ? (findPhoneCountry(personal.phoneCountry)?.dialCode ?? null)
      : null,
    applicantPhoneNumber: personal?.phoneNumber?.trim() || null,
    names,
    transactionTypeId: details?.transactionTypeId || null,
    subTransactionTypeId: details?.subTransactionTypeId || null,
    verificationAuthorityId: details?.verificationAuthorityId || null,
    services,
  };
}

/**
 * Rebuilds wizard state from an application already on the server, so the same six steps can edit
 * an existing draft rather than only create a new one. The inverse of {@link toWriteModel}.
 */
export function fromApplication(application: ApplicationDetailsDto): WizardState {
  const nameOf = (languageType: NameLanguageType) => {
    const match = application.names.find((name) => name.languageType === languageType);
    return {
      languageType,
      firstName: match?.firstName ?? '',
      middleName: match?.middleName ?? '',
      lastName: match?.lastName ?? '',
    };
  };

  return {
    applicationId: application.id,
    addressee: { addressedTo: application.addressedTo },
    personal: {
      arabicName: nameOf(NameLanguageType.Arabic),
      englishName: nameOf(NameLanguageType.English),
      // The API returns a date-only value; the date input wants exactly that prefix.
      birthDate: application.birthDate?.slice(0, 10) ?? '',
      email: application.applicantEmail ?? '',
      // A draft saved before this step has no country stored; the picker opens on the default.
      phoneCountry: application.applicantPhoneCountry ?? DEFAULT_PHONE_COUNTRY,
      phoneNumber: application.applicantPhoneNumber ?? '',
    },
    // A draft saved before the services step has no cascade yet; the step opens empty.
    details: {
      transactionTypeId: application.transactionType?.id ?? '',
      subTransactionTypeId: application.subTransactionType?.id ?? '',
      verificationAuthorityId: application.verificationAuthority?.id ?? '',
      services: application.services.map((service) => ({
        serviceTypeId: service.serviceTypeId,
        quantity: service.quantity,
        languageCode: service.languageCode,
        isExpress: service.isExpress,
      })),
    },
  };
}
