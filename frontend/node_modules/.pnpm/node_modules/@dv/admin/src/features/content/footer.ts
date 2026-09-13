import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import { env } from '@/shared/config/env';
import type { Translations } from './api';

/**
 * Which footer column a link sits in. The API speaks names in both directions, so the numeric
 * values behind the enum never reach the wire.
 */
export type FooterColumn = 'Explore' | 'Account' | 'Organisation';

export const FOOTER_COLUMNS: readonly FooterColumn[] = ['Explore', 'Account', 'Organisation'];

/**
 * Who sees a row. Only the Account column uses anything but Everyone today, but it is stored on
 * every row so a marketing link could be hidden from signed-in visitors without a new concept.
 */
export type FooterLinkVisibility = 'Everyone' | 'SignedIn' | 'SignedOut';

export interface FooterLinkDto {
  id: string;
  column: FooterColumn;
  visibility: FooterLinkVisibility;
  labels: Translations;
  url: string;
  sortOrder: number;
  isActive: boolean;
}

export interface FooterLogoDto {
  id: string;
  alts: Translations;
  url: string | null;
  fileName: string | null;
  hasImage: boolean;
  /** False when it is active but has no file yet — the site leaves it out. */
  isShowable: boolean;
  sortOrder: number;
  isActive: boolean;
}

export interface FooterContentDto {
  subtitle: Translations;
  exploreHeading: Translations;
  accountHeading: Translations;
  organisationHeading: Translations;
  links: FooterLinkDto[];
  logos: FooterLogoDto[];
}

export interface UpsertFooterLinkBody {
  id?: string;
  column: FooterColumn;
  visibility: FooterLinkVisibility;
  labels: Translations;
  url: string;
  sortOrder: number;
  isActive: boolean;
}

/**
 * A mark is its image, an order and a status. `alts` and `url` are carried through unchanged when
 * editing a row that has them — the panel no longer asks for either, because a mark in this row is
 * presentational and the subtitle beside it already carries the meaning.
 */
export interface UpsertFooterLogoBody {
  id?: string;
  alts?: Translations;
  url?: string | null;
  sortOrder: number;
  isActive: boolean;
}

/** Each field is optional; omitting one leaves the stored value alone. */
export interface UpdateFooterHeadingsBody {
  subtitle?: Translations;
  exploreHeading?: Translations;
  accountHeading?: Translations;
  organisationHeading?: Translations;
}

const footerKey = ['admin', 'content', 'footer'] as const;

export function useAdminFooterContent() {
  return useQuery({
    queryKey: footerKey,
    queryFn: () => apiClient.get<FooterContentDto>('admin/content/footer'),
  });
}

function useInvalidateFooter() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: footerKey });
}

export function useSaveFooterLink() {
  const invalidate = useInvalidateFooter();

  return useMutation({
    mutationFn: (body: UpsertFooterLinkBody) =>
      apiClient.post<FooterLinkDto>('admin/content/footer/links', body),
    onSuccess: invalidate,
  });
}

export function useDeleteFooterLink() {
  const invalidate = useInvalidateFooter();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/footer/links/${id}`),
    onSuccess: invalidate,
  });
}

/**
 * Saves a mark and its image as one action.
 *
 * The image needs a row to belong to, so this is two calls to the API and there is a moment between
 * them. That moment used to be visible and permanent: the created row appeared in the list, and if
 * the upload then failed it stayed there for ever as a mark "needing an image" — left behind by a
 * save the operator had watched fail. So the row is only published to the cache once the whole
 * thing has settled, and a row this call created is removed again if its image never arrives.
 *
 * An existing row is never removed: it was there before this call and its own image is untouched
 * until a new one lands.
 */
export function useSaveFooterLogoWithImage() {
  const invalidate = useInvalidateFooter();

  return useMutation({
    mutationFn: async ({ body, file }: { body: UpsertFooterLogoBody; file: File | null }) => {
      const saved = await apiClient.post<FooterLogoDto>('admin/content/footer/logos', body);
      if (!file) return saved;

      const form = new FormData();
      form.append('file', file);

      try {
        // `upload` rather than `post`: it leaves the Content-Type unset so the browser writes the
        // multipart boundary itself.
        return await apiClient.upload<FooterLogoDto>(
          `admin/content/footer/logos/${saved.id}/image`,
          form,
        );
      } catch (error) {
        if (!body.id) {
          // Created a moment ago by this call, so removing it restores what was there before. A
          // failure to clean up is not worth reporting over the error that caused it.
          await apiClient
            .delete<void>(`admin/content/footer/logos/${saved.id}`)
            .catch(() => undefined);
        }

        throw error;
      }
    },
    // Settled, not success: after a rolled-back attempt the list has to be re-read too.
    onSettled: invalidate,
  });
}

export function useDeleteFooterLogo() {
  const invalidate = useInvalidateFooter();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/footer/logos/${id}`),
    onSuccess: invalidate,
  });
}

export function useUpdateFooterHeadings() {
  const invalidate = useInvalidateFooter();

  return useMutation({
    mutationFn: (body: UpdateFooterHeadingsBody) =>
      apiClient.put<void>('admin/content/footer/headings', body),
    onSuccess: invalidate,
  });
}

/** Where the public site serves a mark's image, for the editor's own preview. */
export function footerLogoSrc(id: string): string {
  const base = env.apiBaseUrl.endsWith('/') ? env.apiBaseUrl : `${env.apiBaseUrl}/`;
  return `${base}content/footer/logos/${id}/image`;
}
