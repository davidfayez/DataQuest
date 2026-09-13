import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';
import { env } from '@/shared/config/env';

export type FooterLinkVisibility = 'Everyone' | 'SignedIn' | 'SignedOut';

export interface FooterLinkDto {
  id: string;
  label: string;
  /** As typed by an administrator; `hrefFor` decides how to render it. */
  url: string;
  visibility: FooterLinkVisibility;
  sortOrder: number;
}

export interface FooterLogoDto {
  id: string;
  alt: string;
  /** API-relative, e.g. `content/footer/logos/{id}/image`. */
  imageUrl: string;
  /** Null for a mark that is not a link. */
  url: string | null;
  sortOrder: number;
}

export interface FooterContentDto {
  subtitle: string;
  exploreHeading: string;
  accountHeading: string;
  organisationHeading: string;
  explore: FooterLinkDto[];
  account: FooterLinkDto[];
  organisation: FooterLinkDto[];
  logos: FooterLogoDto[];
}

/**
 * The footer's copy and links. Public and long-cached: it is on every page and changes rarely.
 *
 * Keyed by language, because the labels come back already resolved — switching locale must fetch
 * that locale's copy rather than serving the previous one from cache.
 */
export function useFooterContent() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'footer', language] as const,
    // The language is passed explicitly rather than left to the client's getter: the two can
    // disagree mid-switch, which would file an English response under the Arabic key.
    queryFn: () => apiClient.get<FooterContentDto>('content/footer', { language }),
    staleTime: 10 * 60 * 1000,
    retry: false,
  });
}

/** Turns an API-relative image path into something an `img` tag can load. */
export function footerLogoSrc(imageUrl: string): string {
  const base = env.apiBaseUrl.endsWith('/') ? env.apiBaseUrl : `${env.apiBaseUrl}/`;
  return `${base}${imageUrl}`;
}

/**
 * How one stored address should be rendered.
 *
 * An administrator types what they mean — a full address, an anchor, or an app path — and the
 * language prefix is added here rather than stored, so today's language is never baked into a row.
 */
export function hrefFor(url: string, lang: string): { href: string; external: boolean; to?: string } {
  const value = url.trim();

  if (/^https?:\/\//i.test(value)) {
    return { href: value, external: true };
  }

  if (value.startsWith('#')) {
    return { href: `/${lang}${value}`, external: false };
  }

  return { href: `/${lang}${value}`, external: false, to: `/${lang}${value}` };
}
