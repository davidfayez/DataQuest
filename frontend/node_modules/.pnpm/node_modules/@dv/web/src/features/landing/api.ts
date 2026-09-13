import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';

export interface LandingFeature {
  id: string;
  icon: string;
  title: string;
  body: string;
  sortOrder: number;
  isPublished: boolean;
}

/** One figure in the hero statistics strip, already resolved to the active language. */
export interface LandingStat {
  id: string;
  /** Icon key the hero maps to a glyph. Null draws the figure without one. */
  icon: string | null;
  /** The figure as it should read, e.g. '48,000+' or '7 days'. */
  value: string;
  label: string;
  sortOrder: number;
}

/** One body named in the "trusted for verification with" strip. */
export interface LandingTrustEntry {
  id: string;
  /** Icon key, or null to draw the strip's neutral default mark. */
  icon: string | null;
  name: string;
  sortOrder: number;
}

/** One step in the "how it works" section. The card's number comes from its position. */
export interface LandingStep {
  id: string;
  icon: string;
  title: string;
  body: string;
  sortOrder: number;
}

export interface LandingContent {
  eyebrow: string;
  title: string;
  features: LandingFeature[];
  /** Empty when an administrator has published none, which hides the strip. */
  stats: LandingStat[];
  /** Heading above the trust strip. */
  trustTitle: string;
  /** Empty hides the whole trust strip, heading included. */
  trustedBy: LandingTrustEntry[];
  /** Eyebrow and title above the "how it works" steps. */
  howEyebrow: string;
  howTitle: string;
  /** Empty hides the whole section, heading included. */
  steps: LandingStep[];
  /** 'Grid' or 'Carousel'. */
  howLayout: string;
  /** Cards abreast on a wide screen; narrow screens always stack. */
  howColumns: number;
  /** The closing call to action. */
  ctaTitle: string;
  ctaBody: string;
  ctaButton: string;
  /** Where its button goes: an in-app path, an anchor, or a full https:// address. */
  ctaLink: string;
}

/**
 * Public, admin-managed copy for the landing "features" section. Anonymous — no token required.
 * The section falls back to the built-in translated copy when this is unavailable, so a failed
 * request never blanks the marketing page. Keyed by the active language so switching locale fetches
 * the admin's translation for that language rather than serving the previous one from cache.
 */
export function useLandingContent() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'landing', language] as const,
    queryFn: () => apiClient.get<LandingContent>('content/landing', { language }),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export interface LandingService {
  id: string;
  title: string;
  description: string | null;
  cost: number;
  expressCost: number;
  enableExpress: boolean;
  executionTimeDays: number;
  currencySymbol: string | null;
}

/**
 * Public, admin-managed verification "tracks" for the landing page, sourced from the active
 * service types flagged to show. Falls back to the built-in copy when unavailable.
 */
export function useLandingServices() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'services', language] as const,
    queryFn: () => apiClient.get<LandingService[]>('content/services', { language }),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** One country marked on the coverage map. */
export interface CoverageCountry {
  id: string;
  /** ISO 3166-1 alpha-2 — what places the marker on the map. */
  code: string;
  name: string;
  /** `Spot` or `Flag`, chosen per country in the admin panel. */
  marker: string;
}

export interface CoverageContent {
  title: string;
  subtitle: string;
  countries: CoverageCountry[];
}

/**
 * Public, admin-managed coverage section. Anonymous, and keyed by language like the rest: the
 * country names come back already resolved, so switching locale must refetch rather than reuse the
 * previous language's list.
 */
export function useCoverageContent() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'coverage', language] as const,
    queryFn: () => apiClient.get<CoverageContent>('content/coverage', { language }),
    staleTime: 5 * 60_000,
    retry: false,
  });
}
