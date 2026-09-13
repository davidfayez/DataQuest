import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ListParams } from '@/features/lookups/api';
import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/ui/DataTable';

/** Mirrors the API's SocialLinkPlacement — which row of the public footer a link sits in. */
export enum SocialLinkPlacement {
  FollowUs = 0,
  MessageUs = 1,
}

/**
 * Mirrors the API's SocialPlatform. A closed list, because the applicant site draws each platform's
 * own icon and brand colour from it — an operator picks a platform rather than supplying artwork.
 */
export enum SocialPlatform {
  Facebook = 0,
  Instagram = 1,
  LinkedIn = 2,
  YouTube = 3,
  Telegram = 4,
  Vk = 5,
  X = 6,
  TikTok = 7,
  WhatsApp = 8,
  Messenger = 9,
}

/** Display names, so the editor never shows a bare enum number. */
export const PLATFORM_LABELS: Record<SocialPlatform, string> = {
  [SocialPlatform.Facebook]: 'Facebook',
  [SocialPlatform.Instagram]: 'Instagram',
  [SocialPlatform.LinkedIn]: 'LinkedIn',
  [SocialPlatform.YouTube]: 'YouTube',
  [SocialPlatform.Telegram]: 'Telegram',
  [SocialPlatform.Vk]: 'VK',
  [SocialPlatform.X]: 'X',
  [SocialPlatform.TikTok]: 'TikTok',
  [SocialPlatform.WhatsApp]: 'WhatsApp',
  [SocialPlatform.Messenger]: 'Messenger',
};

export interface SocialLinkDto {
  id: string;
  placement: SocialLinkPlacement;
  placementName: string;
  platform: SocialPlatform;
  platformName: string;
  url: string;
  isActive: boolean;
  sortOrder: number;
  /** False when the link would not reach the footer — inactive, or with no address. */
  isShowable: boolean;
}

export interface UpsertSocialLinkInput {
  id?: string | null;
  placement: SocialLinkPlacement;
  platform: SocialPlatform;
  url: string;
  isActive: boolean;
  sortOrder: number;
}

export interface SocialLinkParams extends ListParams {
  placement?: SocialLinkPlacement;
}

const BASE = 'admin/social-links';

function toQuery(params: object): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }

  return search.toString();
}

export function useSocialLinks(params: SocialLinkParams) {
  return useQuery({
    queryKey: ['admin', 'social-links', params],
    queryFn: () => apiClient.get<PagedResult<SocialLinkDto>>(`${BASE}?${toQuery(params)}`),
  });
}

function useInvalidate() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['admin', 'social-links'] });
}

export function useSaveSocialLink() {
  const invalidate = useInvalidate();

  return useMutation({
    mutationFn: (input: UpsertSocialLinkInput) => apiClient.post<SocialLinkDto>(BASE, input),
    onSuccess: invalidate,
  });
}

export function useDeleteSocialLink() {
  const invalidate = useInvalidate();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
    onSuccess: invalidate,
  });
}
