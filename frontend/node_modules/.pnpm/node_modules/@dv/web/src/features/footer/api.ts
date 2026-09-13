import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

/**
 * Platforms the footer knows how to draw. Sent by name rather than by number so a value inserted
 * into the server's enum cannot silently repaint every icon.
 */
export type SocialPlatform =
  | 'Facebook'
  | 'Instagram'
  | 'LinkedIn'
  | 'YouTube'
  | 'Telegram'
  | 'Vk'
  | 'X'
  | 'TikTok'
  | 'WhatsApp'
  | 'Messenger';

export interface SocialLinkDto {
  id: string;
  platform: SocialPlatform;
  url: string;
  sortOrder: number;
}

/** The footer's two rows, each already filtered to the active links and ordered by the server. */
export interface FooterChannelsDto {
  followUs: SocialLinkDto[];
  messageUs: SocialLinkDto[];
}

/**
 * The channels an administrator configured. Public, so it is fetched without a session, and cached
 * for a while — the footer is on every page and these change about once a year.
 */
export function useFooterChannels() {
  return useQuery({
    queryKey: ['content', 'social-links'],
    queryFn: () => apiClient.get<FooterChannelsDto>('content/social-links'),
    staleTime: 10 * 60 * 1000,
  });
}
