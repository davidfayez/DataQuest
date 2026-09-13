import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

/** A map of language code → text, e.g. { en: "…", ar: "…" }. */
export type Translations = Record<string, string>;

export interface LandingFeatureDto {
  id: string;
  icon: string;
  titles: Translations;
  bodies: Translations;
  sortOrder: number;
  isPublished: boolean;
}

/** One figure in the landing hero's statistics strip, with every language's copy. */
export interface LandingStatDto {
  id: string;
  /** Icon key, or null when the figure is drawn without one. */
  icon: string | null;
  /** The figure as it should read, e.g. '48,000+' or '7 days'. */
  values: Translations;
  labels: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export type LandingSectionLayout = 'Grid' | 'Carousel';

/** How the "how it works" cards are arranged. Not per language. */
export interface UpdateStepsLayoutBody {
  layout: LandingSectionLayout;
  columns: number;
}

export interface LandingStepDto {
  id: string;
  icon: string;
  titles: Translations;
  bodies: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export interface UpsertLandingStepBody {
  id?: string;
  icon: string;
  titles: Translations;
  bodies: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export interface LandingTrustEntryDto {
  id: string;
  /** Icon key, or null to draw the strip's neutral default mark. */
  icon: string | null;
  names: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export interface UpsertLandingTrustEntryBody {
  id?: string;
  icon: string | null;
  names: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export interface LandingContentDto {
  eyebrow: Translations;
  title: Translations;
  features: LandingFeatureDto[];
  stats: LandingStatDto[];
  trustTitle: Translations;
  trustedBy: LandingTrustEntryDto[];
  howEyebrow: Translations;
  howTitle: Translations;
  steps: LandingStepDto[];
  howLayout: LandingSectionLayout;
  howColumns: number;
  ctaTitle: Translations;
  ctaBody: Translations;
  ctaButton: Translations;
  /** Not per language: one destination serves every locale. */
  ctaLink: string;
}

export interface UpsertLandingStatBody {
  id?: string;
  /** Null or empty means "no icon". */
  icon: string | null;
  values: Translations;
  labels: Translations;
  sortOrder: number;
  isPublished: boolean;
}

export interface UpsertLandingFeatureBody {
  id?: string;
  icon: string;
  titles: Translations;
  bodies: Translations;
  sortOrder: number;
  isPublished: boolean;
}

/**
 * Whichever headings the caller is editing. Each is optional and omitting one leaves the stored
 * value alone, so a page saves only the copy it owns.
 */
export interface UpdateHeadingBody {
  eyebrow?: Translations;
  title?: Translations;
  trustTitle?: Translations;
  howEyebrow?: Translations;
  howTitle?: Translations;
  ctaTitle?: Translations;
  ctaBody?: Translations;
  ctaButton?: Translations;
  ctaLink?: string;
}

const landingKey = ['admin', 'content', 'landing'] as const;

/** The full landing section (heading + every card, published or not) for the editor. */
export function useAdminLandingContent() {
  return useQuery({
    queryKey: landingKey,
    queryFn: () => apiClient.get<LandingContentDto>('admin/content/landing'),
  });
}

function useInvalidateLanding() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: landingKey });
}

/** Create or update a card. The API upserts on the presence of an id. */
export function useSaveLandingFeature() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpsertLandingFeatureBody) =>
      apiClient.post<LandingFeatureDto>('admin/content/landing/features', body),
    onSuccess: invalidate,
  });
}

export function useDeleteLandingFeature() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/landing/features/${id}`),
    onSuccess: invalidate,
  });
}

/** Create or update a statistic. The API upserts on the presence of an id. */
export function useSaveLandingStat() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpsertLandingStatBody) =>
      apiClient.post<LandingStatDto>('admin/content/landing/stats', body),
    onSuccess: invalidate,
  });
}

export function useDeleteLandingStat() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/landing/stats/${id}`),
    onSuccess: invalidate,
  });
}

/** Create or update one body in the trust strip. The API upserts on the presence of an id. */
export function useSaveLandingTrustEntry() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpsertLandingTrustEntryBody) =>
      apiClient.post<LandingTrustEntryDto>('admin/content/landing/trusted-by', body),
    onSuccess: invalidate,
  });
}

export function useDeleteLandingTrustEntry() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/landing/trusted-by/${id}`),
    onSuccess: invalidate,
  });
}

/** Create or update one step. The API upserts on the presence of an id. */
export function useSaveLandingStep() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpsertLandingStepBody) =>
      apiClient.post<LandingStepDto>('admin/content/landing/steps', body),
    onSuccess: invalidate,
  });
}

export function useDeleteLandingStep() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/landing/steps/${id}`),
    onSuccess: invalidate,
  });
}

/** Saves the arrangement of the "how it works" cards. */
export function useUpdateStepsLayout() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpdateStepsLayoutBody) =>
      apiClient.put<void>('admin/content/landing/steps/layout', body),
    onSuccess: invalidate,
  });
}

export function useUpdateLandingHeading() {
  const invalidate = useInvalidateLanding();

  return useMutation({
    mutationFn: (body: UpdateHeadingBody) =>
      apiClient.put<void>('admin/content/landing/heading', body),
    onSuccess: invalidate,
  });
}

/**
 * Icon keys the web knows how to render, offered as a dropdown in the editor.
 *
 * Kept in step with the map in the applicant app's `landingIcons.ts`: a key offered here that the
 * site does not know draws no glyph, which is a silent disappointment rather than an error.
 */
export const LANDING_ICON_KEYS = [
  'timeline',
  'wallet',
  'language',
  'security',
  'shield',
  'document',
  'authority',
  'turnaround',
  'users',
  'globe',
  'check',
  'award',
  'mail',
  'file',
  'search',
] as const;
