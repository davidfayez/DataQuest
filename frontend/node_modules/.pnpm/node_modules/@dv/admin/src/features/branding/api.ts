import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import { env } from '@/shared/config/env';

export interface BrandingDto {
  /** False when nothing has been uploaded, so the bundled mark applies. */
  hasLogo: boolean;
  fileName: string | null;
  /** Changes whenever the logo is replaced; appended to the image URL to defeat caching. */
  version: string;
}

const brandingKey = ['content', 'branding'] as const;

/**
 * The uploaded logo, if there is one.
 *
 * Read from the public endpoint rather than the admin one, so the sidebar shows the brand on the
 * sign-in screen too — where there is no session to authorise an admin call.
 */
export function useBranding() {
  return useQuery({
    queryKey: brandingKey,
    queryFn: () => apiClient.get<BrandingDto>('content/branding'),
    staleTime: 10 * 60 * 1000,
    retry: false,
  });
}

function useInvalidateBranding() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: brandingKey });
}

export function useUploadLogo() {
  const invalidate = useInvalidateBranding();

  return useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append('file', file);
      // `upload` rather than `post`: it leaves the Content-Type unset so the browser writes the
      // multipart boundary itself.
      return apiClient.upload<BrandingDto>('admin/content/branding/logo', body);
    },
    onSuccess: invalidate,
  });
}

export function useDeleteLogo() {
  const invalidate = useInvalidateBranding();

  return useMutation({
    mutationFn: () => apiClient.delete<BrandingDto>('admin/content/branding/logo'),
    onSuccess: invalidate,
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
