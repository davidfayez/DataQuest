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
 * The uploaded logo, if there is one. Public and long-cached: the brand mark is on every page and
 * changes about once a year, and a replaced logo is picked up through the version token rather
 * than by re-asking.
 */
export function useBranding() {
  return useQuery({
    queryKey: ['content', 'branding'],
    queryFn: () => apiClient.get<BrandingDto>('content/branding'),
    staleTime: 10 * 60 * 1000,
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
