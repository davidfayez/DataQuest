import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adminKeys, apiClient } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '@/shared/ui/DataTable';

export interface LookupBase {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
}

export interface CountryDto extends LookupBase {
  code: string;
  /** International calling prefix, e.g. +20. */
  phoneCode: string;
  /** Codes of the currencies mapped to this country, from the list endpoint. */
  currencyCodes: string[];
}

export interface CurrencyDto extends LookupBase {
  code: string;
  symbol: string;
  /** Countries this currency is offered in. */
  countryIds: string[];
}

/**
 * A body an application can be addressed to. Unlike the priced lookups this one only decides what
 * is suggested on a new application — the application stores the text, so editing this list never
 * changes one that already exists.
 */
export interface AddresseeDto extends LookupBase {
  /** Lifts the most-used entries above the alphabetical ordering. */
  sortOrder: number;
}

export interface TransactionTypeDto extends LookupBase {
  /** Unique reference, stored upper-case. */
  code: string;
  /** Required on save; null only on rows created before descriptions existed. */
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  /** The description in the reader's language, falling back to the other. */
  description?: string | null;
  /** Countries in which this transaction type is available. */
  countryIds: string[];
}

export interface SubTransactionTypeDto extends LookupBase {
  /** Unique reference, stored upper-case. */
  code: string;
  /** Required on save; null only on rows created before descriptions existed. */
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  /** The description in the reader's language, falling back to the other. */
  description?: string | null;
  transactionTypeId: string;
  /** Always a subset of the parent transaction type's countries. */
  countryIds: string[];
}

export interface AuthorityDto extends LookupBase {
  /** Unique reference, stored upper-case. */
  code: string;
  /** Required on save; null only on rows created before descriptions existed. */
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  /** The description in the reader's language, falling back to the other. */
  description?: string | null;
  countryId: string;
  subTransactionTypeIds: string[];
}

export interface RequiredFileFieldOptionDto {
  value: string;
  label: string;
  labelAr: string;
  labelEn: string;
}

export interface RequiredFileFieldDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  /** Numeric, matching the API's RequiredFieldType enum. */
  fieldType: number;
  isRequired: boolean;
  sortOrder: number;
  minLength: number | null;
  maxLength: number | null;
  pattern: string | null;
  minValue: number | null;
  maxValue: number | null;
  /** Numeric, matching the API's RequiredFieldDateRule enum. */
  dateRule: number;
  minDate: string | null;
  maxDate: string | null;
  options: RequiredFileFieldOptionDto[];
}

export interface RequiredFileDto {
  id: string;
  serviceTypeId: string;
  name: string;
  nameAr: string;
  nameEn: string;
  isMandatory: boolean;
  /** Effective per-document upload cap in bytes, already clamped to the platform maximum. */
  maxSizeBytes: number;
  maxFiles: number;
  fields: RequiredFileFieldDto[];
  /** Codes for the upload formats this document accepts, already resolved to the default if none. */
  allowedFileTypes: string[];
  /** The same set as file extensions, for an upload control's accept list. */
  allowedExtensions: string[];
}

export interface ServiceTypeCostDto {
  currencyId: string;
  currencyCode: string | null;
  cost: number;
  expressCost: number;
}

export interface ServiceTypeDto extends LookupBase {
  /** Unique reference, stored upper-case. */
  code: string;
  verificationAuthorityId: string;
  subTransactionTypeId: string;
  description: string | null;
  descriptionAr: string | null;
  descriptionEn: string | null;
  executionTimeDays: number;
  cost: number;
  enableExpress: boolean;
  expressCost: number;
  /** Admin-authored note beside the express toggle; null means use the app's default wording. */
  expressNote: string | null;
  expressNoteAr: string | null;
  expressNoteEn: string | null;
  showOnLanding: boolean;
  costs: ServiceTypeCostDto[];
  requiredFiles: RequiredFileDto[];
  /** Locale codes the result may be issued in; drives the applicant's output-language list. */
  outputLanguages: string[];
}

export interface ListParams {
  page?: number;
  /** Column to order by, named as the table knows it. Sorting runs on the server. */
  sortBy?: string;
  sortDescending?: boolean;
  /** Overrides the default page size — used by selectors that need the whole list at once. */
  pageSize?: number;
  search?: string;
  isActive?: boolean;
  countryId?: string;
  transactionTypeId?: string;
  verificationAuthorityId?: string;
}

function toQuery(params: ListParams): Record<string, string | number | boolean | undefined> {
  return {
    page: params.page ?? 1,
    pageSize: params.pageSize ?? 25,
    search: params.search || undefined,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    isActive: params.isActive,
    countryId: params.countryId,
    transactionTypeId: params.transactionTypeId,
    verificationAuthorityId: params.verificationAuthorityId,
  };
}

/** Generic list hook. Every lookup shares the same paging, search and language-keyed cache. */
function useLookupList<T>(
  path: string,
  keyFactory: (params: unknown, lang: string) => readonly unknown[],
  params: ListParams,
) {
  const lang = useLanguage();

  return useQuery({
    queryKey: keyFactory(params, lang),
    queryFn: () => apiClient.get<PagedResult<T>>(`admin/lookups/${path}`, {
        query: toQuery(params),
        language: lang,
      }),
  });
}

export const useCountries = (params: ListParams) =>
  useLookupList<CountryDto>('countries', adminKeys.countries, params);
export const useCurrencies = (params: ListParams) =>
  useLookupList<CurrencyDto>('currencies', adminKeys.currencies, params);
export const useAddressees = (params: ListParams) =>
  useLookupList<AddresseeDto>('addressees', adminKeys.addressees, params);
export const useTransactionTypes = (params: ListParams) =>
  useLookupList<TransactionTypeDto>('transaction-types', adminKeys.transactionTypes, params);
export const useSubTransactionTypes = (params: ListParams) =>
  useLookupList<SubTransactionTypeDto>('sub-transaction-types', adminKeys.subTransactionTypes, params);
export const useAuthorities = (params: ListParams) =>
  useLookupList<AuthorityDto>('authorities', adminKeys.authorities, params);
export const useServiceTypes = (params: ListParams) =>
  useLookupList<ServiceTypeDto>('service-types', adminKeys.serviceTypes, params);

/** Saves a lookup. The API upserts on the presence of an id. */
export function useSaveLookup<TBody, TResult>(path: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: TBody) => apiClient.post<TResult>(`admin/lookups/${path}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin'] }),
  });
}

/** Outcome of a delete: 0 removed, 1 deactivated because the record is still referenced. */
export type DeleteOutcome = 0 | 1;

export function useDeleteLookup(path: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`admin/lookups/${path}/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin'] }),
  });
}

/**
 * The currencies currently mapped to a country. Uses the admin route — the applicant-facing
 * `countries/{id}/currencies` sits on the Applicant policy and refuses an admin token.
 */
export function useCountryCurrencies(countryId: string | undefined) {
  const lang = useLanguage();

  return useQuery({
    queryKey: adminKeys.countryCurrencies(countryId ?? 'none', lang),
    queryFn: () =>
      apiClient.get<CurrencyDto[]>(`admin/lookups/countries/${countryId}/currencies`, {
        language: lang,
      }),
    enabled: Boolean(countryId),
  });
}

/**
 * Replaces a country's currency set. The CountriesTab dialog does this inline after upserting the
 * country, but this stays available for any caller that only needs to change the mapping.
 */
export function useSetCountryCurrencies(countryId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (currencyIds: string[]) =>
      apiClient.put<CurrencyDto[]>(`admin/lookups/countries/${countryId}/currencies`, {
        currencyIds,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin'] }),
  });
}
