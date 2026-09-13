import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import { env } from '@/shared/config/env';

export interface ToolDto {
  id: string;
  kind: 'Video' | 'Image';
  name: string;
  description: string;
  videoUrl: string | null;
  /** API-relative path, e.g. `content/tools/{id}/image`. Null for a video entry. */
  imageUrl: string | null;
  sortOrder: number;
}

/**
 * Published tools and guides in the caller's language. Public, so it is fetched without a session
 * and stays cached for a while — this content changes rarely.
 */
export function useTools() {
  return useQuery({
    queryKey: ['content', 'tools'],
    queryFn: () => apiClient.get<ToolDto[]>('content/tools'),
    staleTime: 5 * 60 * 1000,
  });
}

/** Turns the API-relative image path into something an `img` tag can load directly. */
export function toolImageSrc(imageUrl: string): string {
  const base = env.apiBaseUrl.endsWith('/') ? env.apiBaseUrl : `${env.apiBaseUrl}/`;
  return `${base}${imageUrl}`;
}

/**
 * Converts a YouTube or Vimeo watch link into its embeddable form, so the video plays in place.
 * Anything else returns null and is offered as a plain link instead — only hosts we recognise are
 * ever put inside an iframe.
 */
export function toEmbedUrl(videoUrl: string): string | null {
  let url: URL;
  try {
    url = new URL(videoUrl);
  } catch {
    return null;
  }

  const host = url.hostname.replace(/^www\./, '').toLowerCase();

  if (host === 'youtu.be') {
    const id = url.pathname.slice(1);
    return id ? `https://www.youtube.com/embed/${id}` : null;
  }

  if (host === 'youtube.com' || host === 'm.youtube.com') {
    if (url.pathname === '/watch') {
      const id = url.searchParams.get('v');
      return id ? `https://www.youtube.com/embed/${id}` : null;
    }
    // Already an embed or a /shorts link.
    const match = url.pathname.match(/^\/(embed|shorts)\/([\w-]+)/);
    return match ? `https://www.youtube.com/embed/${match[2]}` : null;
  }

  if (host === 'vimeo.com') {
    const id = url.pathname.split('/').filter(Boolean)[0];
    return id && /^\d+$/.test(id) ? `https://player.vimeo.com/video/${id}` : null;
  }

  return null;
}
