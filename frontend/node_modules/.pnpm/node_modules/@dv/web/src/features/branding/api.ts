import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import { env } from '@/shared/config/env';

export interface BrandingDto {
  /** False when nothing has been uploaded, so the bundled mark applies. */
  hasLogo: boolean;
  fileName: string | null;
  /** Changes whenever the logo is replaced; appended to the image URL to defeat caching. */
  version: string;
}

/**
 * The uploaded logo, if there is one. The answer is a few bytes and the image itself is cached
 * against its version token, so asking again is cheap: after a minute, returning to the tab
 * re-checks, and a logo replaced in the admin panel shows without waiting for a full reload.
 */
export function useBranding() {
  return useQuery({
    queryKey: ['content', 'branding'],
    queryFn: () => apiClient.get<BrandingDto>('content/branding'),
    staleTime: 60 * 1000,
    retry: false,
  });
}

/**
 * The URL to draw, or null to fall back to the bundled mark.
 *
 * The version is part of the URL rather than a cache header, so replacing the logo shows the new
 * one immediately instead of whenever the old copy happens to expire.
 */
export function logoSrc(branding: BrandingDto | undefined): string | null {
  if (!branding?.hasLogo) return null;

  const base = env.apiBaseUrl.endsWith('/') ? env.apiBaseUrl : `${env.apiBaseUrl}/`;
  return `${base}content/logo?v=${encodeURIComponent(branding.version)}`;
}
