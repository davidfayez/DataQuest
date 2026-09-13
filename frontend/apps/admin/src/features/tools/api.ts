import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

/** A map of language code → text, e.g. { en: "…", ar: "…" }. */
export type Translations = Record<string, string>;

export type ToolKind = 'Video' | 'Image';

export interface AdminToolDto {
  id: string;
  kind: ToolKind;
  names: Translations;
  descriptions: Translations;
  videoUrl: string | null;
  imageFileName: string | null;
  hasImage: boolean;
  sortOrder: number;
  isPublished: boolean;
}

export interface UpsertToolBody {
  id?: string;
  kind: ToolKind;
  names: Translations;
  descriptions: Translations;
  videoUrl: string | null;
  sortOrder: number;
  isPublished: boolean;
}

const toolsKey = ['admin', 'content', 'tools'] as const;

export function useAdminTools() {
  return useQuery({
    queryKey: toolsKey,
    queryFn: () => apiClient.get<AdminToolDto[]>('admin/content/tools'),
  });
}

function useInvalidateTools() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: toolsKey });
}

/** Create or update an entry. The API upserts on the presence of an id. */
export function useSaveTool() {
  const invalidate = useInvalidateTools();

  return useMutation({
    mutationFn: (body: UpsertToolBody) => apiClient.post<AdminToolDto>('admin/content/tools', body),
    onSuccess: invalidate,
  });
}

export function useDeleteTool() {
  const invalidate = useInvalidateTools();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/tools/${id}`),
    onSuccess: invalidate,
  });
}

/**
 * Attaches a picture to an image entry. Sent as multipart, so it bypasses the JSON client and
 * carries the bearer token by hand.
 */
export function useUploadToolImage() {
  const invalidate = useInvalidateTools();

  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const body = new FormData();
      body.append('file', file);
      return apiClient.upload<AdminToolDto>(`admin/content/tools/${id}/image`, body);
    },
    onSuccess: invalidate,
  });
}
