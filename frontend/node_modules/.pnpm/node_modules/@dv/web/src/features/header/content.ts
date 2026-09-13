import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';

/** Who an entry is shown to, applied here rather than on the server — see the footer for why. */
export type HeaderLinkVisibility = 'Everyone' | 'SignedIn' | 'SignedOut';

export interface HeaderLinkDto {
  id: string;
  /** A built-in entry's key, or null for one an operator added (which carries its own label). */
  key: string | null;
  /** The operator's wording, or null to use the app's own translation. */
  label: string | null;
  url: string;
  visibility: HeaderLinkVisibility;
  sortOrder: number;
}

export interface HeaderContentDto {
  links: HeaderLinkDto[];
}

/**
 * What the header carries when the API cannot be reached.
 *
 * The footer degrades to nothing in that case, which is survivable; a header that degrades to
 * nothing would strand the reader with no way around the site, so this mirrors the seeded rows.
 */
export const DEFAULT_HEADER_LINKS: HeaderLinkDto[] = [
  { id: 'home', key: 'home', label: null, url: '/', visibility: 'Everyone', sortOrder: 0 },
  { id: 'how', key: 'how', label: null, url: '#how', visibility: 'SignedOut', sortOrder: 1 },
  {
    id: 'services',
    key: 'services',
    label: null,
    url: '#services',
    visibility: 'SignedOut',
    sortOrder: 2,
  },
  {
    id: 'coverage',
    key: 'coverage',
    label: null,
    url: '#coverage',
    visibility: 'SignedOut',
    sortOrder: 3,
  },
  {
    id: 'knowledge',
    key: 'knowledge',
    label: null,
    url: '/tools',
    visibility: 'Everyone',
    sortOrder: 4,
  },
  {
    id: 'contact',
    key: 'contact',
    label: null,
    url: '/contact',
    visibility: 'Everyone',
    sortOrder: 5,
  },
];

/**
 * The header's entries as an administrator arranged them.
 *
 * Keyed by language like the rest of the admin-managed copy, and never retried: the header falls
 * back to the built-in arrangement rather than leaving the bar empty while a request fails.
 */
export function useHeaderContent() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'header', language] as const,
    queryFn: () => apiClient.get<HeaderContentDto>('content/header', { language }),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * The entries to draw for this visitor, in order.
 *
 * Visibility is applied here because the response is fetched anonymously and shared by every
 * reader — the same reason the footer filters in the browser.
 */
export function useHeaderLinks(isSignedIn: boolean): HeaderLinkDto[] {
  const content = useHeaderContent();
  const links = content.data?.links?.length ? content.data.links : DEFAULT_HEADER_LINKS;

  return links
    .filter((link) =>
      link.visibility === 'Everyone'
        ? true
        : link.visibility === 'SignedIn'
          ? isSignedIn
          : !isSignedIn,
    )
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder);
}
