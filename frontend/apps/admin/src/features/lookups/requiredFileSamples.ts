import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { RequiredFileSampleDto } from './api';

/** Whose required documents the reference files belong to. */
export type ReferenceFileScope = 'serviceType' | 'paymentMethod';

const SCOPES: Record<ReferenceFileScope, { base: string; invalidate: readonly string[] | null }> = {
  serviceType: { base: 'admin/lookups/service-types', invalidate: ['admin', 'service-types'] },
  // Nothing refreshed: the payment method page edits from its own query, and refetching that
  // mid-edit would overwrite whatever has not been saved yet. The panel keeps its own list.
  paymentMethod: { base: 'admin/payment-methods', invalidate: null },
};

export interface UploadRequiredFileSampleInput {
  requiredFileId: string;
  labelAr: string;
  labelEn: string;
  file: File;
}

/** Attaches one labelled reference file to a saved required document. */
export function useUploadRequiredFileSample(scope: ReferenceFileScope = 'serviceType') {
  const queryClient = useQueryClient();
  const { base, invalidate } = SCOPES[scope];

  return useMutation({
    mutationFn: ({ requiredFileId, labelAr, labelEn, file }: UploadRequiredFileSampleInput) => {
      const body = new FormData();
      body.append('labelAr', labelAr.trim());
      body.append('labelEn', labelEn.trim());
      body.append('file', file);
      // `upload` rather than `post`: it leaves the Content-Type unset so the browser writes the
      // multipart boundary itself.
      return apiClient.upload<RequiredFileSampleDto>(
        `${base}/required-files/${requiredFileId}/samples`,
        body,
      );
    },
    onSuccess: () => {
      if (invalidate) void queryClient.invalidateQueries({ queryKey: invalidate });
    },
  });
}

/** Removes a reference file, and its bytes. */
export function useDeleteRequiredFileSample(scope: ReferenceFileScope = 'serviceType') {
  const queryClient = useQueryClient();
  const { base, invalidate } = SCOPES[scope];

  return useMutation({
    mutationFn: (sampleId: string) => apiClient.delete<void>(`${base}/samples/${sampleId}`),
    onSuccess: () => {
      if (invalidate) void queryClient.invalidateQueries({ queryKey: invalidate });
    },
  });
}

/**
 * A reference file's bytes. Fetched as a blob because the endpoint needs the in-memory access
 * token, which a plain `<img src>` or `<iframe src>` cannot carry.
 */
export function fetchRequiredFileSample(
  sampleId: string,
  scope: ReferenceFileScope = 'serviceType',
): Promise<Blob> {
  return apiClient.getBlob(`${SCOPES[scope].base}/samples/${sampleId}/file`);
}
