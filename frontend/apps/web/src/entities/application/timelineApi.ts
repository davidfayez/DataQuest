import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, queryKeys } from '@/shared/api/client';
import type { ApplicationStatus } from './types';

export enum TimelineEntryKind {
  Comment = 0,
  StatusChange = 1,
  FileUploaded = 2,
  ResultAttached = 3,
}

export enum ActorType {
  System = 0,
  Applicant = 1,
  Admin = 2,
}

export interface TimelineEntryDto {
  id: string;
  kind: TimelineEntryKind;
  kindName: string;
  authorType: ActorType;
  authorName: string | null;
  createdAtUtc: string;
  body: string | null;
  visibility: number | null;
  fromStatus: ApplicationStatus | null;
  toStatus: ApplicationStatus | null;
  fileName: string | null;
  fileId: string | null;
}

export interface ResultFileDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAtUtc: string;
  downloadUrl: string;
  /** What to call the file once it is saved: application, order and what it is. */
  downloadName: string;
}

/**
 * The merged activity feed. Internal admin comments are filtered out server-side, so anything
 * returned here is safe to render.
 */
export function useTimeline(applicationId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.timeline(applicationId ?? 'none'),
    queryFn: () => apiClient.get<TimelineEntryDto[]>(`applications/${applicationId}/timeline`),
    enabled: Boolean(applicationId),
  });
}

export function useApplicationResults(applicationId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.results(applicationId ?? 'none'),
    queryFn: () => apiClient.get<ResultFileDto[]>(`applications/${applicationId}/results`),
    enabled: Boolean(applicationId) && enabled,
  });
}

export function useAddComment(applicationId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: string) =>
      apiClient.post<unknown>(`applications/${applicationId}/comments`, { body }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.timeline(applicationId) }),
  });
}

export function useResubmitApplication(applicationId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => apiClient.post<unknown>(`applications/${applicationId}/resubmit`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.application(applicationId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.timeline(applicationId) });
      void queryClient.invalidateQueries({ queryKey: ['applications'] });
    },
  });
}
