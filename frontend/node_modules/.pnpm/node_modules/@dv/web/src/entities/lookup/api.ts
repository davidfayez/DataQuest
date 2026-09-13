import { apiClient, queryKeys } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import { useQuery } from '@tanstack/react-query';

export interface CountryDto {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
}

export interface CurrencyDto {
  id: string;
  code: string;
  symbol: string;
  name: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
}

/** A body an application can be addressed to, offered as a suggestion rather than a constraint. */
export interface AddresseeDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

export function useCountries() {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.countries(lang),
    queryFn: () => apiClient.get<CountryDto[]>('countries', { language: lang }),
  });
}

/**
 * The addressee list offered on a new application. An empty list is a valid answer — the field
 * falls back to plain text — so a failure here must not block the step.
 */
export function useAddressees() {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.addressees(lang),
    queryFn: () => apiClient.get<AddresseeDto[]>('addressees', { language: lang }),
  });
}

export function useCountryCurrencies(countryId: string | undefined) {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.currencies(countryId ?? 'none', lang),
    queryFn: () => apiClient.get<CurrencyDto[]>(`countries/${countryId}/currencies`, { language: lang }),
    // The currency list is meaningless until a country is chosen.
    enabled: Boolean(countryId),
  });
}
